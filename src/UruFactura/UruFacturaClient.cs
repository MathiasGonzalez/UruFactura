using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.Enums;
using UruFactura.Models;
using UruFactura.Pdf;
using UruFactura.Persistence;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;

namespace UruFactura;

/// <summary>
/// Cliente principal de UruFactura.
/// Orquesta la creación de CFE, generación/firma de XML, comunicación con DGI
/// y, opcionalmente, la generación de representaciones impresas (PDF).
/// <para>
/// Use <see cref="UruFacturaClientBuilder.WithDefaults"/> para construir una instancia
/// con las implementaciones predeterminadas, o el constructor principal para inyectar
/// implementaciones personalizadas.
/// </para>
/// </summary>
public partial class UruFacturaClient : IUruFacturaClient
{
    private readonly UruFacturaConfig _config;
    private readonly ICfeXmlBuilder _xmlBuilder;
    private readonly ICfeFirmante _firmante;
    private readonly ICaeManager _caeManager;
    private readonly IDgiSoapClient _soapClient;
    private readonly ICfePdfGenerator? _pdfGenerator;
    private readonly ICaeRepository? _caeRepository;
    private readonly ICfeRepository? _cfeRepository;
    private readonly Lock _idEnvioLock = new();
    private long _ultimoIdEnvio;
    private bool _disposed;

    /// <summary>Gestión de CAEs del cliente.</summary>
    public ICaeManager Cae => _caeManager;

    /// <summary>
    /// Constructor principal con todas las dependencias inyectadas.
    /// Para el caso de uso estándar prefiera <see cref="UruFacturaClientBuilder.WithDefaults"/>
    /// o los constructores de conveniencia del paquete correspondiente.
    /// </summary>
    /// <param name="caeRepository">
    /// Persistencia de los CAE. Si implementa <see cref="ICaeRepositoryCompartido"/>, la numeración
    /// se reserva en el almacenamiento compartido (varias instancias).
    /// </param>
    /// <param name="cfeRepository">Registro durable de los CFE emitidos (para el reporte diario entre instancias).</param>
    public UruFacturaClient(
        UruFacturaConfig config,
        ICfeXmlBuilder xmlBuilder,
        ICfeFirmante firmante,
        ICaeManager caeManager,
        IDgiSoapClient soapClient,
        ICfePdfGenerator? pdfGenerator,
        ICaeRepository? caeRepository = null,
        ICfeRepository? cfeRepository = null)
    {
        _config = config;
        _xmlBuilder = xmlBuilder;
        _firmante = firmante;
        _caeManager = caeManager;
        _soapClient = soapClient;
        _pdfGenerator = pdfGenerator;
        _caeRepository = caeRepository;
        _cfeRepository = cfeRepository;
    }

    /// <summary>
    /// Inicializa el cliente con las implementaciones predeterminadas y el generador de PDF indicado
    /// (<c>null</c> para no generar PDF).
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    /// <param name="pdfGenerator">Implementación de <see cref="ICfePdfGenerator"/> a utilizar.</param>
    public UruFacturaClient(UruFacturaConfig config, ICfePdfGenerator? pdfGenerator)
        : this(config,
               new CfeXmlBuilder(),
               new CfeFirmante(config.RutaCertificado, config.PasswordCertificado, config.AlgoritmoFirma),
               new CaeManager(),
               new DgiSoapClient(config),
               pdfGenerator)
    {
    }

    // -----------------------------------------------------------------------
    // Generación de CFE
    // -----------------------------------------------------------------------

    /// <summary>
    /// Crea un CFE de cualquier tipo soportado (contingencia, cuenta ajena, etc.) pre-configurado con los
    /// datos del emisor.
    /// </summary>
    public virtual Cfe Crear(TipoCfe tipo)
    {
        if (!Enum.IsDefined(tipo))
            throw new ArgumentOutOfRangeException(nameof(tipo), tipo, $"Tipo de CFE no soportado: {tipo}.");
        return CrearCfe(tipo);
    }

