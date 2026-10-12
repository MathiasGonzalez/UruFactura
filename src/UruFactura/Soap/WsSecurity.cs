using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using UruFactura.Enums;

namespace UruFactura.Soap;

/// <summary>
/// Firma WS-Security (X.509 Token Profile) de los mensajes SOAP hacia la DGI:
/// agrega un <c>BinarySecurityToken</c> con el certificado y una firma XML (exc-c14n) del
/// <c>Body</c> referenciada por <c>wsu:Id</c>, con el mismo formato que los ejemplos de la DGI.
/// </summary>
internal static class WsSecurity
{
    internal const string SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";
    internal const string DgiNs = "http://dgi.gub.uy";
    private const string WsseNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
    private const string WsuNs = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
    private const string Base64Enc = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary";
    private const string X509ValueType = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-x509-token-profile-1.0#X509v3";

    /// <summary>
    /// Construye el sobre SOAP con <paramref name="bodyContenido"/> dentro del Body y lo firma.
    /// </summary>
    /// <param name="bodyContenido">Función que crea el elemento de la operación dentro de <c>doc</c>.</param>
    internal static string CrearYFirmar(
        Func<XmlDocument, XmlElement> bodyContenido,
        X509Certificate2 certificado,
        AlgoritmoFirma algoritmo)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", null));

        var envelope = doc.CreateElement("soapenv", "Envelope", SoapNs);
        envelope.SetAttribute("xmlns:dgi", DgiNs);
        doc.AppendChild(envelope);

        var header = doc.CreateElement("soapenv", "Header", SoapNs);
        envelope.AppendChild(header);

        var bodyId = "id-" + Guid.NewGuid().ToString("N");
        var certId = "CertId-" + Guid.NewGuid().ToString("N");

        var body = doc.CreateElement("soapenv", "Body", SoapNs);
        body.SetAttribute("xmlns:wsu", WsuNs);
        var bodyIdAttr = doc.CreateAttribute("wsu", "Id", WsuNs);
        bodyIdAttr.Value = bodyId;
        body.SetAttributeNode(bodyIdAttr);
        body.AppendChild(bodyContenido(doc));
        envelope.AppendChild(body);

        var security = doc.CreateElement("wsse", "Security", WsseNs);
        var mustUnderstand = doc.CreateAttribute("soapenv", "mustUnderstand", SoapNs);
        mustUnderstand.Value = "1";
        security.SetAttributeNode(mustUnderstand);
        header.AppendChild(security);

        var token = doc.CreateElement("wsse", "BinarySecurityToken", WsseNs);
        token.SetAttribute("EncodingType", Base64Enc);
        token.SetAttribute("ValueType", X509ValueType);
        token.SetAttribute("xmlns:wsu", WsuNs);
        var tokenId = doc.CreateAttribute("wsu", "Id", WsuNs);
        tokenId.Value = certId;
        token.SetAttributeNode(tokenId);
        token.InnerText = Convert.ToBase64String(certificado.RawData);
        security.AppendChild(token);

        using var rsa = certificado.GetRSAPrivateKey()
            ?? throw new Exceptions.FirmaDigitalException(
                "No se pudo obtener la clave RSA privada del certificado para WS-Security.");

        var signedXml = new SignedXmlWsu(doc) { SigningKey = rsa };
        signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;
        signedXml.SignedInfo.SignatureMethod = algoritmo == AlgoritmoFirma.Sha1
            ? SignedXml.XmlDsigRSASHA1Url
            : SignedXml.XmlDsigRSASHA256Url;

        var reference = new Reference("#" + bodyId)
        {
            DigestMethod = algoritmo == AlgoritmoFirma.Sha1
                ? SignedXml.XmlDsigSHA1Url
                : SignedXml.XmlDsigSHA256Url,
        };
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        // KeyInfo -> wsse:SecurityTokenReference -> #BinarySecurityToken
        var str = doc.CreateElement("wsse", "SecurityTokenReference", WsseNs);
        var strRef = doc.CreateElement("wsse", "Reference", WsseNs);
        strRef.SetAttribute("URI", "#" + certId);
        strRef.SetAttribute("ValueType", X509ValueType);
        str.AppendChild(strRef);
        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoNode(str));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        security.AppendChild(doc.ImportNode(signedXml.GetXml(), true));

        // Sin re-indentar: cualquier cambio de espacios posterior a la firma la invalidaría.
        return doc.OuterXml;
    }

    /// <summary><see cref="SignedXml"/> que resuelve referencias <c>#id</c> a atributos <c>wsu:Id</c>.</summary>
    private sealed class SignedXmlWsu(XmlDocument document) : SignedXml(document)
    {
        public override XmlElement? GetIdElement(XmlDocument? doc, string idValue)
        {
            var el = base.GetIdElement(doc, idValue);
            if (el is not null || doc is null)
                return el;

            return Buscar(doc.DocumentElement, idValue);
        }

        private static XmlElement? Buscar(XmlNode? nodo, string id)
        {
            if (nodo is XmlElement e)
            {
                if (e.GetAttribute("Id", WsuNs) == id)
                    return e;
                foreach (XmlNode hijo in e.ChildNodes)
                    if (Buscar(hijo, id) is { } found)
                        return found;
            }
            return null;
        }
    }
}
