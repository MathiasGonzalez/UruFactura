using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using UruFactura.Enums;
using UruFactura.Exceptions;

namespace UruFactura.Signature;

/// <summary>
/// Firmante digital de documentos de la DGI de Uruguay (CFE y reporte diario).
/// Produce una firma XML Signature "enveloped" sobre todo el documento, con el mismo
/// formato que los ejemplos oficiales de la DGI: <c>Reference URI=""</c>, transformadas
/// enveloped-signature y C14N, y el certificado en <c>KeyInfo</c>.
/// </summary>
public class CfeFirmante : ICfeFirmante
{
    private readonly X509Certificate2 _certificado;
    private readonly AlgoritmoFirma _algoritmo;
    private bool _disposed;

    /// <summary>
    /// Inicializa el firmante con el certificado cargado en memoria.
    /// </summary>
    /// <param name="certificado">Certificado X.509 con clave privada.</param>
    /// <param name="algoritmo">Algoritmo de firma (por defecto SHA-256).</param>
    public CfeFirmante(X509Certificate2 certificado, AlgoritmoFirma algoritmo = AlgoritmoFirma.Sha256)
    {
        if (!certificado.HasPrivateKey)
            throw new FirmaDigitalException("El certificado no contiene clave privada.");
        _certificado = certificado;
        _algoritmo = algoritmo;
    }

    /// <summary>
    /// Inicializa el firmante desde un archivo .p12/.pfx.
    /// </summary>
    /// <param name="rutaArchivo">Ruta al archivo del certificado.</param>
    /// <param name="password">Contraseña del certificado.</param>
    /// <remarks>
    /// Se usa <see cref="X509KeyStorageFlags.EphemeralKeySet"/> para no persistir la clave
    /// privada en el almacén del sistema operativo (compatible con contenedores y Linux);
    /// en macOS ese modo no está soportado y se usa el almacenamiento por defecto.
    /// </remarks>
    public CfeFirmante(string rutaArchivo, string password)
        : this(CertificadoLoader.Cargar(rutaArchivo, password))
    {
    }

    /// <summary>Inicializa el firmante desde un archivo .p12/.pfx con el algoritmo indicado.</summary>
    public CfeFirmante(string rutaArchivo, string password, AlgoritmoFirma algoritmo)
        : this(CertificadoLoader.Cargar(rutaArchivo, password), algoritmo)
    {
    }

    /// <inheritdoc />
    public virtual byte[] ObtenerCertificadoDer() => _certificado.RawData;

    /// <inheritdoc />
    /// <exception cref="FirmaDigitalException">Si ocurre algún error durante la firma.</exception>
    public virtual string Firmar(string xmlSinFirmar)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xmlSinFirmar);

            using var rsa = _certificado.GetRSAPrivateKey()
                ?? throw new FirmaDigitalException(
                    "No se pudo obtener la clave RSA privada del certificado.");

            var signedXml = new SignedXml(doc) { SigningKey = rsa };
            signedXml.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigCanonicalizationUrl;
            signedXml.SignedInfo.SignatureMethod = _algoritmo == AlgoritmoFirma.Sha1
                ? SignedXml.XmlDsigRSASHA1Url
                : SignedXml.XmlDsigRSASHA256Url;

            var reference = new Reference
            {
                Uri = "",
                DigestMethod = _algoritmo == AlgoritmoFirma.Sha1
                    ? SignedXml.XmlDsigSHA1Url
                    : SignedXml.XmlDsigSHA256Url,
            };
            reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
            reference.AddTransform(new XmlDsigC14NTransform());
            signedXml.AddReference(reference);

            var keyInfo = new KeyInfo();
            keyInfo.AddClause(new KeyInfoX509Data(_certificado));
            signedXml.KeyInfo = keyInfo;

            signedXml.ComputeSignature();

            doc.DocumentElement!.AppendChild(doc.ImportNode(signedXml.GetXml(), true));

            // Sin re-indentar: cualquier cambio de espacios posterior a la firma la invalidaría.
            return doc.OuterXml;
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

    /// <summary>
    /// Verifica la firma digital de un XML firmado con <see cref="Firmar"/>.
    /// Comprueba que haya exactamente una firma, que sea hija directa de la raíz y que
    /// referencie el documento completo (<c>URI=""</c>), además de la validez criptográfica.
    /// No valida la cadena de confianza del certificado.
    /// </summary>
    /// <param name="xmlFirmado">XML firmado a verificar.</param>
    /// <returns>True si la firma es válida.</returns>
    public static bool VerificarFirma(string xmlFirmado)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(xmlFirmado);

            var signatureNodes = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            if (signatureNodes.Count != 1)
                return false;

            var signatureElement = (XmlElement)signatureNodes[0]!;
            if (signatureElement.ParentNode != doc.DocumentElement)
                return false;

            var signedXml = new SignedXml(doc);
            signedXml.LoadXml(signatureElement);

            if (signedXml.SignedInfo!.References.Count != 1
                || ((Reference)signedXml.SignedInfo.References[0]!).Uri != "")
                return false;

            return signedXml.CheckSignature();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public virtual void Dispose()
    {
        if (_disposed) return;
        _certificado.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