    /// <summary>Crea un nuevo e-Ticket pre-configurado con los datos del emisor.</summary>
    public virtual Cfe CrearETicket() => CrearCfe(TipoCfe.ETicket);

    /// <summary>Crea una nueva e-Factura pre-configurada con los datos del emisor.</summary>
    public virtual Cfe CrearEFactura() => CrearCfe(TipoCfe.EFactura);

    /// <summary>Crea un nuevo e-Remito pre-configurado con los datos del emisor.</summary>
    public virtual Cfe CrearERemito() => CrearCfe(TipoCfe.ERemito);

    /// <summary>Crea una nota de crédito de e-Ticket.</summary>
    public virtual Cfe CrearNotaCreditoETicket() => CrearCfe(TipoCfe.NotaCreditoETicket);

    /// <summary>Crea una nota de débito de e-Ticket.</summary>
    public virtual Cfe CrearNotaDebitoETicket() => CrearCfe(TipoCfe.NotaDebitoETicket);

    /// <summary>Crea una nota de crédito de e-Factura.</summary>
    public virtual Cfe CrearNotaCreditoEFactura() => CrearCfe(TipoCfe.NotaCreditoEFactura);

    /// <summary>Crea una nota de débito de e-Factura.</summary>
    public virtual Cfe CrearNotaDebitoEFactura() => CrearCfe(TipoCfe.NotaDebitoEFactura);

    /// <summary>Crea una e-Factura de exportación.</summary>
    public virtual Cfe CrearEFacturaExportacion() => CrearCfe(TipoCfe.EFacturaExportacion);

    /// <summary>Crea una nota de crédito de e-Factura de exportación.</summary>
    public virtual Cfe CrearNotaCreditoEFacturaExportacion() => CrearCfe(TipoCfe.NotaCreditoEFacturaExportacion);

    /// <summary>Crea una nota de débito de e-Factura de exportación.</summary>
    public virtual Cfe CrearNotaDebitoEFacturaExportacion() => CrearCfe(TipoCfe.NotaDebitoEFacturaExportacion);

    /// <summary>Crea un e-Remito de exportación (124).</summary>
    public virtual Cfe CrearERemitoExportacion() => CrearCfe(TipoCfe.ERemitoExportacion);

    /// <summary>
    /// Crea un e-Resguardo (182). El modelo actual no incluye retenciones/percepciones, por lo
    /// que <see cref="Cfe.Validar"/> lo rechaza hasta que se agregue ese soporte.
    /// </summary>
    public virtual Cfe CrearEResguardo() => CrearCfe(TipoCfe.EResguardo);

    /// <summary>Crea el CFE base de un tipo con los datos del emisor; sobreescribible para ajustar los valores por defecto.</summary>
    protected virtual Cfe CrearCfe(TipoCfe tipo) => new()
    {
        TelefonosEmisor = [.. _config.TelefonosEmisor],
        CorreoEmisor = _config.CorreoEmisor,
        SucursalEmisor = _config.SucursalEmisor,
        TopeMontoNetoSinReceptor = _config.ValorUnidadIndexada is { } ui ? ui * 5000m : null,
        Tipo = tipo,
        RutEmisor = _config.RutEmisor,
        RazonSocialEmisor = _config.RazonSocialEmisor,
        NombreComercialEmisor = _config.NombreComercialEmisor,
        Giro = _config.Giro,
        DomicilioFiscalEmisor = _config.DomicilioFiscal,
        CiudadEmisor = _config.Ciudad,
        DepartamentoEmisor = _config.Departamento,
        CodigoSucursal = _config.CodigoSucursal,
        FechaEmision = HoraUruguay.Hoy(),
    };

    // -----------------------------------------------------------------------
    // XML y Firma
    // -----------------------------------------------------------------------

