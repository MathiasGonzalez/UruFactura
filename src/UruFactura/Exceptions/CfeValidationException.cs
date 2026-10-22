namespace UruFactura;

/// <summary>
/// Error de validación del CFE antes de enviarlo a la DGI.
/// </summary>
public class CfeValidationException : UruFacturaException
{
    public IReadOnlyList<string> Errors { get; }

    public CfeValidationException(IReadOnlyList<string> errors)
        : base($"El CFE contiene {errors.Count} error(es) de validación: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }
}
