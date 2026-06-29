using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.Enums;
using UruFactura.Models;
using UruFactura.Pdf;
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
    private bool _disposed;

    /// <summary>Gestión de CAEs del cliente.</summary>
    public ICaeManager Cae => _caeManager;

    /// <summary>
    /// Constructor principal con todas las dependencias inyectadas.
    /// Para el caso de uso estándar prefiera <see cref="UruFacturaClientBuilder.WithDefaults"/>
    /// o los constructores de conveniencia del paquete correspondiente.
    /// </summary>
    public UruFacturaClient(
        UruFacturaConfig config,
        ICfeXmlBuilder xmlBuilder,
        ICfeFirmante firmante,
        ICaeManager caeManager,
        IDgiSoapClient soapClient,
        ICfePdfGenerator? pdfGenerator)
    {
        _config = config;
        _xmlBuilder = xmlBuilder;
        _firmante = firmante;
        _caeManager = caeManager;
        _soapClient = soapClient;
        _pdfGenerator = pdfGenerator;
    }

    // -----------------------------------------------------------------------
    // Generación de CFE
    // -----------------------------------------------------------------------

    /// <summary>Crea un nuevo e-Ticket pre-configurado con los datos del emisor.</summary>
    public Cfe CrearETicket() => CrearCfe(TipoCfe.ETicket);

    /// <summary>Crea una nueva e-Factura pre-configurada con los datos del emisor.</summary>
    public Cfe CrearEFactura() => CrearCfe(TipoCfe.EFactura);

    /// <summary>Crea un nuevo e-Remito pre-configurado con los datos del emisor.</summary>
    public Cfe CrearERemito() => CrearCfe(TipoCfe.ERemito);

    /// <summary>Crea una nota de crédito de e-Ticket.</summary>
    public Cfe CrearNotaCreditoETicket() => CrearCfe(TipoCfe.NotaCreditoETicket);

    /// <summary>Crea una nota de débito de e-Ticket.</summary>
    public Cfe CrearNotaDebitoETicket() => CrearCfe(TipoCfe.NotaDebitoETicket);

    /// <summary>Crea una nota de crédito de e-Factura.</summary>
    public Cfe CrearNotaCreditoEFactura() => CrearCfe(TipoCfe.NotaCreditoEFactura);

    /// <summary>Crea una nota de débito de e-Factura.</summary>
    public Cfe CrearNotaDebitoEFactura() => CrearCfe(TipoCfe.NotaDebitoEFactura);

    /// <summary>Crea una e-Factura de exportación.</summary>
    public Cfe CrearEFacturaExportacion() => CrearCfe(TipoCfe.EFacturaExportacion);

    /// <summary>Crea una nota de crédito de e-Factura de exportación.</summary>
    public Cfe CrearNotaCreditoEFacturaExportacion() => CrearCfe(TipoCfe.NotaCreditoEFacturaExportacion);

    /// <summary>Crea una nota de débito de e-Factura de exportación.</summary>
    public Cfe CrearNotaDebitoEFacturaExportacion() => CrearCfe(TipoCfe.NotaDebitoEFacturaExportacion);

    /// <summary>Crea un e-Remito de despachante (131).</summary>
    public Cfe CrearERemitoDespachante() => CrearCfe(TipoCfe.ERemitoDespachante);

    /// <summary>Crea un e-Resguardo (151).</summary>
    public Cfe CrearEResguardo() => CrearCfe(TipoCfe.EResguardo);

    /// <summary>Crea una nota de crédito de e-Remito (182).</summary>
    public Cfe CrearNotaCreditoERemito() => CrearCfe(TipoCfe.NotaCreditoERemito);

    private Cfe CrearCfe(TipoCfe tipo) => new()
    {
        Tipo = tipo,
        RutEmisor = _config.RutEmisor,
        RazonSocialEmisor = _config.RazonSocialEmisor,
        NombreComercialEmisor = _config.NombreComercialEmisor,
        Giro = _config.Giro,
        DomicilioFiscalEmisor = _config.DomicilioFiscal,
        CiudadEmisor = _config.Ciudad,
        DepartamentoEmisor = _config.Departamento,
        FechaEmision = DateOnly.FromDateTime(DateTime.Today),
    };

    // -----------------------------------------------------------------------
    // XML y Firma
    // -----------------------------------------------------------------------

    /// <summary>
    /// Genera el XML sin firmar del CFE.
    /// </summary>
    /// <param name="cfe">El CFE a serializar.</param>
    /// <returns>XML sin firmar.</returns>
    public string GenerarXml(Cfe cfe)
    {
        ThrowIfDisposed();
        cfe.CalcularTotales();
        var xml = _xmlBuilder.Generar(cfe);
        cfe.XmlSinFirmar = xml;
        return xml;
    }

    /// <summary>
    /// Firma digitalmente el CFE con XAdES-BES.
    /// </summary>
    /// <param name="cfe">El CFE (debe haberse generado el XML previamente).</param>
    /// <returns>XML firmado.</returns>
    public string FirmarCfe(Cfe cfe)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(cfe.XmlSinFirmar))
            GenerarXml(cfe);

        var xmlFirmado = _firmante.Firmar(cfe.XmlSinFirmar!);
        cfe.XmlFirmado = xmlFirmado;
        return xmlFirmado;
    }

    /// <summary>
    /// Genera y firma el CFE en un solo paso.
    /// </summary>
    /// <param name="cfe">El CFE a procesar.</param>
    /// <returns>XML firmado.</returns>
    public string GenerarYFirmar(Cfe cfe)
    {
        ThrowIfDisposed();
        GenerarXml(cfe);
        return FirmarCfe(cfe);
    }

    // -----------------------------------------------------------------------
    // Comunicación con DGI
    // -----------------------------------------------------------------------

    /// <summary>
    /// Envía el CFE firmado a la DGI.
    /// Si el CFE aún no está firmado, lo firma primero.
    /// </summary>
    /// <param name="cfe">El CFE a enviar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Respuesta de la DGI.</returns>
    public async Task<RespuestaDgi> EnviarCfeAsync(
        Cfe cfe,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(cfe.XmlFirmado))
            GenerarYFirmar(cfe);

        var respuesta = await _soapClient.EnviarCfeAsync(cfe.XmlFirmado!, cancellationToken);

        cfe.CodigoRespuestaDgi = respuesta.Codigo;
        cfe.MensajeRespuestaDgi = respuesta.Mensaje;
        cfe.AceptadoPorDgi = respuesta.Exitoso;

        return respuesta;
    }

    /// <summary>
    /// Consulta el estado de un CFE en los servidores de la DGI.
    /// </summary>
    public async Task<RespuestaDgi> ConsultarEstadoCfeAsync(
        Cfe cfe,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return await _soapClient.ConsultarEstadoCfeAsync(
            cfe.RutEmisor, (int)cfe.Tipo,
            cfe.Serie ?? string.Empty, cfe.Numero,
            cancellationToken);
    }

    /// <summary>
    /// Envía el Reporte Diario a la DGI con los CFE del día.
    /// </summary>
    public async Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(
        DateTime fecha,
        IEnumerable<Cfe> cfes,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var xmlsFirmados = cfes
            .Select(c => string.IsNullOrWhiteSpace(c.XmlFirmado)
                ? GenerarYFirmar(c)
                : c.XmlFirmado!)
            .ToList();

        return await _soapClient.EnviarReporteDiarioAsync(fecha, xmlsFirmados, cancellationToken);
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
    public byte[] GenerarPdfA4(Cfe cfe)
    {
        ThrowIfDisposed();
        ThrowIfNoPdfGenerator();
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
    public byte[] GenerarPdfTermico(Cfe cfe)
    {
        ThrowIfDisposed();
        ThrowIfNoPdfGenerator();
        return _pdfGenerator!.GenerarTermico(cfe);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private void ThrowIfNoPdfGenerator()
    {
        if (_pdfGenerator is null)
            throw new InvalidOperationException(
                "Este cliente no tiene un generador de PDF configurado. " +
                "Use UruFacturaClientBuilder.WithDefaults(config).WithDefaultPdf().Build() (paquete completo) " +
                "o .WithPdfGenerator(miGenerador).Build() para inyectar una implementación personalizada.");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _soapClient.Dispose();
        _firmante.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
