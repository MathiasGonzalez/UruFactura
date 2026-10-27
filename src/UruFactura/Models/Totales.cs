namespace UruFactura;

/// <summary>Totales de un CFE, en la moneda del comprobante (los calcula <see cref="Cfe.CalcularTotales"/>).</summary>
public sealed record Totales
{
    /// <summary>Tasa mínima de IVA, en porcentaje (<c>IVATasaMin</c>).</summary>
    public const decimal TasaIvaMinima = 10m;

    /// <summary>Tasa básica de IVA, en porcentaje (<c>IVATasaBasica</c>).</summary>
    public const decimal TasaIvaBasica = 22m;

    /// <summary>
    /// Monto no gravado (<c>MntNoGrv</c>): líneas exentas y de contribuyentes IVA mínimo o Monotributo
    /// (<see cref="IndicadorFacturacion.Exento"/> e <see cref="IndicadorFacturacion.IvaMinimoMonotributo"/>). En la
    /// e-Boleta de entrada, las líneas con los indicadores 13, 14, 15 y 17.
    /// </summary>
    public decimal MontoNoGravado { get; init; }

    /// <summary>
    /// Monto de exportación y asimiladas (<c>MntExpoyAsim</c>). En el e-Remito de exportación, las líneas menos las
    /// que rebajan otro remito (puede ser negativo).
    /// </summary>
    public decimal MontoExportacion { get; init; }

    /// <summary>Monto de impuesto percibido (<c>MntImpuestoPerc</c>).</summary>
    public decimal MontoImpuestoPercibido { get; init; }

    /// <summary>Monto neto con IVA en suspenso (<c>MntIVaenSusp</c>).</summary>
    public decimal MontoNetoSuspendido { get; init; }

    /// <summary>Monto neto gravado a la tasa mínima (<see cref="TasaIvaMinima"/>).</summary>
    public decimal MontoNetoMinimo { get; init; }

    /// <summary>Monto neto gravado a la tasa básica (<see cref="TasaIvaBasica"/>).</summary>
    public decimal MontoNetoBasico { get; init; }

    /// <summary>Monto neto gravado a otra tasa o con IVA sobre fictos (<c>MntNetoIVAOtra</c>).</summary>
    public decimal MontoNetoOtraTasa { get; init; }

    /// <summary>IVA a la tasa mínima.</summary>
    public decimal IvaMinimo { get; init; }

    /// <summary>IVA a la tasa básica.</summary>
    public decimal IvaBasico { get; init; }

    /// <summary>IVA a otra tasa o sobre fictos (<c>MntIVAOtra</c>, el de <see cref="Cfe.IvaOtraTasa"/>).</summary>
    public decimal IvaOtraTasa { get; init; }

    /// <summary>Monto total (<c>MntTotal</c>): montos netos más IVA. En el e-Resguardo es cero.</summary>
    public decimal MontoTotal { get; init; }

    /// <summary>
    /// Total retenido/percibido (<c>MntTotRetenido</c>): las retenciones y percepciones que no son créditos
    /// fiscales. En el e-Resguardo, las de los ítems a anular restan (puede ser negativo).
    /// </summary>
    public decimal MontoRetenido { get; init; }

    /// <summary>
    /// Total de créditos fiscales (<c>MntTotCredFisc</c>): las retenciones con código del formulario 2181
    /// (<c>2181xxx</c>). No suma al total a pagar.
    /// </summary>
    public decimal MontoCreditoFiscal { get; init; }

    /// <summary>
    /// Monto no facturable (<c>MontoNF</c>): líneas <see cref="IndicadorFacturacion.NoFacturable"/> menos
    /// las <see cref="IndicadorFacturacion.NoFacturableNegativo"/>, con sus descuentos y recargos globales.
    /// </summary>
    public decimal MontoNoFacturable { get; init; }

    /// <summary>
    /// Monto total a pagar (<c>MntPagar</c>, el monto del QR): el total más lo retenido o percibido más el monto no
    /// facturable. En la e-Boleta de entrada con montos que incluyen IMEBA, la retención resta.
    /// </summary>
    public decimal MontoPagar { get; init; }

    /// <summary>
    /// Suma de los montos netos (<c>MntNoGrv</c> a <c>MntNetoIVAOtra</c>): la base del tope de 5.000 UI
    /// de los e-Tickets.
    /// </summary>
    public decimal MontoNeto =>
        MontoNoGravado + MontoExportacion + MontoImpuestoPercibido + MontoNetoSuspendido
        + MontoNetoMinimo + MontoNetoBasico + MontoNetoOtraTasa;
}
