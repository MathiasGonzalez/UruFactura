using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.Enums;
using UruFactura.Models;
using UruFactura.Pdf;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;
using Xunit;

namespace UruFactura.Tests;

/// <summary>
/// Tests para <see cref="UruFacturaClientBuilder"/>.
/// Verifica la validación de configuración, los null-checks de los métodos With*
/// y que cada dependencia inyectada sea realmente la utilizada al operar el cliente.
/// </summary>
public class UruFacturaClientBuilderTests : IDisposable
{
    // Certificado autofirmado en disco para que WithDefaults pueda cargar CfeFirmante.
    private readonly string _certPath;
    private const string CertPassword = "test123";

    public UruFacturaClientBuilderTests()
    {
        _certPath = CrearCertificadoTemporal(CertPassword);
    }

    public void Dispose()
    {
        if (File.Exists(_certPath))
            File.Delete(_certPath);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private UruFacturaConfig ConfigValida() => new()
    {
        RutEmisor = "210000000012",
        RazonSocialEmisor = "Empresa Test S.A.",
        DomicilioFiscal = "Av. 18 de Julio 1234",
        Ciudad = "Montevideo",
        Departamento = "Montevideo",
        Ambiente = Ambiente.Homologacion,
        RutaCertificado = _certPath,
        PasswordCertificado = CertPassword,
    };

    /// <summary>Crea un certificado autofirmado en un archivo temporal.</summary>
    private static string CrearCertificadoTemporal(string password)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("cn=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var path = Path.Combine(Path.GetTempPath(), $"urufactura-test-{Guid.NewGuid()}.p12");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
        return path;
    }

    // -----------------------------------------------------------------------
    // WithDefaults — validación de configuración
    // -----------------------------------------------------------------------

    [Fact]
    public void WithDefaults_ConfigInvalida_LanzaExcepcion()
    {
        var config = ConfigValida() with { RutEmisor = "" };
        Assert.Throws<Exceptions.UruFacturaException>(() => UruFacturaClientBuilder.WithDefaults(config));
    }

    [Fact]
    public void WithDefaults_ConfigValida_BuildRetornaClienteNoNulo()
    {
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida()).Build();
        Assert.NotNull(client);
    }

    // -----------------------------------------------------------------------
    // Null-checks en métodos With*
    // -----------------------------------------------------------------------

    [Fact]
    public void WithXmlBuilder_Null_LanzaArgumentNullException()
    {
        var builder = UruFacturaClientBuilder.WithDefaults(ConfigValida());
        Assert.Throws<ArgumentNullException>(() => builder.WithXmlBuilder(null!));
    }

    [Fact]
    public void WithSigner_Null_LanzaArgumentNullException()
    {
        var builder = UruFacturaClientBuilder.WithDefaults(ConfigValida());
        Assert.Throws<ArgumentNullException>(() => builder.WithSigner(null!));
    }

    [Fact]
    public void WithCaeManager_Null_LanzaArgumentNullException()
    {
        var builder = UruFacturaClientBuilder.WithDefaults(ConfigValida());
        Assert.Throws<ArgumentNullException>(() => builder.WithCaeManager(null!));
    }

    [Fact]
    public void WithSoapClient_Null_LanzaArgumentNullException()
    {
        var builder = UruFacturaClientBuilder.WithDefaults(ConfigValida());
        Assert.Throws<ArgumentNullException>(() => builder.WithSoapClient(null!));
    }

    // -----------------------------------------------------------------------
    // Verificación de que cada With* inyecta la dependencia correcta
    // -----------------------------------------------------------------------

    [Fact]
    public void WithCaeManager_UsaManagerInyectado()
    {
        var caeStub = new CaeManagerStub();
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithCaeManager(caeStub)
            .Build();

        Assert.Same(caeStub, client.Cae);
    }

    [Fact]
    public void WithXmlBuilder_UsaBuilderInyectado()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithXmlBuilder(xmlStub)
            .WithSigner(new FirmanteStub())
            .WithSoapClient(new SoapClientStub())
            .Build();

        var cfe = client.CrearETicket();
        var xml = client.GenerarXml(cfe);

