namespace UruFactura;

/// <summary>Tipo de movimiento de un descuento o recargo global (<c>TpoMovDR</c>).</summary>
public enum TipoMovimientoGlobal
{
    /// <summary>Descuento (<c>D</c>): disminuye la base del impuesto.</summary>
    Descuento,

    /// <summary>Recargo (<c>R</c>): aumenta la base del impuesto.</summary>
    Recargo,
}
