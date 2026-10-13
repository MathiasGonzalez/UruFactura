using UruFactura.Enums;

namespace UruFactura.Models;

/// <summary>
/// Información del receptor del CFE.
/// </summary>
public class Receptor
{
    /// <summary>RUT o documento de identidad del receptor (opcional para e-Ticket al consumidor final).</summary>
    public string? Documento { get; set; }

    /// <summary>Tipo de documento del receptor.</summary>
    public TipoDocumentoReceptor TipoDocumento { get; set; } = TipoDocumentoReceptor.Rut;

    /// <summary>Razón social o nombre del receptor.</summary>
    public string? RazonSocial { get; set; }

    /// <summary>Dirección fiscal del receptor.</summary>
    public string? Direccion { get; set; }

    /// <summary>Ciudad del receptor.</summary>
    public string? Ciudad { get; set; }

    /// <summary>Departamento del receptor.</summary>
    public string? Departamento { get; set; }

    /// <summary>
    /// Código de país del documento (ISO 3166 alfa-2, p. ej. "UY"). Por defecto "UY".
    /// Si es distinto de "UY" el documento se informa como documento extranjero.
    /// </summary>
    public string CodigoPais { get; set; } = "UY";

    /// <summary>País del receptor (nombre, para exportación).</summary>
    public string? Pais { get; set; }

    /// <summary>Código postal del receptor (numérico, hasta 5 dígitos, opcional).</summary>
    public string? CodigoPostal { get; set; }

    /// <summary>Información adicional del receptor (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; set; }

    /// <summary>Identificador de compra del receptor (<c>CompraID</c>, hasta 50 caracteres, opcional).</summary>
    public string? CompraId { get; set; }

    /// <summary>Lugar de destino de la entrega (hasta 100 caracteres, opcional).</summary>
    public string? LugarDestinoEntrega { get; set; }

    /// <summary>Email del receptor (para envío electrónico).</summary>
    public string? Email { get; set; }
}
