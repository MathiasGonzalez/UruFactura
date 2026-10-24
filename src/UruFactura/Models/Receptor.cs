namespace UruFactura;

/// <summary>Receptor del CFE.</summary>
public sealed record Receptor
{
    /// <summary>RUT o documento del receptor (opcional en el e-Ticket al consumidor final).</summary>
    public string? Documento { get; init; }

    /// <summary>Tipo de documento del receptor.</summary>
    public TipoDocumentoReceptor TipoDocumento { get; init; } = TipoDocumentoReceptor.Rut;

    /// <summary>
    /// País del documento (ISO 3166 alfa-2). Por defecto "UY"; si es otro, el documento se informa
    /// como documento extranjero.
    /// </summary>
    public string CodigoPais { get; init; } = "UY";

    /// <summary>Razón social o nombre (hasta 150 caracteres).</summary>
    public string? RazonSocial { get; init; }

    /// <summary>Dirección (hasta 70 caracteres).</summary>
    public string? Direccion { get; init; }

    /// <summary>Ciudad (hasta 30 caracteres).</summary>
    public string? Ciudad { get; init; }

    /// <summary>Departamento (hasta 30 caracteres).</summary>
    public string? Departamento { get; init; }

    /// <summary>Nombre del país (hasta 30 caracteres; habitual en exportación).</summary>
    public string? Pais { get; init; }

    /// <summary>Código postal (numérico, hasta 5 dígitos, opcional).</summary>
    public string? CodigoPostal { get; init; }

    /// <summary>Información adicional (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; init; }

    /// <summary>Identificador de compra del receptor (<c>CompraID</c>, hasta 50 caracteres, opcional).</summary>
    public string? CompraId { get; init; }

    /// <summary>Lugar de destino de la entrega (hasta 100 caracteres, opcional).</summary>
    public string? LugarDestinoEntrega { get; init; }
}
