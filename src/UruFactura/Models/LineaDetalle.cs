using UruFactura.Enums;
using UruFactura.Formatting;

namespace UruFactura.Models;

/// <summary>
/// Línea de detalle del CFE.
/// </summary>
public class LineaDetalle
{
    /// <summary>Número de línea (correlativo a partir de 1).</summary>
    public int NroLinea { get; set; }

    /// <summary>Cantidad del ítem.</summary>
    public decimal Cantidad { get; set; }

    /// <summary>Unidad de medida (p. ej. "UN", "KG").</summary>
    public string? UnidadMedida { get; set; }

    /// <summary>Nombre del ítem (hasta 80 caracteres).</summary>
    public string NombreItem { get; set; } = string.Empty;

    /// <summary>Descripción ampliada del ítem (<c>DscItem</c>, hasta 1000 caracteres, opcional).</summary>
    public string? Descripcion { get; set; }

    /// <summary>Códigos del ítem (<c>CodItem</c>, hasta 5), por ejemplo GTIN o código interno.</summary>
    public List<CodigoItem> Codigos { get; set; } = [];

    /// <summary>
    /// Retenciones/percepciones de la línea. Solo se usan en el e-Resguardo (1 a 5 por línea);
    /// en ese tipo la línea no lleva nombre, cantidad ni precio.
    /// </summary>
    public List<RetencionPercepcion> Retenciones { get; set; } = [];

    /// <summary>Precio unitario sin IVA.</summary>
    public decimal PrecioUnitario { get; set; }

    /// <summary>Indicador de IVA aplicable.</summary>
    public TipoIva IndFactIva { get; set; } = TipoIva.Basico;

    /// <summary>Monto del descuento por línea (sin IVA, opcional).</summary>
    public decimal DescuentoMonto { get; set; }

    /// <summary>
    /// Porcentaje de descuento (0-100, opcional). Si <see cref="DescuentoMonto"/> es cero, el descuento
    /// se calcula como ese porcentaje de (Cantidad × PrecioUnitario); si ambos se informan, el monto
    /// es el que se aplica y el porcentaje solo se informa en el XML.
    /// </summary>
    public decimal? DescuentoPorcentaje { get; set; }

    /// <summary>Monto del recargo por línea (sin IVA, opcional).</summary>
    public decimal RecargoMonto { get; set; }

    /// <summary>Descuento efectivo de la línea (monto, o porcentaje sobre el bruto si no hay monto).</summary>
    public decimal DescuentoEfectivo =>
        DescuentoMonto > 0 || DescuentoPorcentaje is null or <= 0
            ? CfeFormat.Redondear(DescuentoMonto)
            : CfeFormat.Redondear(Bruto * CfeFormat.Redondear(DescuentoPorcentaje.Value, 3) / 100m);

    /// <summary>
    /// Monto total neto de la línea (Cantidad × PrecioUnitario − Descuento + Recargo), calculado con la
    /// precisión con que se escriben en el XML (cantidad 3 decimales, precio 6, montos 2), para que
    /// <c>MontoItem</c> coincida con lo que recalcula la DGI.
    /// </summary>
    public decimal MontoTotal => CfeFormat.Redondear(Bruto - DescuentoEfectivo + CfeFormat.Redondear(RecargoMonto));

    private decimal Bruto => CfeFormat.Redondear(Cantidad, 3) * CfeFormat.Redondear(PrecioUnitario, 6);
}
