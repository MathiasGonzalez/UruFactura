namespace UruFactura;

/// <summary>
/// Propietario de la mercadería que traslada un e-Remito o un e-Remito de exportación cuando no es del emisor
/// (<c>IndPropiedad</c> 1, A-C6.1 a A-C6.6). La representación impresa lleva la leyenda "Bienes de Terceros".
/// </summary>
public sealed record Propietario
{
    /// <summary>Tipo de documento del propietario.</summary>
    public TipoDocumentoReceptor TipoDocumento { get; init; } = TipoDocumentoReceptor.Rut;

    /// <summary>País del documento (ISO 3166-1 alfa-2, por defecto "UY"; "99" para un país que no está en la tabla).</summary>
    public string CodigoPais { get; init; } = "UY";

    /// <summary>Número de documento (hasta 12 caracteres los uruguayos y 20 los demás).</summary>
    public string Documento { get; init; } = string.Empty;

    /// <summary>Nombre o razón social (hasta 150 caracteres).</summary>
    public string Nombre { get; init; } = string.Empty;
}
