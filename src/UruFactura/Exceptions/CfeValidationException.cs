namespace UruFactura;

/// <summary>
/// El CFE no es válido: no se emitió ni se consumió ningún número.
/// </summary>
public class CfeValidationException : UruFacturaException
{
    /// <summary>Errores de validación, con la propiedad afectada de cada uno.</summary>
    public IReadOnlyList<ErrorValidacion> Errores { get; }

    public CfeValidationException(IReadOnlyList<ErrorValidacion> errores)
        : base($"El CFE tiene {errores.Count} error(es) de validación: {string.Join("; ", errores)}")
    {
        Errores = errores;
    }
}
