using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using UruFactura.Configuration;
using UruFactura.Exceptions;
using UruFactura.Models;
using UruFactura.Signature;

namespace UruFactura.Soap;

/// <summary>
/// Cliente SOAP 1.1 de los servicios web de la DGI de Uruguay: <c>ws_efactura</c>
/// (<c>EFACRECEPCIONSOBRE</c>, <c>EFACRECEPCIONREPORTE</c>, <c>EFACCONSULTARESTADOENVIO</c>) y
/// <c>ws_consultas</c> (<c>EFACCONSULTARESTADOCFE</c>). Cada mensaje se firma con WS-Security
/// usando el certificado configurado. Operaciones, namespaces y SOAPAction provienen de los
/// WSDL oficiales (<c>ws_eprueba?wsdl</c> y <c>ws_consultasPrueba?wsdl</c>).
/// </summary>
public class DgiSoapClient : IDgiSoapClient
{
    private readonly UruFacturaConfig _config;
    private readonly X509Certificate2? _certificado;
    private HttpClient? _httpClient;
    private bool _ownsHttpClient;
    private bool _disposed;

    private const string ActionEnvio = "http://dgi.gub.uyaction/AWS_EFACTURA.";
    private const string ActionConsultas = "http://dgi.gub.uyaction/AWS_EFACTURA_CONSULTAS.";

    /// <summary>
    /// Inicializa el cliente SOAP con la configuración de UruFactura.
    /// </summary>
    public DgiSoapClient(UruFacturaConfig config, HttpClient? httpClient = null)
    {
        _config = config;

        if (!string.IsNullOrWhiteSpace(config.RutaCertificado))
        {
            if (!File.Exists(config.RutaCertificado))
                throw new UruFacturaException(
                    $"No se encontró el archivo de certificado: '{config.RutaCertificado}'.");

            _certificado = CertificadoLoader.Cargar(config.RutaCertificado, config.PasswordCertificado);
        }

        if (httpClient is not null)
        {
            ValidarHttpClientExterno(httpClient);
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = CrearHttpClient();
            _ownsHttpClient = true;
        }
    }

    public virtual IDgiSoapClient WithHttpClient(HttpClient httpClient)
    {
        ValidarHttpClientExterno(httpClient);
        if (_ownsHttpClient)
            _httpClient?.Dispose();

        _httpClient = httpClient;
        _ownsHttpClient = false;
        return this;
    }

