namespace UruFactura;

/// <summary>
/// Error relacionado con el CAE (Constancia de Autorización de Emisión).
/// </summary>
public class CaeException : UruFacturaException
{
    public CaeException(string message) : base(message) { }
    public CaeException(string message, Exception innerException) : base(message, innerException) { }
}
