using UruFactura.Formatting;

namespace UruFactura;

/// <summary>
/// Descuento o recargo global del comprobante (zona <c>DscRcgGlobal</c>). Se aplica a la base
/// de un indicador de facturación (<see cref="IndFact"/>) y por lo tanto cambia los totales y el IVA.
/// Se indica en monto o en porcentaje de esa base; en el XML se informa siempre el monto (<c>TpoDR</c> 1).
/// </summary>
public sealed record DescuentoRecargoGlobal
{
    /// <summary>Descuento o recargo.</summary>
    public TipoMovimientoGlobal Tipo { get; init; } = TipoMovimientoGlobal.Descuento;

    /// <summary>Descripción (hasta 100 caracteres).</summary>
    public string Glosa { get; init; } = string.Empty;

    /// <summary>Monto sin IVA, en la moneda del comprobante. Si es cero se usa <see cref="Porcentaje"/>.</summary>
    public decimal Monto { get; init; }

    /// <summary>
    /// Porcentaje (de 0 a 100, con hasta 3 decimales) de la suma de las líneas con el mismo
    /// <see cref="IndFact"/>. Se usa si <see cref="Monto"/> es cero.
    /// </summary>
    public decimal? Porcentaje { get; init; }

    /// <summary>Indicador de facturación de la base sobre la que se aplica.</summary>
    public IndicadorFacturacion IndFact { get; init; } = IndicadorFacturacion.TasaBasica;

    /// <summary>Código asignado al concepto (hasta 3 dígitos, opcional).</summary>
    public int? Codigo { get; init; }

    /// <summary>
    /// El monto que se informa (<c>ValorDR</c>), con 2 decimales: <see cref="Monto"/> o el porcentaje de
    /// <paramref name="base"/>, la suma de las líneas con su indicador.
    /// </summary>
    internal decimal MontoEfectivo(decimal @base) =>
        Monto != 0 || Porcentaje is null
            ? CfeFormat.Redondear(Monto)
            : CfeFormat.Redondear(@base * CfeFormat.Redondear(Porcentaje.Value, 3) / 100m);
}