    private static void ValidarHttpClientExterno(HttpClient httpClient)
    {
        if (httpClient.Timeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentException(
                "El HttpClient externo tiene Timeout infinito. " +
                "Configure un timeout finito (p.ej. TimeSpan.FromSeconds(30)).",
                nameof(httpClient));
    }

    /// <inheritdoc />
    public virtual Task<RespuestaDgi> EnviarSobreAsync(string sobreXml, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sobreXml);
        return InvocarAsync(
            _config.DgiEnvioUrl, ActionEnvio + "EFACRECEPCIONSOBRE", "EFACRECEPCIONSOBRE",
            doc => DatainXml(doc, "WS_eFactura.EFACRECEPCIONSOBRE", sobreXml),
            DgiRespuestaParser.ProcesarSobre, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<RespuestaDgi> ConsultarEstadoEnvioAsync(
        string idReceptor, string token, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idReceptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var consulta = new XmlDocument();
        var el = consulta.CreateElement("ConsultaCFE", WsSecurity.DgiNs);
        AddText(consulta, el, "IdReceptor", idReceptor);
        AddText(consulta, el, "Token", token);
        consulta.AppendChild(el);

        return InvocarAsync(
            _config.DgiEnvioUrl, ActionEnvio + "EFACCONSULTARESTADOENVIO", "EFACCONSULTARESTADOENVIO",
            doc => DatainXml(doc, "WS_eFactura.EFACCONSULTARESTADOENVIO", consulta.OuterXml),
            DgiRespuestaParser.ProcesarEstadoEnvio, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<RespuestaDgi> ConsultarEstadoCfeAsync(
        int tipoCfe, string serie, long numero, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serie);
        return InvocarAsync(
            _config.DgiConsultasUrl, ActionConsultas + "EFACCONSULTARESTADOCFE", "EFACCONSULTARESTADOCFE",
            doc =>
            {
                var op = doc.CreateElement("dgi", "WS_eFactura_Consultas.EFACCONSULTARESTADOCFE", WsSecurity.DgiNs);
                var cfeid = doc.CreateElement("dgi", "Cfeid", WsSecurity.DgiNs);
                AddDgi(doc, cfeid, "TipoCFE", tipoCfe.ToString(CultureInfo.InvariantCulture));
                AddDgi(doc, cfeid, "Serie", serie);
                AddDgi(doc, cfeid, "Nro", numero.ToString(CultureInfo.InvariantCulture));
                op.AppendChild(cfeid);
                return op;
            },
            DgiRespuestaParser.ProcesarEstadoCfe, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<RespuestaDgi> EnviarReporteDiarioAsync(
        string reporteFirmadoXml, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reporteFirmadoXml);
        return InvocarAsync(
            _config.DgiEnvioUrl, ActionEnvio + "EFACRECEPCIONREPORTE", "EFACRECEPCIONREPORTE",
            doc => DatainXml(doc, "WS_eFactura.EFACRECEPCIONREPORTE", reporteFirmadoXml),
            DgiRespuestaParser.ProcesarReporte, cancellationToken);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Arma <c>&lt;dgi:Operacion&gt;&lt;dgi:Datain&gt;&lt;dgi:xmlData&gt;&lt;![CDATA[xml]]&gt;</c>.
    /// </summary>
    private static XmlElement DatainXml(XmlDocument doc, string operacion, string xml)
    {
        var op = doc.CreateElement("dgi", operacion, WsSecurity.DgiNs);
        var datain = doc.CreateElement("dgi", "Datain", WsSecurity.DgiNs);
        var xmlData = doc.CreateElement("dgi", "xmlData", WsSecurity.DgiNs);
        xmlData.AppendChild(doc.CreateCDataSection(xml));
        datain.AppendChild(xmlData);
        op.AppendChild(datain);
        return op;
    }

    private static void AddDgi(XmlDocument doc, XmlElement parent, string name, string text)
    {
        var el = doc.CreateElement("dgi", name, WsSecurity.DgiNs);
        el.InnerText = text;
        parent.AppendChild(el);
    }

    private static void AddText(XmlDocument doc, XmlElement parent, string name, string text)
    {
        var el = doc.CreateElement(name, WsSecurity.DgiNs);
        el.InnerText = text;
        parent.AppendChild(el);
    }

    private async Task<RespuestaDgi> InvocarAsync(
        string url,
        string soapAction,
        string operacion,
        Func<XmlDocument, XmlElement> body,
        Func<string, RespuestaDgi> procesar,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var certificado = _certificado ?? throw new UruFacturaException(
            "Se requiere RutaCertificado para firmar los mensajes SOAP (WS-Security) de la DGI.");

        var client = _httpClient ?? throw new InvalidOperationException("No se ha inicializado el HttpClient.");

        try
        {
            var envelope = WsSecurity.CrearYFirmar(body, certificado, _config.AlgoritmoFirma);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            // Un error HTTP sin cuerpo SOAP (p. ej. HTML de un proxy 502/503) no es interpretable.
            if (!response.IsSuccessStatusCode && !responseBody.TrimStart().StartsWith('<'))
                return RespuestaDgi.Error(
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} al invocar '{operacion}'.",
                    responseBody);

            return procesar(responseBody);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new DgiCommunicationException(
                $"Error de red al invocar '{operacion}': {ex.Message}", ex);
        }
        catch (TaskCanceledException ex)
        {
            throw new DgiCommunicationException(
                $"Timeout al invocar '{operacion}'. Verifique el SoapTimeoutSegundos.", ex);
        }
        catch (Exception ex) when (ex is DgiCommunicationException or UruFacturaException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DgiCommunicationException($"Error inesperado al invocar '{operacion}'.", ex);
        }
    }

    private HttpClient CrearHttpClient()
    {
        var handler = new HttpClientHandler();

        // La validación del certificado TLS del servidor solo se deshabilita si el llamador
        // lo pide explícitamente (OmitirValidacionSsl). Nunca se hace de forma automática.
        if (_config.OmitirValidacionSsl)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        if (_certificado is not null)
            handler.ClientCertificates.Add(_certificado);

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.SoapTimeoutSegundos),
        };
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        if (_ownsHttpClient)
            _httpClient?.Dispose();
        _certificado?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
