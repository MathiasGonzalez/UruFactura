namespace UruFactura;

/// <summary>
/// Error al generar el PDF del comprobante.
/// </summary>
public class PdfGenerationException : UruFacturaException
{
    public PdfGenerationException(string message) : base(message) { }
    public PdfGenerationException(string message, Exception innerException) : base(message, innerException) { }
}
