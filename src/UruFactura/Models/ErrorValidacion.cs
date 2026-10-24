namespace UruFactura;

/// <summary>Error de validación de un CFE: la propiedad afectada y el motivo.</summary>
/// <param name="Campo">
/// Ruta de la propiedad dentro del <see cref="Cfe"/>, por ejemplo <c>Detalle[0].PrecioUnitario</c>,
/// <c>Receptor.Documento</c> o <c>Referencias[1].Serie</c>. Sirve para mostrar el error junto al campo.
/// </param>
/// <param name="Mensaje">Motivo del error.</param>
public sealed record ErrorValidacion(string Campo, string Mensaje)
{
    /// <inheritdoc />
    public override string ToString() => $"{Campo}: {Mensaje}";
}
