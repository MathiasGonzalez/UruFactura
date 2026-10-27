namespace UruFactura;

/// <summary>
/// Datos del mandante (zona <c>Compl_Fiscal</c>): en una venta por cuenta ajena, la persona o empresa en cuyo
/// nombre se emite el comprobante; en la e-Boleta de entrada, por cuenta de quién se compra la moneda extranjera.
/// </summary>
public sealed record Mandante
{
    /// <summary>Tipo de documento del mandante.</summary>
    public TipoDocumentoReceptor TipoDocumento { get; init; } = TipoDocumentoReceptor.Rut;

    /// <summary>País del documento (ISO alfa-2, por defecto "UY").</summary>
    public string CodigoPais { get; init; } = "UY";

    /// <summary>
    /// Número de documento del mandante (hasta 20 caracteres), con las mismas reglas que el del receptor (dígito
    /// verificador del RUT y la C.I., país del documento). No puede ser el RUT del emisor.
    /// </summary>
    public string Documento { get; init; } = string.Empty;

    /// <summary>Nombre o razón social del mandante (hasta 150 caracteres).</summary>
    public string Nombre { get; init; } = string.Empty;

    /// <summary>
    /// Indica si el mandante tiene la constancia de estar al día con el IVA (por defecto, sí). Si no la
    /// tiene, el CFE lo informa (<c>IVAalDia</c>).
    /// </summary>
    public bool IvaAlDia { get; init; } = true;
}
