using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using UruFactura.Configuration;
using UruFactura.Exceptions;
using UruFactura.Formatting;
using UruFactura.Models;

namespace UruFactura.Soap;

/// <summary>
/// Cliente SOAP para comunicarse con los servicios web de la DGI de Uruguay
/// (envío de CFE, consulta de estado y envío de reporte diario).
/// </summary>
public class DgiSoapClient : IDgiSoapClient
{
    private readonly UruFacturaConfig _config;
    private HttpClient? _httpClient;
    private bool _ownsHttpClient;
    private bool _disposed;

    private const string SoapAction = "\"\"";
    private const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>
    /// Inicializa el cliente SOAP con la configuración de UruFactura.
    /// </summary>
    public DgiSoapClient(UruFacturaConfig config, HttpClient? httpClient = null)
    {
        _config = config;
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

    public IDgiSoapClient WithHttpClient(HttpClient httpClient)
    {
        ValidarHttpClientExterno(httpClient);
        if (_ownsHttpClient)
            _httpClient?.Dispose();

        _httpClient = httpClient;
        _ownsHttpClient = false;
        return this;
    }

    /// <summary>
    /// Valida que el <see cref="HttpClient"/> externo cumpla los requisitos mínimos para
    /// comunicarse correctamente con la DGI.
    /// </summary>
    private static void ValidarHttpClientExterno(HttpClient httpClient)
    {
        if (httpClient.Timeout == Timeout.InfiniteTimeSpan)
            throw new ArgumentException(
                "El HttpClient externo tiene Timeout infinito. " +
                "Configure un timeout finito (p.ej. TimeSpan.FromSeconds(30)).",
                nameof(httpClient));
    }

    /// <summary>
    /// Envía un CFE firmado a la DGI.
    /// </summary>
    /// <param name="xmlFirmado">XML del CFE firmado digitalmente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Respuesta de la DGI.</returns>
    public async Task<RespuestaDgi> EnviarCfeAsync(
        string xmlFirmado,
        CancellationToken cancellationToken = default)
    {
        var soapBody = ConstruirSoapEnvio(xmlFirmado);
        return await EnviarSolicitudAsync(soapBody, "enviarCFE", cancellationToken);
    }

    /// <summary>
    /// Consulta el estado de un CFE previamente enviado.
    /// </summary>
    /// <param name="rutEmisor">RUT del emisor.</param>
    /// <param name="tipoCfe">Tipo de CFE (número entero de la DGI).</param>
    /// <param name="serie">Serie del CFE.</param>
    /// <param name="numero">Número del CFE.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    public async Task<RespuestaDgi> ConsultarEstadoCfeAsync(
        string rutEmisor,
        int tipoCfe,
        string serie,
        long numero,
        CancellationToken cancellationToken = default)
    {
        var soapBody = ConstruirSoapConsulta(rutEmisor, tipoCfe, serie, numero);
        return await EnviarSolicitudAsync(soapBody, "consultarEstadoCFE", cancellationToken);
    }