        Assert.True(xmlStub.LlamadoGenerar);
        Assert.Equal("<CFE/>", xml);
    }

    [Fact]
    public void WithSigner_UsaFirmanteInyectado()
    {
        var firmanteStub = new FirmanteStub();
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithXmlBuilder(new XmlBuilderStub("<CFE/>"))
            .WithSigner(firmanteStub)
            .WithSoapClient(new SoapClientStub())
            .Build();

        var cfe = client.CrearETicket();
        client.GenerarYFirmar(cfe);

        Assert.True(firmanteStub.LlamadoFirmar);
    }

    [Fact]
    public async Task WithSoapClient_UsaSoapClientInyectado()
    {
        var soapStub = new SoapClientStub();
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithXmlBuilder(new XmlBuilderStub("<CFE/>"))
            .WithSigner(new FirmanteStub())
            .WithSoapClient(soapStub)
            .Build();

        var cfe = client.CrearETicket();
        await client.EnviarCfeAsync(cfe);

        Assert.True(soapStub.LlamadoEnviarCfe);
    }

    [Fact]
    public void WithPdfGenerator_UsaGeneradorInyectado()
    {
        var pdfStub = new PdfGeneratorStub();
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithSigner(new FirmanteStub())
            .WithSoapClient(new SoapClientStub())
            .WithPdfGenerator(pdfStub)
            .Build();

        var cfe = client.CrearETicket();
        client.GenerarPdfA4(cfe);

        Assert.True(pdfStub.LlamadoGenerarA4);
    }

    [Fact]
    public void WithPdfGenerator_Null_GenerarPdfLanzaInvalidOperationException()
    {
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithSigner(new FirmanteStub())
            .WithSoapClient(new SoapClientStub())
            .WithPdfGenerator(null)
            .Build();

        var cfe = client.CrearETicket();
        Assert.Throws<InvalidOperationException>(() => client.GenerarPdfA4(cfe));
    }

    [Fact]
    public void WithDefaultPdf_ConfiguraPdfGenerator_NoLanzaInvalidOperation()
    {
        using var client = UruFacturaClientBuilder.WithDefaults(ConfigValida())
            .WithSigner(new FirmanteStub())
            .WithSoapClient(new SoapClientStub())
            .WithDefaultPdf()
            .Build();

        var cfe = client.CrearETicket();
        // Con generador configurado no debe lanzar InvalidOperationException.
        // Puede lanzar PdfGenerationException si el CFE está incompleto; eso está bien.
        var ex = Record.Exception(() => client.GenerarPdfA4(cfe));
        Assert.IsNotType<InvalidOperationException>(ex);
    }

    // -----------------------------------------------------------------------
    // Stubs internos
    // -----------------------------------------------------------------------

    private sealed class XmlBuilderStub(string xml = "<CFE/>") : ICfeXmlBuilder
    {
        public bool LlamadoGenerar { get; private set; }
        public string Generar(Cfe cfe) { LlamadoGenerar = true; return xml; }
    }

    private sealed class FirmanteStub : ICfeFirmante
    {
        public bool LlamadoFirmar { get; private set; }
        public string Firmar(string xmlSinFirmar) { LlamadoFirmar = true; return xmlSinFirmar.Replace("<CFE/>", "<CFE><Firmado/></CFE>"); }
        public byte[] ObtenerCertificadoDer() => [1, 2, 3];
        public void Dispose() { }
    }

    private sealed class SoapClientStub : IDgiSoapClient
    {
        public bool LlamadoEnviarCfe { get; private set; }

        public Task<RespuestaDgi> EnviarSobreAsync(string sobreXml, CancellationToken ct = default)
        {
            LlamadoEnviarCfe = true;
            return Task.FromResult(RespuestaDgi.Exito("AS", "OK"));
        }

        public Task<RespuestaDgi> ConsultarEstadoEnvioAsync(string idReceptor, string token, CancellationToken ct = default)
            => Task.FromResult(RespuestaDgi.Exito("AE", "OK"));

        public Task<RespuestaDgi> ConsultarEstadoCfeAsync(int tipo, string serie, long numero, CancellationToken ct = default)
            => Task.FromResult(RespuestaDgi.Exito("AE", "OK"));

        public Task<RespuestaDgi> EnviarReporteDiarioAsync(string reporteFirmadoXml, CancellationToken ct = default)
            => Task.FromResult(RespuestaDgi.Exito("AR", "OK"));

        public IDgiSoapClient WithHttpClient(HttpClient httpClient) => this;

        public void Dispose() { }
    }

    private sealed class PdfGeneratorStub : ICfePdfGenerator
    {
        public bool LlamadoGenerarA4 { get; private set; }
        public bool LlamadoGenerarTermico { get; private set; }
        public byte[] GenerarA4(Cfe cfe) { LlamadoGenerarA4 = true; return [1, 2, 3]; }
        public byte[] GenerarTermico(Cfe cfe) { LlamadoGenerarTermico = true; return [4, 5, 6]; }
    }

    private sealed class CaeManagerStub : ICaeManager
    {
        private readonly CaeManager _inner = new();
        public void RegistrarCae(Models.Cae cae) => _inner.RegistrarCae(cae);
        public void RegistrarCaes(IEnumerable<Models.Cae> caes) => _inner.RegistrarCaes(caes);
        public (Models.Cae Cae, long Numero) ObtenerProximoNumero(TipoCfe t) => _inner.ObtenerProximoNumero(t);
        public Models.Cae? ObtenerCaeActivo(TipoCfe t) => _inner.ObtenerCaeActivo(t);
        public IReadOnlyList<string> ObtenerAdvertencias(int d = 7, decimal p = 80m) => _inner.ObtenerAdvertencias(d, p);
        public IReadOnlyList<Models.Cae> ObtenerTodosLosCaes() => _inner.ObtenerTodosLosCaes();
        public IReadOnlyList<Models.Cae> ObtenerCaesActivos() => _inner.ObtenerCaesActivos();
        public string ResumenEstado() => _inner.ResumenEstado();
    }
}
