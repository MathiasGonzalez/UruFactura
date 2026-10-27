namespace UruFactura;

/// <summary>
/// Subtotal informativo del comprobante (zona <c>SubTotInfo</c>, hasta 20): por ejemplo, el subtotal de un grupo de
/// productos. No cambia la base del impuesto ni los totales. Se numera según su posición en
/// <see cref="Cfe.SubtotalesInformativos"/>.
/// </summary>
public sealed record SubtotalInformativo
{
    /// <summary>Título del subtotal (hasta 100 caracteres).</summary>
    public string Glosa { get; init; } = string.Empty;

    /// <summary>Valor del subtotal, en la moneda del comprobante (admite negativos).</summary>
    public decimal Valor { get; init; }

    /// <summary>Orden en que se imprime (1 a 99, opcional).</summary>
    public int? Orden { get; init; }
}
