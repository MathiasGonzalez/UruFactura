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
    /// <summary>Intentos de numerar un CFE cuando otras emisiones usan el mismo CAE al mismo tiempo.</summary>
    private const int MaxIntentosEmision = 100;

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
    /// Almacenamiento de CAE, numeración y CFE emitidos, obligatorio en Producción (por ejemplo,
    /// <c>EfUruFacturaStore</c>). En Homologación, si se omite, se usa un <see cref="InMemoryStore"/>
    /// (un solo proceso, sin persistencia).
    /// </param>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> para la DGI (p. ej. de <c>IHttpClientFactory</c>). Al indicarlo, su handler
    /// debe tener el certificado de cliente TLS y la validación del servidor configurados.
    /// </param>
    /// <param name="dgiClient">Transporte propio hacia la DGI (en lugar del SOAP predeterminado).</param>
    /// <param name="reloj">Reloj para las fechas fiscales (por defecto, el del sistema).</param>
    /// <exception cref="UruFacturaException">Si la configuración no es válida o falta el almacenamiento en Producción.</exception>
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
        if (store is null && config.Ambiente == Ambiente.Produccion)
            throw new UruFacturaException(
                "En Producción hay que indicar el almacenamiento (por ejemplo, EfUruFacturaStore): un InMemoryStore " +
                "pierde la numeración y los CFE del día al reiniciar la aplicación.");

        _config = config;
        _reloj = reloj ?? TimeProvider.System;
        _store = store ?? new InMemoryStore(_reloj);

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

        // Dos CAE con números en común emitirían números repetidos.
        var caes = await _store.ListarCaesAsync(cancellationToken);
        if (caes.FirstOrDefault(c => !c.EsElMismo(cae) && c.SeSuperponeCon(cae)) is { } otro)
            throw new CaeException(
                $"El rango {cae.RangoDesde}-{cae.RangoHasta} se superpone con el del CAE {otro.NumeroAutorizacion} " +
                $"({otro.TipoCfe}, serie {otro.Serie}, {otro.RangoDesde}-{otro.RangoHasta}).");

        await _store.GuardarCaeAsync(cae, cancellationToken);
    }

    /// <inheritdoc />
    public async Task EliminarCaeAsync(Cae cae, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cae);
        if (!await _store.EliminarCaeAsync(cae, cancellationToken))
            throw new CaeException(
                $"El CAE {cae.NumeroAutorizacion} ({cae.TipoCfe}, serie {cae.Serie}, desde {cae.RangoDesde}) no está " +
                "registrado o ya numeró comprobantes: no se puede eliminar.");
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
    public IReadOnlyList<ErrorValidacion> Validar(Cfe cfe)
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

        var (documento, errores) = Resolver(cfe, hoy);
        if (errores.Count > 0)
            throw new CfeValidationException(errores);

        // El reintento de una emisión que ya se completó no emite otro comprobante.
        if (!string.IsNullOrWhiteSpace(documento.NroInterno)
            && (await _store.BuscarCfesAsync(documento.NroInterno, cancellationToken))
                .FirstOrDefault(c => c.Tipo == documento.Tipo && c.EstadoDgi != EstadoCfe.Rechazado) is { } existente)
            throw new CfeDuplicadoException(existente);

        var totales = documento.CalcularTotales();
        for (var intento = 1; ; intento++)
        {
            var leido = Cae.Elegir(await _store.ListarCaesAsync(cancellationToken), documento.Tipo, hoy)
                ?? throw new CaeException(
                    $"No hay un CAE vigente con números disponibles para {documento.Tipo}. Registre uno con RegistrarCaeAsync.");
            var numero = leido.ProximoNumero;
            var cae = leido with { UltimoNroUsado = numero };

            var xml = CfeXml.Generar(documento, cae, numero, totales, ahora);
            var (firmado, codigoSeguridad) = FirmaDigital.Firmar(xml, _certificado, _config.AlgoritmoFirma);
            var emitido = new CfeEmitido(documento, cae, numero, totales, ahora, codigoSeguridad, firmado);

            // El número se consume solo junto con el registro del CFE: el registro no se cancela a mitad.
            cancellationToken.ThrowIfCancellationRequested();
            if (await _store.RegistrarEmisionAsync(emitido, leido.UltimoNroUsado, CancellationToken.None))
                return emitido;

            // Otra emisión usó el número: se vuelve a numerar.
            if (intento == MaxIntentosEmision)
                throw new UruFacturaException(
                    $"No se pudo numerar el CFE tras {MaxIntentosEmision} intentos (contención con otras emisiones).");
            await Task.Delay(Random.Shared.Next(1, Math.Min(50, 5 * intento) + 1), cancellationToken);
        }
    }

    /// <summary>
    /// El CFE con la fecha de emisión y el emisor efectivos y copias de sus listas (lo que el llamador
    /// cambie después no altera lo emitido), y sus errores de validación.
    /// </summary>
    private (Cfe Documento, List<ErrorValidacion> Errores) Resolver(Cfe cfe, DateOnly hoy)
    {
        var emisor = cfe.Emisor ?? _config.Emisor;
        var documento = cfe with
        {
            FechaEmision = cfe.FechaEmision ?? hoy,
            Emisor = emisor with { Telefonos = Copia(emisor.Telefonos) },
            Detalle = Copia(cfe.Detalle) is { Count: > 0 } detalle
                ? [.. detalle.Select(l => l with { Codigos = Copia(l.Codigos), Retenciones = Copia(l.Retenciones) })]
                : [],
            DescuentosRecargos = Copia(cfe.DescuentosRecargos),
            MediosPago = Copia(cfe.MediosPago),
            Referencias = Copia(cfe.Referencias),
        };

        var errores = documento.Validar(hoy, _config.ValorUnidadIndexada is { } ui ? ui * 5000m : null);

        if (cfe.Emisor is { } propio)
        {
            errores.AddRange(propio.Validar());
            if (propio.Rut != _config.Emisor.Rut)
                errores.Add(new ErrorValidacion("Emisor.Rut", "Debe ser el RUT de la configuración (el del certificado)."));
        }

        return (documento, errores);
    }

    private static IReadOnlyList<T> Copia<T>(IReadOnlyList<T>? lista) => lista is null or { Count: 0 } ? [] : [.. lista];

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
        if (cfes.Select(c => (c.Tipo, c.Serie, c.Numero)).Distinct().Count() != cfes.Count)
            throw new UruFacturaException("El sobre incluye el mismo CFE más de una vez.");
        if (cfes.FirstOrDefault(c => c.Enviado || c.EstadoDgi is not null) is { } enviado)
            throw new InvalidOperationException(
                $"El CFE {enviado} ya {(enviado.EstadoDgi is { } estado ? $"tiene resultado de la DGI ({estado})" : "fue recibido por la DGI")}: " +
                "no se reenvía, porque la DGI lo trataría como duplicado. Su estado se consulta con ConsultarCfeAsync.");

        var comprobantes = cfes.ToList();
        var sobre = CfeSobre.Generar(
            _config.Emisor.Rut,
            await _store.ReservarIdEnvioAsync(cancellationToken),
            _reloj.GetUtcNow(),
            _certificado.RawData,
            [.. comprobantes.Select(c => c.Xml)]);

        var acuse = await _dgi.EnviarSobreAsync(sobre, cancellationToken);

        // El sobre ya llegó a la DGI: el acuse se registra aunque se cancele la operación.
        foreach (var cfe in comprobantes)
            cfe.RegistrarEnvio(acuse);
        await _store.GuardarCfesAsync(comprobantes, CancellationToken.None);

        return acuse with { Comprobantes = comprobantes };
    }

    /// <inheritdoc />
    public async Task<EstadoEnvio> ConsultarEnvioAsync(AcuseSobre acuse, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(acuse);
        if (!acuse.Recibido || string.IsNullOrWhiteSpace(acuse.IdReceptor) || string.IsNullOrWhiteSpace(acuse.Token))
            throw new InvalidOperationException("El sobre no fue recibido por la DGI: no hay resultado que consultar.");

        var estado = await _dgi.ConsultarEnvioAsync(acuse.IdReceptor, acuse.Token, cancellationToken);

        // Cada resultado se asocia por tipo, serie y número o, si la DGI no los informa, por la posición en el sobre.
        var actualizados = new List<CfeEmitido>();
        for (var i = 0; i < acuse.Comprobantes.Count; i++)
        {
            var cfe = acuse.Comprobantes[i];
            var ordinal = i + 1;
            if (estado.Comprobantes.FirstOrDefault(e => e.Corresponde(cfe, ordinal)) is not { } resultado)
                continue;
            cfe.RegistrarEstado(resultado);
            actualizados.Add(cfe);
        }
        if (actualizados.Count > 0)
            await _store.GuardarCfesAsync(actualizados, CancellationToken.None);

        return estado;
    }

    /// <inheritdoc />
    public async Task<EstadoComprobante> ConsultarCfeAsync(CfeEmitido cfe, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfe);

        var estado = await _dgi.ConsultarCfeAsync(cfe.Tipo, cfe.Serie, cfe.Numero, cancellationToken);
        cfe.RegistrarEstado(estado);
        await _store.GuardarCfesAsync([cfe], CancellationToken.None);
        return estado;
    }

    /// <inheritdoc />
    public async Task<AcuseReporte> EnviarReporteDiarioAsync(DateOnly fecha, int secEnvio = 1, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (fecha > HoraUruguay.Hoy(_reloj))
            throw new UruFacturaException("No se puede enviar el reporte diario de una fecha futura.");
        if (secEnvio is < 1 or > 99)
            throw new UruFacturaException("SecEnvio debe estar entre 1 (el primer envío del día) y 99.");

        // Se resume a medida que se lee: no se cargan en memoria los XML de todo el día.
        var resumen = new ReporteDiario.Acumulador(fecha);
        await foreach (var cfe in _store.ListarCfesAsync(fecha, cancellationToken))
            resumen.Agregar(cfe);

        var reporte = resumen.Generar(
            _config.Emisor.Rut, secEnvio, await _store.ReservarIdEnvioAsync(cancellationToken), _reloj.GetUtcNow());
        var (firmado, _) = FirmaDigital.Firmar(reporte, _certificado, _config.AlgoritmoFirma);

        var acuse = await _dgi.EnviarReporteAsync(firmado, cancellationToken);
        return acuse with { Fecha = fecha, CantidadCfe = resumen.Cantidad };
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
