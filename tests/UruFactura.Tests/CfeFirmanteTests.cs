using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Signature;
using UruFactura.Xml;
using Xunit;

namespace UruFactura.Tests;

/// <summary>
/// Tests de la firma XML Signature enveloped de los documentos DGI (CFE y reporte).
/// Se utiliza un certificado auto-firmado generado en memoria (solo para tests).
/// </summary>
public class CfeFirmanteTests : IDisposable
{
    private readonly X509Certificate2 _certificadoTest = TestData.CertificadoAutofirmado();
    private readonly CfeXmlBuilder _builder = new();

    public void Dispose() => _certificadoTest.Dispose();

    private string XmlSinFirmar() => _builder.Generar(TestData.CfeValido());

    private static XmlDocument Cargar(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    [Fact]
    public void Constructor_CertificadoSinClavePrivada_LanzaFirmaDigitalException()
    {
        using var soloPublico = X509CertificateLoader.LoadCertificate(_certificadoTest.RawData);
        Assert.Throws<FirmaDigitalException>(() => new CfeFirmante(soloPublico));
    }

    [Fact]
    public void Firmar_AgregaUnaSolaSignatureComoUltimoHijoDeLaRaiz()
    {
        using var firmante = new CfeFirmante(_certificadoTest);

        var doc = Cargar(firmante.Firmar(XmlSinFirmar()));

        var firmas = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        Assert.Equal(1, firmas.Count);
        Assert.Same(doc.DocumentElement!.LastChild, firmas[0]);
    }

    [Fact]
    public void Firmar_ReferenciaAlDocumentoCompletoConEnvelopedYC14N_SinXades()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var xml = firmante.Firmar(XmlSinFirmar());
        var doc = Cargar(xml);

        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        var reference = doc.SelectSingleNode("//ds:Reference", nsm)!;
        Assert.Equal("", reference.Attributes!["URI"]!.Value);
        Assert.Equal(1, doc.SelectNodes("//ds:Reference", nsm)!.Count);

        var transforms = doc.SelectNodes("//ds:Reference/ds:Transforms/ds:Transform/@Algorithm", nsm)!
            .Cast<XmlNode>().Select(n => n.Value).ToList();
        Assert.Equal([SignedXml.XmlDsigEnvelopedSignatureTransformUrl, SignedXml.XmlDsigC14NTransformUrl], transforms);

        Assert.DoesNotContain("QualifyingProperties", xml);
        Assert.DoesNotContain("SignedProperties", xml);
    }

    [Fact]
    public void Firmar_IncluyeElCertificadoEnKeyInfo()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var doc = Cargar(firmante.Firmar(XmlSinFirmar()));

        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);
        var cert = doc.SelectSingleNode("//ds:KeyInfo/ds:X509Data/ds:X509Certificate", nsm)!.InnerText;

        Assert.Equal(Convert.ToBase64String(_certificadoTest.RawData), cert);
        Assert.Equal(_certificadoTest.RawData, firmante.ObtenerCertificadoDer());
    }

    [Theory]
    [InlineData(AlgoritmoFirma.Sha256, "xmldsig-more#rsa-sha256", "xmlenc#sha256")]
    [InlineData(AlgoritmoFirma.Sha1, "xmldsig#rsa-sha1", "xmldsig#sha1")]
    public void Firmar_UsaElAlgoritmoConfigurado(AlgoritmoFirma algoritmo, string firma, string digest)
    {
        using var firmante = new CfeFirmante(_certificadoTest, algoritmo);
        var xml = firmante.Firmar(XmlSinFirmar());

        Assert.Contains(firma, xml);
        Assert.Contains(digest, xml);
        Assert.True(CfeFirmante.VerificarFirma(xml));
    }

    [Fact]
    public void Firmar_NoReindentaDespuesDeFirmar_LaFirmaSigueSiendoValida()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var xml = firmante.Firmar(XmlSinFirmar());

        Assert.True(CfeFirmante.VerificarFirma(xml));
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void VerificarFirma_XmlModificado_RetornaFalse()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var xml = firmante.Firmar(XmlSinFirmar());

        var alterado = xml.Replace("<MntTotal>244.00</MntTotal>", "<MntTotal>1.00</MntTotal>");

        Assert.NotEqual(xml, alterado);
        Assert.False(CfeFirmante.VerificarFirma(alterado));
    }

    [Fact]
    public void VerificarFirma_SinFirma_RetornaFalse() =>
        Assert.False(CfeFirmante.VerificarFirma(XmlSinFirmar()));

    [Fact]
    public void VerificarFirma_XmlConDtd_RetornaFalse()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var xml = firmante.Firmar(XmlSinFirmar());
        var conDtd = xml.Replace("?>", "?><!DOCTYPE CFE [<!ENTITY x \"x\">]>");

        Assert.False(CfeFirmante.VerificarFirma(conDtd));
    }

    [Fact]
    public void VerificarFirma_XmlNoValido_RetornaFalse() =>
        Assert.False(CfeFirmante.VerificarFirma("no es xml"));

    [Fact]
    public void VerificarFirma_FirmaQueNoEsHijaDeLaRaiz_RetornaFalse()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var doc = Cargar(firmante.Firmar(XmlSinFirmar()));

        // Signature wrapping: mover la firma dentro de otro elemento.
        var firma = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        firma.ParentNode!.RemoveChild(firma);
        doc.DocumentElement!.FirstChild!.AppendChild(firma);

        Assert.False(CfeFirmante.VerificarFirma(doc.OuterXml));
    }

    [Fact]
    public void VerificarFirma_ReferenciaQueNoEsElDocumentoCompleto_RetornaFalse()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        var xml = firmante.Firmar(XmlSinFirmar());

        var conOtraUri = xml.Replace("<Reference URI=\"\"", "<Reference URI=\"#otro\"");

        Assert.False(CfeFirmante.VerificarFirma(conOtraUri));
    }

    [Fact]
    public void Firmar_XmlInvalido_LanzaFirmaDigitalException()
    {
        using var firmante = new CfeFirmante(_certificadoTest);
        Assert.Throws<FirmaDigitalException>(() => firmante.Firmar("<sin-cerrar>"));
    }

    [Fact]
    public void Firmar_ArchivoP12_FirmaCorrectamente()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"urufactura_test_{Guid.NewGuid():N}.p12");
        try
        {
            File.WriteAllBytes(ruta, _certificadoTest.Export(X509ContentType.Pfx, "pw"));
            using var firmante = new CfeFirmante(ruta, "pw");

            Assert.True(CfeFirmante.VerificarFirma(firmante.Firmar(XmlSinFirmar())));
        }
        finally
        {
            File.Delete(ruta);
        }
    }
}
