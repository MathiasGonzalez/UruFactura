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

    /// <summary>
    /// Código de la posición arancelaria del producto (<c>NCM</c>, hasta 20 caracteres, opcional). Solo en la
    /// e-Factura y el e-Remito de exportación; se imprime en la representación impresa.
    /// </summary>
    public string? Ncm { get; init; }

    /// <summary>
    /// Cantidad (se informa con 3 decimales). Distinta de cero: una cantidad negativa elimina un ítem del propio
    /// comprobante (por ejemplo, un artículo que se anula en el mismo ticket) y va sin descuentos ni recargos.
    /// </summary>
    public decimal Cantidad { get; init; }

    /// <summary>Unidad de medida (hasta 4 caracteres, p. ej. "UN", "KG"); si se omite se informa "N/A".</summary>
    public string? UnidadMedida { get; init; }

    /// <summary>
    /// Precio unitario (se informa con 6 decimales), sin IVA salvo que <see cref="Cfe.MontosBrutos"/> indique lo
    /// contrario. Mayor a cero, salvo en las entregas gratuitas (cero) y en los ítems a rebajar de un remito (cero o
    /// más). Los e-Remitos no llevan precio.
    /// </summary>
    public decimal PrecioUnitario { get; init; }

    /// <summary>
    /// Indicador de facturación (<c>IndFact</c>): exento, tasa de IVA o concepto no facturable. En los tipos
    /// de exportación las líneas son <see cref="IndicadorFacturacion.ExportacionYAsimiladas"/> salvo que se
    /// indique <see cref="IndicadorFacturacion.EntregaGratuita"/>, <see cref="IndicadorFacturacion.NoFacturable"/>
    /// o <see cref="IndicadorFacturacion.NoFacturableNegativo"/>. Los e-Remitos y e-Resguardos solo lo informan en
    /// los ítems a rebajar (<see cref="IndicadorFacturacion.RebajaRemito"/>) o anular
    /// (<see cref="IndicadorFacturacion.AnulacionResguardo"/>), y el e-Remito de exportación también en los de valor
    /// cero (<see cref="IndicadorFacturacion.EntregaGratuita"/>). La e-Boleta de entrada usa los suyos (12 a 17).
    /// </summary>
    public IndicadorFacturacion IndFact { get; init; } = IndicadorFacturacion.TasaBasica;

    /// <summary>
    /// Indicador de agente o responsable (<c>IndAgenteResp</c>): obligatorio, y solo admitido, en las líneas de
    /// e-Tickets, e-Facturas y e-Boletas de entrada que documentan <see cref="Retenciones"/>.
    /// </summary>
    public AgenteResponsable? AgenteResponsable { get; init; }

    /// <summary>Descuento de la línea en monto (opcional).</summary>
    public decimal DescuentoMonto { get; init; }

    /// <summary>
    /// Descuento en porcentaje (0-100, opcional). Si <see cref="DescuentoMonto"/> es cero, el descuento
    /// es ese porcentaje de Cantidad × PrecioUnitario; si se informan ambos, se aplica el monto.
    /// </summary>
    public decimal? DescuentoPorcentaje { get; init; }

    /// <summary>Distribución informativa del descuento de la línea (<c>SubDescuento</c>, hasta 5).</summary>
    public IReadOnlyList<DistribucionAjuste> Subdescuentos { get; init; } = [];

    /// <summary>Recargo de la línea en monto (opcional).</summary>
    public decimal RecargoMonto { get; init; }

    /// <summary>
    /// Recargo en porcentaje (0-100, opcional). Si <see cref="RecargoMonto"/> es cero, el recargo es ese
    /// porcentaje de Cantidad × PrecioUnitario; si se informan ambos, se aplica el monto.
    /// </summary>
    public decimal? RecargoPorcentaje { get; init; }

    /// <summary>Distribución informativa del recargo de la línea (<c>SubRecargo</c>, hasta 5).</summary>
    public IReadOnlyList<DistribucionAjuste> Subrecargos { get; init; } = [];

    /// <summary>Códigos del ítem (<c>CodItem</c>, hasta 5), por ejemplo GTIN o código interno.</summary>
    public IReadOnlyList<CodigoItem> Codigos { get; init; } = [];

    /// <summary>
    /// Retenciones, percepciones o créditos fiscales de la línea (hasta 5): de 1 a 5 en el e-Resguardo, opcionales
    /// en e-Tickets, e-Facturas y e-Boletas de entrada (con <see cref="AgenteResponsable"/>). No van en remitos ni
    /// en exportación.
    /// </summary>
    public IReadOnlyList<RetencionPercepcion> Retenciones { get; init; } = [];

    /// <summary>Descuento efectivo de la línea (monto, o porcentaje sobre el bruto si no hay monto).</summary>
    public decimal DescuentoEfectivo => Efectivo(DescuentoMonto, DescuentoPorcentaje);

    /// <summary>Recargo efectivo de la línea (monto, o porcentaje sobre el bruto si no hay monto).</summary>
    public decimal RecargoEfectivo => Efectivo(RecargoMonto, RecargoPorcentaje);

    /// <summary>
    /// Monto de la línea (Cantidad × PrecioUnitario − Descuento + Recargo), calculado con la precisión
    /// con que se escribe en el XML (cantidad 3 decimales, precio 6, montos 2) para que <c>MontoItem</c>
    /// coincida con lo que recalcula la DGI.
    /// </summary>
    public decimal MontoTotal => CfeFormat.Redondear(Bruto - DescuentoEfectivo + RecargoEfectivo);

    /// <summary>Cantidad × PrecioUnitario con la precisión del XML.</summary>
    internal decimal Bruto => CfeFormat.Redondear(Cantidad, 3) * CfeFormat.Redondear(PrecioUnitario, 6);

    private decimal Efectivo(decimal monto, decimal? porcentaje) =>
        monto > 0 || porcentaje is null or <= 0
            ? CfeFormat.Redondear(monto)
            : CfeFormat.Redondear(Bruto * CfeFormat.Redondear(porcentaje.Value, 3) / 100m);
}