    /// <summary>
    /// Envía el Reporte Diario de CFE a la DGI.
    /// </summary>
    /// <param name="fecha">Fecha del reporte.</param>
    /// <param name="cfesFirmados">Lista de XMLs firmados de los CFE del día.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    public async Task<RespuestaReporteDiario> EnviarReporteDiarioAsync(
        DateTime fecha,
        IEnumerable<string> cfesFirmados,
        CancellationToken cancellationToken = default)
    {
        var listaXmls = cfesFirmados.ToList();
        var soapBody = ConstruirSoapReporteDiario(fecha, listaXmls);
        var respuesta = await EnviarSolicitudAsync(soapBody, "enviarReporteDiario", cancellationToken);

        return new RespuestaReporteDiario
        {
            FechaReporte = fecha,
            CantidadCfe = listaXmls.Count,
            Respuesta = respuesta,
        };
    }

    private async Task<RespuestaDgi> EnviarSolicitudAsync(
        string soapEnvelope,
        string operacion,
        CancellationToken cancellationToken)
    {
        try
        {
            var client = _httpClient ?? throw new InvalidOperationException(
                "No se ha inicializado el HttpClient.");

            var content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
            content.Headers.Add("SOAPAction", SoapAction);

            var response = await client.PostAsync(
                _config.DgiSoapBaseUrl, content, cancellationToken);

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            return ProcesarRespuesta(responseBody, operacion);
        }
        catch (HttpRequestException ex)
        {
            throw new DgiCommunicationException(
                $"Error de red al invocar '{operacion}': {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DgiCommunicationException(
                $"Timeout al invocar '{operacion}'. Verifique el SoapTimeoutSegundos.", ex);
        }
        catch (DgiCommunicationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DgiCommunicationException(
                $"Error inesperado al invocar '{operacion}'.", ex);
        }
    }

    private static RespuestaDgi ProcesarRespuesta(string xmlRespuesta, string operacion)
    {
        try
        {
            var doc = new XmlDocument();
            doc.LoadXml(xmlRespuesta);

            var nsManager = new XmlNamespaceManager(doc.NameTable);
            nsManager.AddNamespace("soap", SoapNs);

            // Detectar SOAP Fault
            var fault = doc.SelectSingleNode("//soap:Fault", nsManager)
                      ?? doc.SelectSingleNode("//Fault");

            if (fault != null)
            {
                var faultString = fault.SelectSingleNode("faultstring")?.InnerText
                               ?? "Error SOAP sin detalle.";
                return RespuestaDgi.Error("SOAP_FAULT", faultString, xmlRespuesta);
            }

            // Intentar extraer codigo y mensaje de la respuesta DGI
            var codigo = doc.SelectSingleNode("//*[local-name()='Codigo']")?.InnerText
                      ?? doc.SelectSingleNode("//*[local-name()='Estado']")?.InnerText
                      ?? "OK";

            var mensaje = doc.SelectSingleNode("//*[local-name()='Mensaje']")?.InnerText
                       ?? doc.SelectSingleNode("//*[local-name()='Descripcion']")?.InnerText
                       ?? $"Respuesta de '{operacion}' procesada correctamente.";

            bool exitoso = codigo == "00" || codigo == "01";

            return exitoso
                ? RespuestaDgi.Exito(codigo, mensaje, xmlRespuesta)
                : RespuestaDgi.Error(codigo, mensaje, xmlRespuesta);
        }
        catch (Exception ex)
        {
            throw new DgiCommunicationException(
                $"Error al procesar la respuesta de '{operacion}'.", ex);
        }
    }

    private static string ConstruirSoapEnvio(string xmlFirmado)
    {
        var (doc, body) = CrearEnvelope();

        var enviarCfe = doc.CreateElement("enviarCFE");
        enviarCfe.SetAttribute("xmlns", "http://dgi.gub.uy/efactura");
        body.AppendChild(enviarCfe);

        var cfeEl = doc.CreateElement("cfe");
        cfeEl.AppendChild(doc.CreateCDataSection(xmlFirmado));
        enviarCfe.AppendChild(cfeEl);

        return SerializeXml(doc);
    }

    private static string ConstruirSoapConsulta(
        string rut, int tipo, string serie, long numero)
    {
        var (doc, body) = CrearEnvelope();

        var consultar = doc.CreateElement("consultarEstadoCFE");
        consultar.SetAttribute("xmlns", "http://dgi.gub.uy/efactura");
        body.AppendChild(consultar);

        AddTextEl(doc, consultar, "rut",      rut);
        AddTextEl(doc, consultar, "tipoCFE",  tipo.ToString(CultureInfo.InvariantCulture));
        AddTextEl(doc, consultar, "serie",    serie);
        AddTextEl(doc, consultar, "numero",   numero.ToString(CultureInfo.InvariantCulture));

        return SerializeXml(doc);
    }

    private static string ConstruirSoapReporteDiario(
        DateTime fecha, IList<string> cfes)
    {
        var (doc, body) = CrearEnvelope();

        var enviarReporte = doc.CreateElement("enviarReporteDiario");
        enviarReporte.SetAttribute("xmlns", "http://dgi.gub.uy/efactura");
        body.AppendChild(enviarReporte);

        AddTextEl(doc, enviarReporte, "fecha", CfeFormat.DateIso(fecha));

        var cfesEl = doc.CreateElement("cfes");
        foreach (var xml in cfes)
        {
            var cfeFirmadoEl = doc.CreateElement("cfeFirmado");
            cfeFirmadoEl.AppendChild(doc.CreateCDataSection(xml));
            cfesEl.AppendChild(cfeFirmadoEl);
        }
        enviarReporte.AppendChild(cfesEl);

        return SerializeXml(doc);
    }

    // -------------------------------------------------------------------------
    // XML helpers
    // -------------------------------------------------------------------------

    private static (XmlDocument Doc, XmlElement Body) CrearEnvelope()
    {
        var doc = new XmlDocument();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));

        var envelope = doc.CreateElement("soap", "Envelope", SoapNs);
        doc.AppendChild(envelope);

        var body = doc.CreateElement("soap", "Body", SoapNs);
        envelope.AppendChild(body);

        return (doc, body);
    }

    private static void AddTextEl(XmlDocument doc, XmlElement parent, string tag, string text)
    {
        var el = doc.CreateElement(tag);
        el.InnerText = text;
        parent.AppendChild(el);
    }

    /// <summary>
    /// Serializes an <see cref="XmlDocument"/> to a UTF-8 string.
    /// Using XmlWriter + MemoryStream ensures the XML declaration reflects the
    /// actual encoding of the string and avoids string-interpolation data flows
    /// that static analysis tools can misclassify as injection vectors.
    /// </summary>
    private static string SerializeXml(XmlDocument doc)
    {
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent   = false,
        };
        using (var writer = XmlWriter.Create(ms, settings))
            doc.WriteTo(writer);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private HttpClient CrearHttpClient()
    {
        var handler = new HttpClientHandler();

        // Disable TLS certificate validation only when the caller explicitly opts in.
        // Previously this was enabled automatically for the Homologación environment, but
        // doing so silently is a security risk. Callers must now set OmitirValidacionSsl = true.
        if (_config.OmitirValidacionSsl)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        if (!string.IsNullOrWhiteSpace(_config.RutaCertificado))
        {
            if (!File.Exists(_config.RutaCertificado))
                throw new Exceptions.UruFacturaException(
                    $"No se encontró el archivo de certificado: '{_config.RutaCertificado}'.");

            var cert = X509CertificateLoader.LoadPkcs12FromFile(
                _config.RutaCertificado, _config.PasswordCertificado);
            handler.ClientCertificates.Add(cert);
        }

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_config.SoapTimeoutSegundos),
            BaseAddress = new Uri(_config.DgiSoapBaseUrl),
        };

        client.DefaultRequestHeaders.Add("Accept", "text/xml");
        return client;
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_ownsHttpClient)
            _httpClient?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
