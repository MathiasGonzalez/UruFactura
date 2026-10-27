namespace UruFactura;

/// <summary>
/// Parte en que se distribuye el descuento o el recargo de una línea (<c>SubDescuento</c> / <c>SubRecargo</c>, hasta
/// 5 por línea). Es informativa: el monto de la línea sale de <see cref="LineaDetalle.DescuentoMonto"/> y
/// <see cref="LineaDetalle.RecargoMonto"/> (o de sus porcentajes).
/// </summary>
public sealed record DistribucionAjuste
{
    /// <summary>Si <see cref="Valor"/> es un monto o un porcentaje (<c>DescTipo</c> / <c>RecargoTipo</c>).</summary>
    public TipoValor Tipo { get; init; } = TipoValor.Monto;

    /// <summary>Valor de la parte: un monto en la moneda del comprobante o un porcentaje (2 decimales).</summary>
    public decimal Valor { get; init; }
}

/// <summary>Si un valor se expresa en monto o en porcentaje. El valor es el código de la DGI.</summary>
public enum TipoValor
{
    /// <summary>En monto, en la moneda del comprobante (1).</summary>
    Monto = 1,

    /// <summary>En porcentaje (2).</summary>
    Porcentaje = 2,
}
