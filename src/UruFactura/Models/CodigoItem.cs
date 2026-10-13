namespace UruFactura.Models;

/// <summary>Código de un ítem (por ejemplo, GTIN o código interno).</summary>
public class CodigoItem
{
    /// <summary>Tipo de código (hasta 10 caracteres, p. ej. "EAN13", "INT1").</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Valor del código (hasta 35 caracteres).</summary>
    public string Codigo { get; set; } = string.Empty;
}
