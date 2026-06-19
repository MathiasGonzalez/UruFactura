using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.Pdf;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;

namespace UruFactura;

/// <summary>
/// Builder fluido para construir un <see cref="UruFacturaClient"/> con pleno control sobre
/// cada dependencia. Use <see cref="WithDefaults"/> para obtener un builder ya configurado
/// con las implementaciones predeterminadas y reemplace sólo lo que necesite.
/// </summary>
/// <example>
/// <code>
/// // Caso habitual — todas las implementaciones predeterminadas:
/// var client = UruFacturaClientBuilder.WithDefaults(config).Build();
///
/// // Con un gestor de CAE y cliente SOAP personalizados (p.ej. para tests):
/// var client = UruFacturaClientBuilder.WithDefaults(config)
///     .WithCaeManager(miCaeManager)
///     .WithSoapClient(miSoapMock)
///     .Build();
/// </code>
/// </example>
public sealed partial class UruFacturaClientBuilder
{
    private readonly UruFacturaConfig _config;
    private ICfeXmlBuilder _xmlBuilder;
    private ICfeFirmante _firmante;
    private ICaeManager _caeManager;
    private IDgiSoapClient _soapClient;
    private ICfePdfGenerator? _pdfGenerator;
    private HttpClient? _httpClient;

    private UruFacturaClientBuilder(UruFacturaConfig config)
    {
        config.Validate();
        _config = config;
        _xmlBuilder = new CfeXmlBuilder();
        _firmante   = new CfeFirmante(config.RutaCertificado, config.PasswordCertificado);
        _caeManager = new CaeManager();
        _soapClient = new DgiSoapClient(config);
    }

    /// <summary>
    /// Crea un builder con todas las dependencias en sus implementaciones predeterminadas.
    /// </summary>
    /// <param name="config">Configuración de UruFactura.</param>
    public static UruFacturaClientBuilder WithDefaults(UruFacturaConfig config) => new(config);

    /// <summary>Reemplaza el generador de XML predeterminado.</summary>
    public UruFacturaClientBuilder WithXmlBuilder(ICfeXmlBuilder xmlBuilder)
    {
        ArgumentNullException.ThrowIfNull(xmlBuilder);
        _xmlBuilder = xmlBuilder;
        return this;
    }

    /// <summary>Reemplaza el firmante digital predeterminado.</summary>
    public UruFacturaClientBuilder WithSigner(ICfeFirmante firmante)
    {
        ArgumentNullException.ThrowIfNull(firmante);
        _firmante = firmante;
        return this;
    }

    /// <summary>Reemplaza el gestor de CAEs predeterminado.</summary>
    public UruFacturaClientBuilder WithCaeManager(ICaeManager caeManager)
    {
        ArgumentNullException.ThrowIfNull(caeManager);
        _caeManager = caeManager;
        return this;
    }

    /// <summary>Reemplaza el cliente SOAP predeterminado.</summary>
    public UruFacturaClientBuilder WithSoapClient(IDgiSoapClient soapClient)
    {
        ArgumentNullException.ThrowIfNull(soapClient);
        _soapClient = soapClient;
        return this;
    }

    /// <summary>
    /// Usa el <see cref="HttpClient"/> provisto en lugar de crear uno interno.
    /// <para>
    /// <b>Recomendado en APIs de alto tráfico:</b> registre el SDK como singleton e inyecte
    /// un <see cref="HttpClient"/> administrado por <c>IHttpClientFactory</c> para evitar el
    /// agotamiento de sockets (socket exhaustion) que ocurre cuando se crean y descartan
    /// instancias de <see cref="HttpClient"/> por cada request.
    /// </para>
    /// <para>
    /// <b>Importante:</b> al inyectar un <see cref="HttpClient"/> propio, el SDK <b>no</b>
    /// configura el <see cref="System.Net.Http.HttpClientHandler"/> interno (certificado de
    /// cliente desde <c>RutaCertificado</c> / <c>PasswordCertificado</c> ni la opción
    /// <c>OmitirValidacionSsl</c>). Es responsabilidad del caller asegurarse de que el
    /// <see cref="HttpClient"/> tenga el certificado de cliente y la validación TLS
    /// correctamente configurados antes de pasarlo a este método.
    /// </para>
    /// <example>
    /// <code>
    /// // Ejemplo en Startup / Program.cs (ASP.NET Core)
    /// builder.Services.AddHttpClient("DGI");
    /// builder.Services.AddSingleton(sp =>
    ///     UruFacturaClientBuilder
    ///         .WithDefaults(config)
    ///         .WithHttpClient(sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("DGI"))
    ///         .Build());
    /// </code>
    /// </example>
    /// </summary>
    public UruFacturaClientBuilder WithHttpClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        return this;
    }

    /// <summary>Establece el generador de PDF a utilizar.</summary>
    public UruFacturaClientBuilder WithPdfGenerator(ICfePdfGenerator? pdfGenerator)
    {
        _pdfGenerator = pdfGenerator;
        return this;
    }

    /// <summary>
    /// Construye el <see cref="UruFacturaClient"/> con las dependencias configuradas.
    /// </summary>
    public UruFacturaClient Build()
    {
        if (_httpClient is not null)
            _soapClient = _soapClient.WithHttpClient(_httpClient);

        return new(_config, _xmlBuilder, _firmante, _caeManager, _soapClient, _pdfGenerator);
    }
}
