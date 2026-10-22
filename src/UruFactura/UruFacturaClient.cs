using System.Security.Cryptography.X509Certificates;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;

namespace UruFactura;

/// <summary>
/// Cliente de facturación electrónica de la DGI (ver <see cref="IUruFacturaClient"/>).
/// </summary>
public sealed class UruFacturaClient : IUruFacturaClient
{
    private readonly UruFacturaConfig _config;
    private readonly IUruFacturaStore _store;
    private readonly IDgiClient _dgi;
    private readonly DgiSoapClient? _dgiPropio;
    private readonly X509Certificate2 _certificado;
    private readonly bool _certificadoPropio;
    private readonly TimeProvider _reloj;
    private bool _disposed;

    /// <summary>Crea el cliente. Valida la configuración y carga el certificado.</summary>
    /// <param name="config">Configuración.</param>
    /// <param name="store">
    /// Almacenamiento de CAE, numeración y CFE emitidos. Por defecto <see cref="InMemoryStore"/> (un solo
    /// proceso, sin persistencia); en producción use uno durable, como <c>EfUruFacturaStore</c>.
    /// </param>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> para la DGI (p. ej. de <c>IHttpClientFactory</c>). Al indicarlo, su handler
    /// debe tener el certificado de cliente TLS y la validación del servidor configurados.
    /// </param>
    /// <param name="dgiClient">Transporte propio hacia la DGI (en lugar del SOAP predeterminado).</param>
    /// <param name="reloj">Reloj para las fechas fiscales (por defecto, el del sistema).</param>
    /// <exception cref="UruFacturaException">Si la configuración no es válida.</exception>
    /// <exception cref="FirmaDigitalException">Si no se puede cargar el certificado o no tiene clave privada.</exception>
    public UruFacturaClient(
        UruFacturaConfig config,
        IUruFacturaStore? store = null,
        HttpClient? httpClient = null,
        IDgiClient? dgiClient = null,
        TimeProvider? reloj = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var errores = config.Validar();
        if (errores.Count > 0)
            throw new UruFacturaException($"Configuración inválida: {string.Join("; ", errores)}");
        if (httpClient is not null && dgiClient is not null)
            throw new ArgumentException("httpClient solo se usa con el transporte predeterminado, no con un dgiClient propio.", nameof(httpClient));

        _config = config;
        _store = store ?? new InMemoryStore();
        _reloj = reloj ?? TimeProvider.System;

        if (config.Certificado is { } certificado)
        {
            CertificadoLoader.ValidarClavePrivada(certificado);
            _certificado = certificado;
        }
        else
        {
            _certificado = CertificadoLoader.Cargar(config.RutaCertificado!, config.PasswordCertificado);
            _certificadoPropio = true;
        }

        try
        {
            _dgi = dgiClient ?? (_dgiPropio = new DgiSoapClient(config, _certificado, httpClient));
        }
        catch
        {
            if (_certificadoPropio)
                _certificado.Dispose();
            throw;
        }
    }

    // -----------------------------------------------------------------------
    // CAE
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async Task RegistrarCaeAsync(Cae cae, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cae);
        var errores = cae.Validar().ToList();
        if (errores.Count > 0)
            throw new CaeException($"CAE inválido: {string.Join("; ", errores)}");

        await _store.GuardarCaeAsync(cae, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Cae>> ObtenerCaesAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _store.ListarCaesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ObtenerAdvertenciasCaeAsync(
        int diasAlertaVencimiento = 7, decimal porcentajeAlertaUso = 80m, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var hoy = HoraUruguay.Hoy(_reloj);
        var caes = await _store.ListarCaesAsync(cancellationToken);
        return [.. caes.Select(c => c.ObtenerAdvertencia(hoy, diasAlertaVencimiento, porcentajeAlertaUso)).OfType<string>()];
    }

    // -----------------------------------------------------------------------
    // Emisión
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public IReadOnlyList<string> Validar(Cfe cfe)
    {
        ArgumentNullException.ThrowIfNull(cfe);
        return Resolver(cfe, HoraUruguay.Hoy(_reloj)).Errores;
    }

    /// <inheritdoc />
    public async Task<CfeEmitido> EmitirAsync(Cfe cfe, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfe);

        var ahora = HoraUruguay.Ahora(_reloj);
        ahora = ahora.AddTicks(-(ahora.Ticks % TimeSpan.TicksPerSecond)); // TmstFirma se informa en segundos
        var hoy = DateOnly.FromDateTime(ahora.DateTime);

        // Se valida antes de reservar: un CFE inválido no consume números.
        var (documento, errores) = Resolver(cfe, hoy);
        if (errores.Count > 0)
            throw new CfeValidationException(errores);

        var totales = documento.CalcularTotales();
        var cae = await _store.ReservarNumeroAsync(documento.Tipo, hoy, cancellationToken)
            ?? throw new CaeException(
                $"No hay un CAE vigente con números disponibles para {documento.Tipo}. Registre uno con RegistrarCaeAsync.");
        var numero = cae.UltimoNroUsado;

        var xml = CfeXml.Generar(documento, cae, numero, totales, ahora);
        var (firmado, codigoSeguridad) = FirmaDigital.Firmar(xml, _certificado, _config.AlgoritmoFirma);

        var emitido = new CfeEmitido(documento, cae, numero, totales, ahora, codigoSeguridad, firmado);
        await _store.GuardarCfesAsync([emitido], cancellationToken);
        return emitido;
    }

