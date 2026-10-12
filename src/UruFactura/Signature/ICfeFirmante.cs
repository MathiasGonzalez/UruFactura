namespace UruFactura.Signature;

/// <summary>
/// Contrato para la firma digital (XML Signature enveloped) de documentos de la DGI:
/// CFE y reporte diario.
/// </summary>
public interface ICfeFirmante : IDisposable
{
    /// <summary>
    /// Firma el XML con una firma enveloped (<c>ds:Signature</c> como último hijo del
    /// elemento raíz, <c>Reference URI=""</c>, transformadas enveloped-signature y C14N).
    /// </summary>
    /// <param name="xmlSinFirmar">XML sin firmar (UTF-8).</param>
    /// <returns>XML firmado.</returns>
    string Firmar(string xmlSinFirmar);

    /// <summary>
    /// Certificado público (DER) con el que se firma. Se incluye en la carátula del sobre
    /// (<c>X509Certificate</c>) y en el token de seguridad de WS-Security.
    /// </summary>
    byte[] ObtenerCertificadoDer();
}
