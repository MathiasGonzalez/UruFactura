using System.Globalization;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;

namespace UruFactura;

/// <summary>
/// Firma XML (XML Signature <i>enveloped</i>) de los documentos de la DGI: un <c>ds:Signature</c>
/// como último hijo de la raíz que referencia el documento completo (<c>URI=""</c>) con las
/// transformadas enveloped-signature y C14N, e incluye en <c>KeyInfo</c> el emisor y el número de serie
/// del certificado (<c>X509IssuerSerial</c>, zona I del formato del CFE) y el certificado.
/// </summary>
public static class FirmaDigital
{
    /// <summary>
    /// Verifica la firma de un XML firmado (por ejemplo, un CFE recibido de un proveedor) y devuelve el
    /// certificado con que se firmó; <c>null</c> si la firma no es válida: que haya exactamente una
    /// firma, hija directa de la raíz, que referencie el documento completo, que incluya el certificado
    /// y que sea criptográficamente válida con él.
    /// </summary>
    /// <param name="xmlFirmado">El documento firmado.</param>
    /// <param name="certificadoDelSobre">
    /// El certificado de la carátula del sobre, para las firmas que no incluyen el certificado sino solo su
    /// emisor y número de serie (<c>X509IssuerSerial</c>), como los CFE del sobre de ejemplo de la DGI. Se
    /// usa solo si el número de serie que indica la firma es el suyo. Se devuelve una copia.
    /// </param>
    /// <remarks>
    /// Una firma válida solo prueba que el documento no cambió desde que lo firmó el titular del
    /// certificado devuelto, que viene en el propio XML: compruebe que pertenece al emisor (por ejemplo,
    /// que su sujeto contenga el RUT del <c>RUCEmisor</c>) y, si corresponde, su cadena de confianza.
    /// </remarks>
    public static X509Certificate2? VerificarFirma(string xmlFirmado, X509Certificate2? certificadoDelSobre = null)
    {
        try
        {
            // Puede ser un CFE de un tercero: se carga sin DTD.
            var doc = Xml.XmlSeguro.Cargar(xmlFirmado, preservarEspacios: true);

            var firmas = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            if (firmas.Count != 1 || firmas[0]!.ParentNode != doc.DocumentElement)
                return null;

            var signedXml = new SignedXml(doc);
            signedXml.LoadXml((XmlElement)firmas[0]!);
            if (signedXml.SignedInfo!.References.Count != 1 || ((Reference)signedXml.SignedInfo.References[0]!).Uri != "")
                return null;

            // El firmante es el certificado de KeyInfo (puede venir con su cadena) cuya clave verifica la firma.
            var datos = signedXml.KeyInfo.OfType<KeyInfoX509Data>().ToList();
            var certificados = datos.SelectMany(d => d.Certificates?.OfType<X509Certificate2>() ?? []).ToList();
            if (certificados.Count == 0 && certificadoDelSobre is not null
                && datos.SelectMany(d => d.IssuerSerials?.OfType<X509IssuerSerial>() ?? []).Any(s => MismoSerial(s.SerialNumber, certificadoDelSobre)))
                certificados.Add(X509CertificateLoader.LoadCertificate(certificadoDelSobre.RawData));
            var firmante = certificados.FirstOrDefault(c => signedXml.CheckSignature(c, verifySignatureOnly: true));
            foreach (var otro in certificados.Where(c => c != firmante))
                otro.Dispose();
            return firmante;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Si el número de serie decimal de <c>X509IssuerSerial</c> es el del certificado.</summary>
    internal static bool MismoSerial(string? serialDecimal, X509Certificate2 certificado) =>
        BigInteger.TryParse(serialDecimal, NumberStyles.None, CultureInfo.InvariantCulture, out var serial)
        && serial == new BigInteger(certificado.SerialNumberBytes.Span, isUnsigned: true, isBigEndian: true);

    /// <summary>Firma el XML y devuelve el documento firmado y el <c>DigestValue</c> (base64) de la referencia.</summary>
    /// <exception cref="FirmaDigitalException">Si la firma falla.</exception>
    internal static (string Xml, string Digest) Firmar(string xmlSinFirmar, X509Certificate2 certificado, AlgoritmoFirma algoritmo)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xmlSinFirmar);

            using var rsa = certificado.GetRSAPrivateKey()
                ?? throw new FirmaDigitalException("No se pudo obtener la clave RSA privada del certificado.");

            var sha1 = algoritmo == AlgoritmoFirma.Sha1;
            var signedXml = new SignedXml(doc) { SigningKey = rsa };
            signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl;
            signedXml.SignedInfo.SignatureMethod = sha1 ? SignedXml.XmlDsigRSASHA1Url : SignedXml.XmlDsigRSASHA256Url;

            var reference = new Reference
            {
                Uri = "",
                DigestMethod = sha1 ? SignedXml.XmlDsigSHA1Url : SignedXml.XmlDsigSHA256Url,
            };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(reference);

            // X509IssuerSerial lleva el número de serie en decimal: AddIssuerSerial lo convierte desde el hexadecimal
            // del certificado, que se toma sin signo.
            var x509 = new KeyInfoX509Data(certificado);
            x509.AddIssuerSerial(certificado.Issuer, "0" + certificado.SerialNumber);
            var keyInfo = new KeyInfo();
            keyInfo.AddClause(x509);
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();
            doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));

            // Sin re-indentar: cualquier cambio de espacios posterior a la firma la invalidaría.
            return (doc.OuterXml, Convert.ToBase64String(reference.DigestValue!));
        }
        catch (FirmaDigitalException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FirmaDigitalException("Error al firmar el XML.", ex);
        }
    }
}
