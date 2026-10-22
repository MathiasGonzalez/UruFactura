namespace UruFactura;

/// <summary>Código de un ítem (<c>CodItem</c>), por ejemplo GTIN o código interno.</summary>
public sealed record CodigoItem
{
    /// <summary>Tipo de código (hasta 10 caracteres, p. ej. "EAN13", "INT1").</summary>
    public string Tipo { get; init; } = string.Empty;

    /// <summary>Valor del código (hasta 35 caracteres).</summary>
    public string Codigo { get; init; } = string.Empty;
}
