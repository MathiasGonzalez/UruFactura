namespace UruFactura;

/// <summary>
/// Datos del mandante en una venta por cuenta ajena (zona <c>Compl_Fiscal</c>): la persona o empresa
/// en cuyo nombre se emite el comprobante.
/// </summary>
public sealed record Mandante
{
    /// <summary>Tipo de documento del mandante.</summary>
    public TipoDocumentoReceptor TipoDocumento { get; init; } = TipoDocumentoReceptor.Rut;

    /// <summary>País del documento (ISO alfa-2, por defecto "UY").</summary>
    public string CodigoPais { get; init; } = "UY";

    /// <summary>Número de documento del mandante (hasta 20 caracteres).</summary>
    public string Documento { get; init; } = string.Empty;

    /// <summary>Nombre o razón social del mandante (hasta 150 caracteres).</summary>
    public string Nombre { get; init; } = string.Empty;
}
