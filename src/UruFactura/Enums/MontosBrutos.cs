namespace UruFactura;

/// <summary>
/// Indicador de montos brutos (<c>MntBruto</c>, A-C10): en qué se expresan los montos de las líneas. El valor es el
/// código de la DGI. El 3 (ventas de contribuyentes IVA mínimo o Monotributo) no se indica: lo informa la emisión
/// cuando el CAE es de IVA mínimo o Monotributo.
/// </summary>
public enum MontosBrutos
{
    /// <summary>
    /// Las líneas de e-Tickets y e-Facturas se expresan con IVA incluido (1): los netos de las tasas mínima y básica
    /// se obtienen dividiendo por (1 + tasa).
    /// </summary>
    IvaIncluido = 1,

    /// <summary>
    /// Las líneas de la e-Boleta de entrada se expresan con IMEBA y adicionales incluidos (2): la retención resta del
    /// total a pagar. Obligatorio si la e-Boleta tiene líneas con IVA en suspenso.
    /// </summary>
    ImebaYAdicionalesIncluidos = 2,
}
