using System.Globalization;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace UruFactura.Soap;

/// <summary>
/// Cliente SOAP 1.1 de los servicios web de la DGI: <c>ws_efactura</c> (<c>EFACRECEPCIONSOBRE</c>,
/// <c>EFACRECEPCIONREPORTE</c>, <c>EFACCONSULTARESTADOENVIO</c>) y <c>ws_consultas</c>
/// (<c>EFACCONSULTARESTADOCFE</c>). Cada mensaje se firma con WS-Security. Operaciones, namespaces y
/// SOAPAction provienen de los WSDL oficiales (<c>ws_eprueba?wsdl</c> y <c>ws_consultasPrueba?wsdl</c>).
/// </summary>
internal sealed class DgiSoapClient : IDgiClient, IDisposable
{
    private const string ActionEnvio = "http://dgi.gub.uyaction/AWS_EFACTURA.";
    private const string ActionConsultas = "http://dgi.gub.uyaction/AWS_EFACTURA_CONSULTAS.";

    private readonly UruFacturaConfig _config;
    private readonly X509Certificate2 _certificado;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    /// <param name="config">Configuración (URLs, algoritmo de firma, timeout y validación TLS).</param>
    /// <param name="certificado">Certificado para WS-Security y, con el <see cref="HttpClient"/> propio, para TLS.</param>
    /// <param name="httpClient">
    /// <see cref="HttpClient"/> externo (por ejemplo, de <c>IHttpClientFactory</c>). Si se indica, no se le
    /// configura el certificado de cliente TLS ni <see cref="UruFacturaConfig.OmitirValidacionSsl"/>.
    /// </param>
    internal DgiSoapClient(UruFacturaConfig config, X509Certificate2 certificado, HttpClient? httpClient = null)
    {
        if (httpClient is { Timeout: var t } && t == Timeout.InfiniteTimeSpan)
            throw new ArgumentException(
                "El HttpClient externo tiene Timeout infinito. Configure un timeout finito (p. ej. 30 segundos).",
                nameof(httpClient));

        _config = config;
        _certificado = certificado;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CrearHttpClient();
    }

    public Task<AcuseSobre> EnviarSobreAsync(string sobreXml, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sobreXml);
        return InvocarEfacturaAsync("EFACRECEPCIONSOBRE", sobreXml, DgiRespuestaParser.ProcesarSobre, cancellationToken);
    }

    public Task<EstadoEnvio> ConsultarEnvioAsync(string idReceptor, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idReceptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        XNamespace dgi = WsSecurity.DgiNs;
        var consulta = new XElement(dgi + "ConsultaCFE",
            new XElement(dgi + "IdReceptor", idReceptor),
            new XElement(dgi + "Token", token));

        return InvocarEfacturaAsync(
            "EFACCONSULTARESTADOENVIO", consulta.ToString(SaveOptions.DisableFormatting),
            DgiRespuestaParser.ProcesarEstadoEnvio, cancellationToken);
    }

    public Task<EstadoComprobante> ConsultarCfeAsync(TipoCfe tipo, string serie, long numero, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serie);
        return InvocarAsync(
            _config.UrlConsultasEfectiva, ActionConsultas + "EFACCONSULTARESTADOCFE", "EFACCONSULTARESTADOCFE",
            doc =>
            {
                var op = doc.CreateElement("dgi", "WS_eFactura_Consultas.EFACCONSULTARESTADOCFE", WsSecurity.DgiNs);
                var cfeid = Agregar(op, "Cfeid");
                Agregar(cfeid, "TipoCFE", ((int)tipo).ToString(CultureInfo.InvariantCulture));
                Agregar(cfeid, "Serie", serie);
                Agregar(cfeid, "Nro", numero.ToString(CultureInfo.InvariantCulture));
                return op;
            },
            xml => DgiRespuestaParser.ProcesarEstadoCfe(xml) with { TipoCfe = (int)tipo, Serie = serie, Numero = numero },
            cancellationToken);
    }

    public Task<AcuseReporte> EnviarReporteAsync(string reporteFirmadoXml, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reporteFirmadoXml);
        return InvocarEfacturaAsync("EFACRECEPCIONREPORTE", reporteFirmadoXml, DgiRespuestaParser.ProcesarReporte, cancellationToken);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Operación de <c>ws_efactura</c>:
    /// <c>&lt;dgi:WS_eFactura.Operacion&gt;&lt;dgi:Datain&gt;&lt;dgi:xmlData&gt;&lt;![CDATA[xml]]&gt;</c>.
    /// </summary>
    private Task<T> InvocarEfacturaAsync<T>(
        string operacion, string xmlData, Func<string, T> procesar, CancellationToken cancellationToken) =>
        InvocarAsync(
            _config.UrlEnvioEfectiva, ActionEnvio + operacion, operacion,
            doc =>
            {
                var op = doc.CreateElement("dgi", "WS_eFactura." + operacion, WsSecurity.DgiNs);
                Agregar(Agregar(op, "Datain"), "xmlData").AppendChild(doc.CreateCDataSection(xmlData));
                return op;
            },
            procesar, cancellationToken);

    /// <summary>Agrega <c>&lt;dgi:nombre&gt;</c> (con el texto indicado, si lo hay) a <paramref name="padre"/>.</summary>
    private static XmlElement Agregar(XmlElement padre, string nombre, string? texto = null)
    {
        var el = padre.OwnerDocument.CreateElement("dgi", nombre, WsSecurity.DgiNs);
        if (texto is not null)
            el.InnerText = texto;
        padre.AppendChild(el);
        return el;
    }

    private async Task<T> InvocarAsync<T>(
        string url,
        string soapAction,
        string operacion,
        Func<XmlDocument, XmlElement> body,
        Func<string, T> procesar,
        CancellationToken cancellationToken)
    {
        string responseBody;
        HttpStatusCode status;
        string? reason;
        try
        {
            var envelope = WsSecurity.CrearYFirmar(body, _certificado, _config.AlgoritmoFirma);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            (status, reason) = (response.StatusCode, response.ReasonPhrase);
            responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            throw new DgiCommunicationException($"Timeout al invocar '{operacion}' (Timeout: {_config.Timeout}).", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            // IOException: la conexión se cortó mientras se leía la respuesta.
            throw new DgiCommunicationException($"Error de red al invocar '{operacion}': {ex.Message}", ex);
        }

        if ((int)status is >= 200 and < 300)
            return procesar(responseBody);

        // Un error HTTP puede traer un SOAP Fault (que se informa como tal) o un cuerpo ajeno,
        // como el HTML de un proxy 502/503: en ese caso se informa el error HTTP.
        try
        {
            return procesar(responseBody);
        }
        catch (DgiCommunicationException ex) when (ex.CodigoRespuesta is null)
        {
            var codigo = ((int)status).ToString(CultureInfo.InvariantCulture);
            throw new DgiCommunicationException(
                $"HTTP {codigo} {reason} al invocar '{operacion}'.", codigo, responseBody);
        }
    }

    private HttpClient CrearHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            // El cliente suele vivir toda la aplicación: renovar las conexiones periódicamente hace
            // que se respeten los cambios de DNS de los servicios de la DGI.
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };
        handler.SslOptions.ClientCertificates = [_certificado];

        // Solo si el llamador lo pide explícitamente.
        if (_config.OmitirValidacionSsl)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        return new HttpClient(handler) { Timeout = _config.Timeout };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
