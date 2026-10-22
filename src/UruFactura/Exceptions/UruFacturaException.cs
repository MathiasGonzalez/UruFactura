namespace UruFactura;

/// <summary>
/// Excepción base de UruFactura.
/// </summary>
public class UruFacturaException : Exception
{
    public UruFacturaException(string message) : base(message) { }
    public UruFacturaException(string message, Exception innerException) : base(message, innerException) { }
}