    /// <summary>El CFE con la fecha de emisión y el emisor efectivos, y sus errores de validación.</summary>
    private (Cfe Documento, List<string> Errores) Resolver(Cfe cfe, DateOnly hoy)
    {
        var documento = cfe with
        {
            FechaEmision = cfe.FechaEmision ?? hoy,
            Emisor = cfe.Emisor ?? _config.Emisor,
        };

        var errores = documento.Validar(
            documento.FechaEmision.Value,
            _config.ValorUnidadIndexada is { } ui ? ui * 5000m : null);

        if (cfe.Emisor is { } emisor)
        {
            errores.AddRange(emisor.Validar());
            if (emisor.Rut != _config.Emisor.Rut)
                errores.Add("Cfe.Emisor.Rut debe ser el RUT de la configuración (el del certificado).");
        }

        return (documento, errores);
    }

    // -----------------------------------------------------------------------
    // DGI
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public Task<AcuseSobre> EnviarAsync(CfeEmitido cfe, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cfe);
        return EnviarAsync([cfe], cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AcuseSobre> EnviarAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfes);
        if (cfes.Count is < 1 or > CfeSobre.MaxCfePorSobre)
            throw new UruFacturaException(
                $"Un sobre debe contener entre 1 y {CfeSobre.MaxCfePorSobre} CFE (recibidos: {cfes.Count}).");

        var sobre = CfeSobre.Generar(
            _config.Emisor.Rut,
            await _store.ReservarIdEnvioAsync(cancellationToken),
            _reloj.GetUtcNow(),
            _certificado.RawData,
            [.. cfes.Select(c => c.Xml)]);

        var acuse = await _dgi.EnviarSobreAsync(sobre, cancellationToken);

        foreach (var cfe in cfes)
        {
            if (acuse.Recibido)
            {
                cfe.IdReceptorDgi = acuse.IdReceptor;
                cfe.TokenDgi = acuse.Token;
                cfe.EstadoDgi = null;
                cfe.MensajeDgi = null;
            }
            else
                cfe.MensajeDgi = acuse.Mensaje;
        }
        await _store.GuardarCfesAsync(cfes, cancellationToken);

        return acuse with { Comprobantes = [.. cfes] };
    }

    /// <inheritdoc />
    public async Task<EstadoEnvio> ConsultarEnvioAsync(AcuseSobre acuse, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(acuse);
        if (!acuse.Recibido || string.IsNullOrWhiteSpace(acuse.IdReceptor) || string.IsNullOrWhiteSpace(acuse.Token))
            throw new InvalidOperationException("El sobre no fue recibido por la DGI: no hay resultado que consultar.");

        var estado = await _dgi.ConsultarEnvioAsync(acuse.IdReceptor, acuse.Token, cancellationToken);

        var actualizados = new List<CfeEmitido>();
        foreach (var cfe in acuse.Comprobantes)
        {
            if (estado.Comprobantes.FirstOrDefault(e => e.Corresponde(cfe)) is not { } resultado)
                continue;
            Actualizar(cfe, resultado);
            actualizados.Add(cfe);
        }
        if (actualizados.Count > 0)
            await _store.GuardarCfesAsync(actualizados, cancellationToken);

        return estado;
    }

    /// <inheritdoc />
    public async Task<EstadoComprobante> ConsultarCfeAsync(CfeEmitido cfe, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfe);

        var estado = await _dgi.ConsultarCfeAsync(cfe.Tipo, cfe.Serie, cfe.Numero, cancellationToken);
        Actualizar(cfe, estado);
        await _store.GuardarCfesAsync([cfe], cancellationToken);
        return estado;
    }

    private static void Actualizar(CfeEmitido cfe, EstadoComprobante estado)
    {
        cfe.EstadoDgi = estado.Estado;
        cfe.MensajeDgi = estado.Motivos.Count > 0 ? string.Join("; ", estado.Motivos) : null;
    }

    /// <inheritdoc />
    public async Task<AcuseReporte> EnviarReporteDiarioAsync(DateOnly fecha, int secEnvio = 0, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var cfes = await _store.ListarCfesAsync(fecha, cancellationToken);

        var reporte = ReporteDiario.Generar(
            _config.Emisor.Rut, fecha, secEnvio, await _store.ReservarIdEnvioAsync(cancellationToken), _reloj.GetUtcNow(), cfes);
        var (firmado, _) = FirmaDigital.Firmar(reporte, _certificado, _config.AlgoritmoFirma);

        var acuse = await _dgi.EnviarReporteAsync(firmado, cancellationToken);
        return acuse with { Fecha = fecha, CantidadCfe = cfes.Count };
    }

    // -----------------------------------------------------------------------

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dgiPropio?.Dispose();
        if (_certificadoPropio)
            _certificado.Dispose();
    }
}
