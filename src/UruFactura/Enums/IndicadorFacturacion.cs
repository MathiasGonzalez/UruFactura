namespace UruFactura;

/// <summary>
/// Indicador de facturación de una línea o de un descuento/recargo global (<c>IndFact</c>): si el
/// concepto es exento, a qué tasa está gravado o si no es facturable. El valor es el código de la DGI.
/// </summary>
public enum IndicadorFacturacion
{
    /// <summary>Exento de IVA (1). Va al total no gravado (<c>MntNoGrv</c>).</summary>
    Exento = 1,

    /// <summary>Gravado a la tasa mínima de IVA (2).</summary>
    TasaMinima = 2,

    /// <summary>Gravado a la tasa básica de IVA (3).</summary>
    TasaBasica = 3,

    /// <summary>
    /// Gravado a otra tasa o IVA sobre fictos (4), por ejemplo la venta de autos usados. El IVA no se
    /// calcula: se informa en <see cref="Cfe.IvaOtraTasa"/>.
    /// </summary>
    OtraTasa = 4,

    /// <summary>
    /// Entrega gratuita (5), por ejemplo la decimotercera unidad de una docena de trece. El ítem va con precio cero.
    /// En el e-Remito de exportación marca el ítem con valor unitario cero.
    /// </summary>
    EntregaGratuita = 5,

    /// <summary>Producto o servicio no facturable (6). Va al monto no facturable (<c>MontoNF</c>) y suma al total a pagar.</summary>
    NoFacturable = 6,

    /// <summary>Producto o servicio no facturable negativo (7). Resta del monto no facturable y del total a pagar.</summary>
    NoFacturableNegativo = 7,

    /// <summary>
    /// Ítem a rebajar de un e-Remito ya emitido (8), solo en e-Remitos y e-Remitos de exportación: la referencia
    /// indica el remito que se ajusta. En el e-Remito de exportación su monto resta del total.
    /// </summary>
    RebajaRemito = 8,

    /// <summary>
    /// Ítem a anular de un e-Resguardo ya emitido (9), solo en e-Resguardos: la referencia indica el resguardo que
    /// se anula y sus retenciones restan del total retenido.
    /// </summary>
    AnulacionResguardo = 9,

    /// <summary>Exportación y asimiladas (10). Es el indicador de toda línea de los tipos de exportación.</summary>
    ExportacionYAsimiladas = 10,

    /// <summary>Impuesto percibido (11).</summary>
    ImpuestoPercibido = 11,

    /// <summary>IVA en suspenso (12).</summary>
    IvaEnSuspenso = 12,

    /// <summary>
    /// e-Boleta de entrada: ítem vendido por un no contribuyente (13). El vendedor no se identifica con RUT. Va al
    /// total no gravado.
    /// </summary>
    VendedorNoContribuyente = 13,

    /// <summary>
    /// e-Boleta de entrada: ítem vendido por un contribuyente IVA mínimo, Monotributo o Monotributo Social MIDES
    /// (14). El vendedor se identifica con su RUT. Va al total no gravado.
    /// </summary>
    VendedorIvaMinimo = 14,

    /// <summary>e-Boleta de entrada: ítem vendido por un contribuyente IMEBA (15). El vendedor se identifica con su RUT. Va al total no gravado.</summary>
    VendedorImeba = 15,

    /// <summary>
    /// Ítem vendido por un contribuyente IVA mínimo, Monotributo o Monotributo Social MIDES (16), o en una
    /// venta por cuenta ajena de un mandante IVA mínimo. Va al total no gravado, sin IVA.
    /// </summary>
    IvaMinimoMonotributo = 16,

    /// <summary>
    /// e-Boleta de entrada: compra de moneda extranjera para su reventa con contrapartida en moneda local (17).
    /// Exige <see cref="Cfe.CompraMonedaExtranjera"/>. Va al total no gravado.
    /// </summary>
    CompraMonedaExtranjera = 17,
}
