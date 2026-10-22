using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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
    private HttpClient _httpClient;
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
            ValidarHttpClientExterno(httpClient);
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? CrearHttpClient();
    }

    public virtual IDgiSoapClient WithHttpClient(HttpClient httpClient)
    {
        ValidarHttpClientExterno(httpClient);
        if (_ownsHttpClient)
            _httpClient.Dispose();

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
        return InvocarEfacturaAsync("EFACRECEPCIONSOBRE", sobreXml, DgiRespuestaParser.ProcesarSobre, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<RespuestaDgi> ConsultarEstadoEnvioAsync(
        string idReceptor, string token, CancellationToken cancellationToken = default)
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
                var cfeid = Agregar(op, "Cfeid");
                Agregar(cfeid, "TipoCFE", tipoCfe.ToString(CultureInfo.InvariantCulture));
                Agregar(cfeid, "Serie", serie);
                Agregar(cfeid, "Nro", numero.ToString(CultureInfo.InvariantCulture));
                return op;
            },
            DgiRespuestaParser.ProcesarEstadoCfe, cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<RespuestaDgi> EnviarReporteDiarioAsync(
        string reporteFirmadoXml, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reporteFirmadoXml);
        return InvocarEfacturaAsync("EFACRECEPCIONREPORTE", reporteFirmadoXml, DgiRespuestaParser.ProcesarReporte, cancellationToken);
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Operación de <c>ws_efactura</c>:
    /// <c>&lt;dgi:WS_eFactura.Operacion&gt;&lt;dgi:Datain&gt;&lt;dgi:xmlData&gt;&lt;![CDATA[xml]]&gt;</c>.
    /// </summary>
    private Task<RespuestaDgi> InvocarEfacturaAsync(
        string operacion, string xmlData, Func<string, RespuestaDgi> procesar, CancellationToken cancellationToken) =>
        InvocarAsync(
            _config.DgiEnvioUrl, ActionEnvio + operacion, operacion,
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

        try
        {
            var envelope = WsSecurity.CrearYFirmar(body, certificado, _config.AlgoritmoFirma);

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
            };
            request.Headers.TryAddWithoutValidation("SOAPAction", $"\"{soapAction}\"");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
                return procesar(responseBody);

            // Un error HTTP puede traer un SOAP Fault (interpretable) o un cuerpo ajeno, como el
            // HTML de un proxy 502/503: en ese caso se informa el error HTTP.
            try
            {
                return procesar(responseBody);
            }
            catch (DgiCommunicationException)
            {
                return RespuestaDgi.Error(
                    ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture),
                    $"HTTP {(int)response.StatusCode} {response.ReasonPhrase} al invocar '{operacion}'.",
                    responseBody);
            }
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
        var handler = new SocketsHttpHandler
        {
            // El cliente suele vivir toda la aplicación (singleton): renovar las conexiones
            // periódicamente hace que se respeten los cambios de DNS de los servicios de la DGI.
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
        };

        // La validación del certificado TLS del servidor solo se deshabilita si el llamador
        // lo pide explícitamente (OmitirValidacionSsl). Nunca se hace de forma automática.
        if (_config.OmitirValidacionSsl)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;

        if (_certificado is not null)
            handler.SslOptions.ClientCertificates = [_certificado];

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.SoapTimeoutSegundos),
        };
    }

    public virtual void Dispose()
    {
        if (_disposed) return;
        if (_ownsHttpClient)
            _httpClient.Dispose();
        _certificado?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