    /// <summary>
    /// Carga en el gestor todos los CAE guardados en el <see cref="ICaeRepository"/> configurado
    /// (con su último número usado). Llamarlo al iniciar la aplicación. Es idempotente.
    /// </summary>
    /// <returns>Cantidad de CAE cargados (0 si no hay repositorio).</returns>
    public virtual async Task<int> CargarCaesAsync()
    {
        ThrowIfDisposed();
        if (_caeRepository is null)
            return 0;

        var caes = (await _caeRepository.CargarTodosAsync()).ToList();
        _caeManager.RegistrarCaes(caes);
        return caes.Count;
    }

    /// <summary>
    /// Registra un CAE nuevo en el gestor y lo guarda en el <see cref="ICaeRepository"/> configurado (si lo hay).
    /// </summary>
    public virtual async Task RegistrarCaeAsync(Models.Cae cae)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cae);
        _caeManager.RegistrarCae(cae);
        if (_caeRepository is not null)
            await _caeRepository.GuardarCaeAsync(cae);
    }

    /// <summary>
    /// Toma el próximo número del CAE vigente para el tipo del CFE y completa
    /// <see cref="Cfe.Numero"/>, <see cref="Cfe.Serie"/> y <see cref="Cfe.CaeData"/>.
    /// No persiste el número usado: si lo necesita, use <see cref="AsignarCaeAsync"/>
    /// con un <see cref="ICaeRepository"/>.
    /// </summary>
    /// <exception cref="Exceptions.CaeException">Si no hay un CAE vigente con números disponibles.</exception>
    /// <exception cref="InvalidOperationException">
    /// Si el repositorio de CAE es compartido (<see cref="ICaeRepositoryCompartido"/>): en ese caso
    /// el número debe reservarse en el almacenamiento con <see cref="AsignarCaeAsync"/>.
    /// </exception>
    public virtual void AsignarCae(Cfe cfe)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfe);
        if (_caeRepository is ICaeRepositoryCompartido)
            throw new InvalidOperationException(
                "Con un repositorio de CAE compartido (varias instancias) use AsignarCaeAsync: " +
                "el número se reserva en el almacenamiento compartido.");
        AsignarCaeInterno(cfe);
    }

    /// <summary>
    /// Como <see cref="AsignarCae"/>, y además guarda el último número usado en el
    /// <see cref="ICaeRepository"/> configurado (si lo hay) para que sobreviva a reinicios.
    /// Si el repositorio es un <see cref="ICaeRepositoryCompartido"/>, el número se reserva
    /// directamente en el almacenamiento compartido, por lo que es seguro con varias instancias.
    /// </summary>
    /// <exception cref="Exceptions.CaeException">Si no hay un CAE vigente con números disponibles.</exception>
    public virtual async Task AsignarCaeAsync(Cfe cfe, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfe);

        if (_caeRepository is ICaeRepositoryCompartido compartido)
        {
            var (reservado, numero) = await compartido.ReservarProximoNumeroAsync(cfe.Tipo, HoraUruguay.Hoy(), cancellationToken)
                ?? throw new Exceptions.CaeException(
                    $"No hay CAE vigente y con números disponibles para el tipo {cfe.Tipo}.");
            AplicarNumero(cfe, reservado, numero);
            // Mantiene el gestor en memoria al día para las advertencias de esta instancia.
            _caeManager.RegistrarCae(reservado);
            return;
        }

        var cae = AsignarCaeInterno(cfe);

        if (_caeRepository is not null)
            await _caeRepository.ActualizarUltimoNroUsadoAsync(cae.NroSerie, cfe.Numero);
    }

    private Models.Cae AsignarCaeInterno(Cfe cfe)
    {
        var (cae, numero) = _caeManager.ObtenerProximoNumero(cfe.Tipo);
        AplicarNumero(cfe, cae, numero);
        return cae;
    }

    private static void AplicarNumero(Cfe cfe, Models.Cae cae, long numero)
    {
        cfe.Numero = numero;
        cfe.Serie = cae.Serie;
        cfe.CaeData = CaeData.Desde(cae);
        // El XML anterior (si existía) ya no corresponde a estos datos.
        cfe.XmlSinFirmar = null;
        cfe.XmlFirmado = null;
    }

    /// <summary>
    /// Genera el XML sin firmar del CFE.
    /// </summary>
    /// <param name="cfe">El CFE a serializar.</param>
    /// <returns>XML sin firmar.</returns>
    public virtual string GenerarXml(Cfe cfe)
    {
        ThrowIfDisposed();
        cfe.CalcularTotales();
        var xml = _xmlBuilder.Generar(cfe);
        cfe.XmlSinFirmar = xml;
        return xml;
    }

    /// <summary>
    /// Firma digitalmente el CFE (XML Signature enveloped).
    /// </summary>
    /// <param name="cfe">El CFE (debe haberse generado el XML previamente).</param>
    /// <returns>XML firmado.</returns>
    public virtual string FirmarCfe(Cfe cfe)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(cfe.XmlSinFirmar))
            GenerarXml(cfe);

        var xmlFirmado = _firmante.Firmar(cfe.XmlSinFirmar!);
        cfe.XmlFirmado = xmlFirmado;
        RegistrarDatosDeFirma(cfe, xmlFirmado);
        return xmlFirmado;
    }

    /// <summary>
    /// Genera y firma el CFE en un solo paso.
    /// </summary>
    /// <param name="cfe">El CFE a procesar.</param>
    /// <returns>XML firmado.</returns>
    public virtual string GenerarYFirmar(Cfe cfe)
    {
        ThrowIfDisposed();
        GenerarXml(cfe);
        return FirmarCfe(cfe);
    }

    // -----------------------------------------------------------------------
    // Comunicación con DGI
    // -----------------------------------------------------------------------

    /// <summary>
    /// Envía el CFE a la DGI dentro de un sobre (<c>EFACRECEPCIONSOBRE</c>).
    /// Si el CFE aún no está firmado, lo genera y firma primero. Un envío exitoso
    /// (<see cref="RespuestaDgi.Exitoso"/>) significa que la DGI <b>recibió el sobre</b>; el
    /// resultado del comprobante se obtiene después con <see cref="ConsultarEstadoEnvioAsync"/>.
    /// </summary>
    /// <param name="cfe">El CFE a enviar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Respuesta de la DGI (<c>ACKSobre</c>).</returns>
    public virtual Task<RespuestaDgi> EnviarCfeAsync(
        Cfe cfe,
        CancellationToken cancellationToken = default) =>
        EnviarCfesAsync([cfe], cancellationToken);

    /// <summary>
    /// Envía varios CFE (1 a 250) en un único sobre. Con un <see cref="ICfeRepository"/> configurado,
    /// los CFE se guardan como emitidos antes de enviarlos (así entran en el reporte diario aunque el
    /// envío falle) y otra vez con la respuesta de la DGI.
    /// </summary>
    public virtual async Task<RespuestaDgi> EnviarCfesAsync(
        IEnumerable<Cfe> cfes,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfes);
        var lista = cfes.ToList();

        // Antes de firmar y registrar: un sobre inválido no debe dejar CFE guardados como emitidos.
        if (lista.Count is < 1 or > CfeSobreBuilder.MaxCfePorSobre)
            throw new Exceptions.UruFacturaException(
                $"Un sobre debe contener entre 1 y {CfeSobreBuilder.MaxCfePorSobre} CFE (recibidos: {lista.Count}).");

        foreach (var cfe in lista)
            AsegurarFirmado(cfe);

        if (_cfeRepository is not null)
            await _cfeRepository.GuardarAsync(lista, cancellationToken);

        var sobre = CfeSobreBuilder.Generar(
            _config.RutEmisor,
            await SiguienteIdEnvioAsync(cancellationToken),
            DateTimeOffset.UtcNow,
            _firmante.ObtenerCertificadoDer(),
            lista.Select(c => c.XmlFirmado!).ToList());

        var respuesta = await _soapClient.EnviarSobreAsync(sobre, cancellationToken);

        foreach (var cfe in lista)
        {
            cfe.CodigoRespuestaDgi = respuesta.Codigo;
            cfe.MensajeRespuestaDgi = respuesta.Mensaje;
            cfe.IdReceptorDgi = respuesta.IdReceptor;
            cfe.TokenDgi = respuesta.Token;
            // AceptadoPorDgi solo se actualiza con la consulta de estado.
        }

        if (_cfeRepository is not null)
            await _cfeRepository.GuardarAsync(lista, cancellationToken);

        return respuesta;
    }

    /// <summary>
    /// Guarda los CFE en el <see cref="ICfeRepository"/> configurado. <see cref="EnviarCfesAsync"/> ya
    /// lo hace; úselo para los comprobantes que se emiten sin enviarse en el momento, para que entren
    /// en el reporte diario. Los CFE sin firmar se generan y firman primero.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si no hay un <see cref="ICfeRepository"/> configurado.</exception>
    public virtual async Task RegistrarCfesAsync(
        IEnumerable<Cfe> cfes,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfes);
        var repositorio = RequerirCfeRepository();

        var lista = cfes.ToList();
        foreach (var cfe in lista)
            AsegurarFirmado(cfe);

        await repositorio.GuardarAsync(lista, cancellationToken);
    }

    /// <summary>
    /// Consulta el resultado de un sobre ya enviado (<c>EFACCONSULTARESTADOENVIO</c>) y actualiza
    /// <see cref="Cfe.AceptadoPorDgi"/> de los CFE indicados según el estado informado.
    /// </summary>
    /// <param name="envio">Respuesta devuelta por <see cref="EnviarCfeAsync"/> / <see cref="EnviarCfesAsync"/>.</param>
    /// <param name="cfes">CFE del sobre cuyo estado se actualiza (opcional).</param>
    public virtual async Task<RespuestaDgi> ConsultarEstadoEnvioAsync(
        RespuestaDgi envio,
        IEnumerable<Cfe>? cfes = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(envio);

        if (string.IsNullOrWhiteSpace(envio.IdReceptor) || string.IsNullOrWhiteSpace(envio.Token))
            throw new InvalidOperationException(
                "La respuesta del envío no tiene IdReceptor/Token: el sobre no fue recibido por la DGI.");

        var respuesta = await _soapClient.ConsultarEstadoEnvioAsync(
            envio.IdReceptor, envio.Token, cancellationToken);

        if (cfes is not null)
        {
            var actualizados = new List<Cfe>();
            foreach (var cfe in cfes)
            {
                var estado = respuesta.Comprobantes.FirstOrDefault(c =>
                    c.TipoCfe == (int)cfe.Tipo && c.Serie == cfe.Serie && c.Numero == cfe.Numero);
                if (estado is null)
                    continue;

                cfe.CodigoRespuestaDgi = estado.Estado;
                cfe.MensajeRespuestaDgi = string.Join(" ", estado.Motivos);
                cfe.AceptadoPorDgi = estado.Estado == "AE";
                actualizados.Add(cfe);
            }

            if (_cfeRepository is not null && actualizados.Count > 0)
                await _cfeRepository.GuardarAsync(actualizados, cancellationToken);
        }

        return respuesta;
    }

    /// <summary>
    /// Consulta el estado de un CFE por tipo, serie y número (<c>EFACCONSULTARESTADOCFE</c>).
    /// </summary>
    public virtual async Task<RespuestaDgi> ConsultarEstadoCfeAsync(
        Cfe cfe,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cfe);
        if (string.IsNullOrWhiteSpace(cfe.Serie) || cfe.Numero <= 0)
            throw new InvalidOperationException("El CFE no tiene serie y número asignados.");

        var respuesta = await _soapClient.ConsultarEstadoCfeAsync(
            (int)cfe.Tipo, cfe.Serie, cfe.Numero, cancellationToken);

        cfe.CodigoRespuestaDgi = respuesta.Codigo;
        cfe.MensajeRespuestaDgi = respuesta.Mensaje;
        cfe.AceptadoPorDgi = respuesta.Exitoso;

        if (_cfeRepository is not null)
            await _cfeRepository.GuardarAsync([cfe], cancellationToken);

        return respuesta;
    }

    /// <summary>
    /// Envía el Reporte Diario (<c>EFACRECEPCIONREPORTE</c>): un resumen firmado con montos,
    /// cantidades y rangos de los CFE emitidos en <paramref name="fecha"/>. Los CFE con otra
    /// fecha de emisión se ignoran.
    /// </summary>
    public virtual Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(
        DateTime fecha,
        IEnumerable<Cfe> cfes,
        CancellationToken cancellationToken = default) =>
        EnviarReporteDiarioAsync(fecha, cfes, secEnvio: 0, cancellationToken);

    /// <summary>
    /// Igual que <see cref="EnviarReporteDiarioAsync(DateTime, IEnumerable{Cfe}, CancellationToken)"/>
    /// indicando la secuencia del envío del día (0 el primero, 1 el siguiente, ...).
    /// </summary>
    public virtual async Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(
        DateTime fecha,
        IEnumerable<Cfe> cfes,
        int secEnvio,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var dia = DateOnly.FromDateTime(fecha);
        var delDia = cfes.Where(c => c.FechaEmision == dia).ToList();

        // Los CFE reconstruidos desde la base sin detalle conservan los totales guardados;
        // recalcularlos los dejaría en cero.
        foreach (var cfe in delDia.Where(c => c.Detalle.Count > 0))
            cfe.CalcularTotales();

        var reporte = ReporteDiarioBuilder.Generar(
            _config.RutEmisor, dia, secEnvio, await SiguienteIdEnvioAsync(cancellationToken), DateTimeOffset.UtcNow, delDia);
        var firmado = _firmante.Firmar(reporte);

        var respuesta = await _soapClient.EnviarReporteDiarioAsync(firmado, cancellationToken);

        return new RespuestaReporteDiario
        {
            FechaReporte = fecha,
            CantidadCfe = delDia.Count,
            Respuesta = respuesta,
        };
    }

    /// <summary>
    /// Envía el Reporte Diario de <paramref name="fecha"/> con los CFE guardados en el
    /// <see cref="ICfeRepository"/> configurado, es decir, los emitidos por todas las instancias.
    /// </summary>
    /// <param name="fecha">Fecha de emisión a resumir (en general, <c>HoraUruguay.Hoy().AddDays(-1)</c>).</param>
    /// <param name="secEnvio">Secuencia del envío del día (0 el primero, 1 el siguiente, ...).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <exception cref="InvalidOperationException">Si no hay un <see cref="ICfeRepository"/> configurado.</exception>
    public virtual async Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(
        DateOnly fecha,
        int secEnvio = 0,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var cfes = await RequerirCfeRepository().ObtenerPorFechaEmisionAsync(fecha, cancellationToken);
        return await EnviarReporteDiarioAsync(fecha.ToDateTime(TimeOnly.MinValue), cfes, secEnvio, cancellationToken);
    }

    /// <summary>
    /// Número de envío asignado por el emisor (<c>Idemisor</c>, hasta 10 dígitos): estrictamente
    /// creciente dentro del proceso y basado en la hora para no repetirse entre reinicios.
    /// Con varias instancias se usa <see cref="SiguienteIdEnvioAsync"/>, que lo reserva en el
    /// almacenamiento compartido.
    /// </summary>
    protected virtual long SiguienteIdEnvio()
    {
        lock (_idEnvioLock)
        {
            _ultimoIdEnvio = Math.Max(_ultimoIdEnvio + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return _ultimoIdEnvio;
        }
    }

    /// <summary>
    /// Número de envío del emisor: lo reserva el <see cref="ICaeRepositoryCompartido"/> si está
    /// configurado (único entre instancias) y, si no, <see cref="SiguienteIdEnvio"/>.
    /// </summary>
    protected virtual async ValueTask<long> SiguienteIdEnvioAsync(CancellationToken cancellationToken)
    {
        if (_caeRepository is ICaeRepositoryCompartido compartido)
            return await compartido.ReservarIdEnvioAsync(cancellationToken);
        return SiguienteIdEnvio();
    }

    // -----------------------------------------------------------------------
    // PDF
    // -----------------------------------------------------------------------

    /// <summary>
    /// Genera el PDF A4 del CFE.
    /// </summary>
    /// <param name="cfe">El CFE.</param>
    /// <returns>Bytes del PDF.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si el cliente fue construido sin un generador de PDF.
    /// Use <see cref="UruFacturaClientBuilder.WithPdfGenerator"/> o el constructor con <see cref="ICfePdfGenerator"/>.
    /// </exception>
    public virtual byte[] GenerarPdfA4(Cfe cfe)
    {
        ThrowIfDisposed();
        ThrowIfNoPdfGenerator();
        AsegurarFirmado(cfe);
        return _pdfGenerator!.GenerarA4(cfe);
    }

    /// <summary>
    /// Genera el PDF térmico (ticket 80mm) del CFE.
    /// </summary>
    /// <param name="cfe">El CFE.</param>
    /// <returns>Bytes del PDF.</returns>
    /// <exception cref="InvalidOperationException">
    /// Si el cliente fue construido sin un generador de PDF.
    /// Use <see cref="UruFacturaClientBuilder.WithPdfGenerator"/> o el constructor con <see cref="ICfePdfGenerator"/>.
    /// </exception>
    public virtual byte[] GenerarPdfTermico(Cfe cfe)
    {
        ThrowIfDisposed();
        ThrowIfNoPdfGenerator();
        AsegurarFirmado(cfe);
        return _pdfGenerator!.GenerarTermico(cfe);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Extrae del XML firmado la fecha de firma (<c>TmstFirma</c>) y el código de seguridad
    /// (<c>DigestValue</c> de la firma), que se imprimen en el QR de la representación impresa.
    /// </summary>
    private static void RegistrarDatosDeFirma(Cfe cfe, string xmlFirmado)
    {
        try
        {
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xmlFirmado);

            var tmst = doc.SelectSingleNode("//*[local-name()='TmstFirma']")?.InnerText;
            if (DateTimeOffset.TryParse(tmst, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var fecha))
                cfe.FechaFirma = fecha;

            cfe.CodigoSeguridad = doc.SelectSingleNode(
                "//*[local-name()='SignedInfo']/*[local-name()='Reference']/*[local-name()='DigestValue']")
                ?.InnerText.Trim();
        }
        catch (System.Xml.XmlException)
        {
            // Un firmante personalizado puede devolver XML que no sigue el formato estándar.
        }
    }

    /// <summary>Genera y firma el CFE si todavía no lo está (para enviarlo, registrarlo o imprimirlo).</summary>
    private void AsegurarFirmado(Cfe cfe)
    {
        if (string.IsNullOrWhiteSpace(cfe.XmlFirmado))
            GenerarYFirmar(cfe);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private ICfeRepository RequerirCfeRepository() =>
        _cfeRepository ?? throw new InvalidOperationException(
            "Este cliente no tiene un ICfeRepository configurado. " +
            "Use UruFacturaClientBuilder.WithCfeRepository(miRepositorio).");

    private void ThrowIfNoPdfGenerator()
    {
        if (_pdfGenerator is null)
            throw new InvalidOperationException(
                "Este cliente no tiene un generador de PDF configurado. " +
                "Use UruFacturaClientBuilder.WithDefaults(config).WithDefaultPdf().Build() (paquete completo) " +
                "o .WithPdfGenerator(miGenerador).Build() para inyectar una implementación personalizada.");
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_disposed) return;
        _soapClient.Dispose();
        _firmante.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
