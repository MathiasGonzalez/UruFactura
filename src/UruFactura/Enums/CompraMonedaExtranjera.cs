namespace UruFactura;

/// <summary>
/// Indicador de compra de moneda extranjera para su reventa (<c>IndCompraMEReventa</c>, A-C21), solo en la
/// e-Boleta de entrada: por ejemplo, un banco o una casa de cambio que compra divisas pagando en pesos. Sus líneas
/// van con <see cref="IndicadorFacturacion.CompraMonedaExtranjera"/>. El valor es el código de la DGI.
/// </summary>
public enum CompraMonedaExtranjera
{
    /// <summary>Compra de moneda extranjera para su reventa (1). El comprobante es en pesos uruguayos.</summary>
    Reventa = 1,

    /// <summary>
    /// Compra de moneda extranjera para su reventa por cuenta ajena (2): exige el <see cref="Cfe.Mandante"/>,
    /// identificado con NIE, RUT o NIFE, y no admite el secreto profesional.
    /// </summary>
    ReventaPorCuentaAjena = 2,
}
