using UruFactura.Models;
using UruFactura.Pdf;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;

namespace UruFactura.Tests;

// Dobles de prueba compartidos por los tests del cliente y del builder (sin red, disco ni firma real).

internal sealed class XmlBuilderStub(string xml = "<CFE/>") : ICfeXmlBuilder
{
    public bool LlamadoGenerar { get; private set; }
    public string Generar(Cfe cfe) { LlamadoGenerar = true; return xml; }
}

internal sealed class FirmanteStub : ICfeFirmante
{
    public bool LlamadoFirmar => ContadorFirmar > 0;
    public int ContadorFirmar { get; set; }
    public bool Disposed { get; private set; }

    public string Firmar(string xmlSinFirmar)
    {
        ContadorFirmar++;
        return xmlSinFirmar.Replace("<CFE/>", "<CFE><Firmado/></CFE>");
    }

    public byte[] ObtenerCertificadoDer() => [1, 2, 3];
    public void Dispose() { Disposed = true; }
}

internal sealed class SoapClientStub(
    string codigo = "AS",
    string mensaje = "OK",
    bool exitoso = true) : IDgiSoapClient
{
    public bool LlamadoEnviarCfe { get; private set; }
    public bool LlamadoConsultarEstado { get; private set; }
    public bool LlamadoEnviarReporte { get; private set; }
    public bool Disposed { get; private set; }
    public string? UltimoSobre { get; private set; }
    public string? UltimoReporte { get; private set; }
    public string? UltimoIdReceptorConsultado { get; private set; }
    public List<EstadoComprobante> EstadoEnvio { get; } = [];

    private RespuestaDgi Respuesta() => exitoso
        ? RespuestaDgi.Exito(codigo, mensaje)
        : RespuestaDgi.Error(codigo, mensaje);

    public Task<RespuestaDgi> EnviarSobreAsync(string sobreXml, CancellationToken ct = default)
    {
        LlamadoEnviarCfe = true;
        UltimoSobre = sobreXml;
        var r = Respuesta();
        r.IdReceptor = "555";
        r.Token = "tok";
        return Task.FromResult(r);
    }

    public Task<RespuestaDgi> ConsultarEstadoEnvioAsync(string idReceptor, string token, CancellationToken ct = default)
    {
        LlamadoConsultarEstado = true;
        UltimoIdReceptorConsultado = idReceptor;
        var r = Respuesta();
        r.Comprobantes = EstadoEnvio;
        return Task.FromResult(r);
    }

    public Task<RespuestaDgi> ConsultarEstadoCfeAsync(int tipo, string serie, long numero, CancellationToken ct = default)
    {
        LlamadoConsultarEstado = true;
        return Task.FromResult(Respuesta());
    }

    public Task<RespuestaDgi> EnviarReporteDiarioAsync(string reporteFirmadoXml, CancellationToken ct = default)
    {
        LlamadoEnviarReporte = true;
        UltimoReporte = reporteFirmadoXml;
        return Task.FromResult(Respuesta());
    }

    public IDgiSoapClient WithHttpClient(HttpClient httpClient) => this;
    public void Dispose() { Disposed = true; }
}

internal sealed class PdfGeneratorStub : ICfePdfGenerator
{
    public bool LlamadoGenerarA4 { get; private set; }
    public bool LlamadoGenerarTermico { get; private set; }
    public byte[] GenerarA4(Cfe cfe) { LlamadoGenerarA4 = true; return [1, 2, 3]; }
    public byte[] GenerarTermico(Cfe cfe) { LlamadoGenerarTermico = true; return [4, 5, 6]; }
}
