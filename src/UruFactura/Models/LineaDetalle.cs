using UruFactura.Formatting;

namespace UruFactura;

/// <summary>
/// Línea de detalle del CFE. Se numera según su posición en <see cref="Cfe.Detalle"/>.
/// En el e-Resguardo la línea no lleva nombre, cantidad ni precio: solo <see cref="Retenciones"/>.
/// </summary>
public sealed record LineaDetalle
{
    /// <summary>Nombre del ítem (hasta 80 caracteres).</summary>
    public string NombreItem { get; init; } = string.Empty;

    /// <summary>Descripción ampliada del ítem (<c>DscItem</c>, hasta 1000 caracteres, opcional).</summary>
    public string? Descripcion { get; init; }

    /// <summary>Cantidad (mayor a cero; se informa con 3 decimales).</summary>
    public decimal Cantidad { get; init; }

    /// <summary>Unidad de medida (hasta 4 caracteres, p. ej. "UN", "KG"); si se omite se informa "N/A".</summary>
    public string? UnidadMedida { get; init; }

    /// <summary>Precio unitario sin IVA (se informa con 6 decimales). Los remitos no llevan precio.</summary>
    public decimal PrecioUnitario { get; init; }

    /// <summary>Tipo de IVA de la línea. En exportación se informa siempre <c>IndFact</c> 10.</summary>
    public TipoIva IndFactIva { get; init; } = TipoIva.Basico;

    /// <summary>Descuento de la línea en monto (sin IVA, opcional).</summary>
    public decimal DescuentoMonto { get; init; }

    /// <summary>
    /// Descuento en porcentaje (0-100, opcional). Si <see cref="DescuentoMonto"/> es cero, el descuento
    /// es ese porcentaje de Cantidad × PrecioUnitario; si se informan ambos, se aplica el monto.
    /// </summary>
    public decimal? DescuentoPorcentaje { get; init; }

    /// <summary>Recargo de la línea en monto (sin IVA, opcional).</summary>
    public decimal RecargoMonto { get; init; }

    /// <summary>Códigos del ítem (<c>CodItem</c>, hasta 5), por ejemplo GTIN o código interno.</summary>
    public IReadOnlyList<CodigoItem> Codigos { get; init; } = [];

    /// <summary>Retenciones/percepciones de la línea (solo e-Resguardo, de 1 a 5).</summary>
    public IReadOnlyList<RetencionPercepcion> Retenciones { get; init; } = [];

    /// <summary>Descuento efectivo de la línea (monto, o porcentaje sobre el bruto si no hay monto).</summary>
    public decimal DescuentoEfectivo =>
        DescuentoMonto > 0 || DescuentoPorcentaje is null or <= 0
            ? CfeFormat.Redondear(DescuentoMonto)
            : CfeFormat.Redondear(Bruto * CfeFormat.Redondear(DescuentoPorcentaje.Value, 3) / 100m);

    /// <summary>
    /// Monto de la línea (Cantidad × PrecioUnitario − Descuento + Recargo), calculado con la precisión
    /// con que se escribe en el XML (cantidad 3 decimales, precio 6, montos 2) para que <c>MontoItem</c>
    /// coincida con lo que recalcula la DGI.
    /// </summary>
    public decimal MontoTotal => CfeFormat.Redondear(Bruto - DescuentoEfectivo + CfeFormat.Redondear(RecargoMonto));

    /// <summary>Cantidad × PrecioUnitario con la precisión del XML.</summary>
    internal decimal Bruto => CfeFormat.Redondear(Cantidad, 3) * CfeFormat.Redondear(PrecioUnitario, 6);
}
