namespace UruFactura;

/// <summary>
/// Descuento o recargo global del comprobante (zona <c>DscRcgGlobal</c>). Se aplica a la base
/// de un tipo de IVA (<see cref="IndFact"/>) y por lo tanto cambia los totales y el IVA.
/// Solo se admiten montos (<c>TpoDR</c> = 1), no porcentajes.
/// </summary>
public sealed record DescuentoRecargoGlobal
{
    /// <summary>Descuento o recargo.</summary>
    public TipoMovimientoGlobal Tipo { get; init; } = TipoMovimientoGlobal.Descuento;

    /// <summary>Descripción (hasta 100 caracteres).</summary>
    public string Glosa { get; init; } = string.Empty;

    /// <summary>Monto sin IVA, mayor a cero, en la moneda del comprobante.</summary>
    public decimal Monto { get; init; }

    /// <summary>Tipo de IVA de la base sobre la que se aplica.</summary>
    public TipoIva IndFact { get; init; } = TipoIva.Basico;

    /// <summary>Código asignado al concepto (hasta 3 dígitos, opcional).</summary>
    public int? Codigo { get; init; }
}
