using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using UruFactura.Signature;
using UruFactura.Xml;

namespace UruFactura.Tests;

/// <summary>Firma XML Signature enveloped de los documentos de la DGI (CFE y reporte) y carga del certificado.</summary>
public class FirmaDigitalTests
{
    private static readonly X509Certificate2 Cert = TestData.Certificado;

    private static string XmlSinFirmar()
    {
        var cfe = TestData.CfeValido() with { Emisor = TestData.Emisor() };
        return CfeXml.Generar(cfe, TestData.Cae(), 1, cfe.CalcularTotales(), DateTimeOffset.UtcNow);
    }

    private static string Firmar(string xml, AlgoritmoFirma algoritmo = AlgoritmoFirma.Sha256) =>
        FirmaDigital.Firmar(xml, Cert, algoritmo).Xml;

    private static XmlDocument Cargar(string xml)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(xml);
        return doc;
    }

    [Fact]
    public void Firmar_AgregaUnaSolaSignatureComoUltimoHijoDeLaRaiz()
    {
        var doc = Cargar(Firmar(XmlSinFirmar()));

        var firmas = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
        Assert.Equal(1, firmas.Count);
        Assert.Same(doc.DocumentElement!.LastChild, firmas[0]);
    }

    [Fact]
    public void Firmar_ReferenciaAlDocumentoCompletoConEnvelopedYC14N_SinXades()
    {
        var xml = Firmar(XmlSinFirmar());
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
    public void Firmar_IncluyeElCertificadoEnKeyInfo_YDevuelveElDigest()
    {
        var (xml, digest) = FirmaDigital.Firmar(XmlSinFirmar(), Cert, AlgoritmoFirma.Sha256);
        var doc = Cargar(xml);
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("ds", SignedXml.XmlDsigNamespaceUrl);

        Assert.Equal(Convert.ToBase64String(Cert.RawData), doc.SelectSingleNode("//ds:KeyInfo/ds:X509Data/ds:X509Certificate", nsm)!.InnerText);
        Assert.Equal(digest, doc.SelectSingleNode("//ds:Reference/ds:DigestValue", nsm)!.InnerText);
    }

    [Theory]
    [InlineData(AlgoritmoFirma.Sha256, "xmldsig-more#rsa-sha256", "xmlenc#sha256")]
    [InlineData(AlgoritmoFirma.Sha1, "xmldsig#rsa-sha1", "xmldsig#sha1")]
    public void Firmar_UsaElAlgoritmoConfigurado(AlgoritmoFirma algoritmo, string firma, string digest)
    {
        var xml = Firmar(XmlSinFirmar(), algoritmo);

        Assert.Contains(firma, xml);
        Assert.Contains(digest, xml);
        Assert.NotNull(FirmaDigital.VerificarFirma(xml));
    }

    [Fact]
    public void Firmar_NoReindenta_LaFirmaSigueSiendoValida()
    {
        var xml = Firmar(XmlSinFirmar());

        Assert.NotNull(FirmaDigital.VerificarFirma(xml));
        Assert.StartsWith("<?xml", xml);
    }

    [Fact]
    public void Firmar_XmlInvalido_LanzaFirmaDigitalException() =>
        Assert.Throws<FirmaDigitalException>(() => Firmar("<sin-cerrar>"));

    [Fact]
    public void Verificar_XmlModificado_DevuelveNull()
    {
        var xml = Firmar(XmlSinFirmar());
        var alterado = xml.Replace("<MntTotal>244.00</MntTotal>", "<MntTotal>1.00</MntTotal>");

        Assert.NotEqual(xml, alterado);
        Assert.Null(FirmaDigital.VerificarFirma(alterado));
    }

    [Fact]
    public void Verificar_SinFirma_DevuelveNull() => Assert.Null(FirmaDigital.VerificarFirma(XmlSinFirmar()));

    [Fact]
    public void Verificar_XmlConDtd_DevuelveNull() =>
        Assert.Null(FirmaDigital.VerificarFirma(Firmar(XmlSinFirmar()).Replace("?>", "?><!DOCTYPE CFE [<!ENTITY x \"x\">]>")));

    [Fact]
    public void Verificar_XmlNoValido_DevuelveNull() => Assert.Null(FirmaDigital.VerificarFirma("no es xml"));

    [Fact]
    public void Verificar_FirmaQueNoEsHijaDeLaRaiz_DevuelveNull()
    {
        var doc = Cargar(Firmar(XmlSinFirmar()));

        // Signature wrapping: mover la firma dentro de otro elemento.
        var firma = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)[0]!;
        firma.ParentNode!.RemoveChild(firma);
        doc.DocumentElement!.FirstChild!.AppendChild(firma);

        Assert.Null(FirmaDigital.VerificarFirma(doc.OuterXml));
    }

    [Fact]
    public void Verificar_ReferenciaQueNoEsElDocumentoCompleto_DevuelveNull() =>
        Assert.Null(FirmaDigital.VerificarFirma(Firmar(XmlSinFirmar()).Replace("<Reference URI=\"\"", "<Reference URI=\"#otro\"")));

    [Fact]
    public void Verificar_DevuelveElCertificadoFirmante()
    {
        using var firmante = FirmaDigital.VerificarFirma(Firmar(XmlSinFirmar()));

        Assert.Equal(Cert.Thumbprint, firmante!.Thumbprint);
    }

    [Fact]
    public void Verificar_FirmadoConOtroCertificado_DevuelveEseCertificado()
    {
        // La firma de un CFE falso, hecha con cualquier certificado, es válida: lo que lo delata es
        // el firmante, que el llamador compara con el emisor.
        using var otro = TestData.CertificadoAutofirmado();

        using var firmante = FirmaDigital.VerificarFirma(FirmaDigital.Firmar(XmlSinFirmar(), otro, AlgoritmoFirma.Sha256).Xml);

        Assert.Equal(otro.Thumbprint, firmante!.Thumbprint);
        Assert.NotEqual(Cert.Thumbprint, firmante.Thumbprint);
    }

    [Fact]
    public void Verificar_CertificadoReemplazadoEnKeyInfo_DevuelveNull()
    {
        // Poner otro certificado en KeyInfo no hace pasar la firma como suya.
        using var otro = TestData.CertificadoAutofirmado();
        var xml = Firmar(XmlSinFirmar())
            .Replace(Convert.ToBase64String(Cert.RawData), Convert.ToBase64String(otro.RawData));

        Assert.Null(FirmaDigital.VerificarFirma(xml));
    }

    // --- Carga del certificado ---

    [Fact]
    public void CargarCertificado_ArchivoP12_FirmaCorrectamente()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"urufactura_test_{Guid.NewGuid():N}.p12");
        try
        {
            File.WriteAllBytes(ruta, Cert.Export(X509ContentType.Pfx, "pw"));
            using var cargado = CertificadoLoader.Cargar(ruta, "pw");

            Assert.NotNull(FirmaDigital.VerificarFirma(FirmaDigital.Firmar(XmlSinFirmar(), cargado, AlgoritmoFirma.Sha256).Xml));
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    [Fact]
    public void CargarCertificado_ContrasenaIncorrecta_LanzaFirmaDigitalException()
    {
        var ruta = Path.Combine(Path.GetTempPath(), $"urufactura_test_{Guid.NewGuid():N}.p12");
        try
        {
            File.WriteAllBytes(ruta, Cert.Export(X509ContentType.Pfx, "pw"));
            var ex = Assert.Throws<FirmaDigitalException>(() => CertificadoLoader.Cargar(ruta, "mal"));
            Assert.Contains("contraseña", ex.Message);
        }
        finally
        {
            File.Delete(ruta);
        }
    }

    [Fact]
    public void CargarCertificado_Inexistente_LanzaFirmaDigitalException() =>
        Assert.Throws<FirmaDigitalException>(() => CertificadoLoader.Cargar("/no/existe.p12", "pw"));

    [Fact]
    public void CertificadoSinClavePrivada_LanzaFirmaDigitalException()
    {
        using var soloPublico = X509CertificateLoader.LoadCertificate(Cert.RawData);
        Assert.Throws<FirmaDigitalException>(() => CertificadoLoader.ValidarClavePrivada(soloPublico));
    }
}
