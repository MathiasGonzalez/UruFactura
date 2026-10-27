namespace UruFactura;

/// <summary>
/// Indicador de agente o responsable de una línea (<c>IndAgenteResp</c>, B-C6), obligatorio para los agentes de
/// retención o percepción en las líneas que documentan retenciones o percepciones (e-Ticket, e-Factura y
/// e-Boleta de entrada).
/// </summary>
public enum AgenteResponsable
{
    /// <summary>Responsable sobre el producto o servicio vendido (<c>R</c>).</summary>
    Producto,

    /// <summary>Responsable sobre un concepto asociado al producto o servicio vendido (<c>A</c>).</summary>
    ConceptoAsociado,
}
