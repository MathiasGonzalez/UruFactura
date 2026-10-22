namespace UruFactura;

/// <summary>
/// Datos del emisor que se informan en cada CFE (zona <c>Emisor</c>) y en su representación impresa.
/// Se configuran una vez en <see cref="UruFacturaConfig.Emisor"/>; un <see cref="Cfe"/> puede
/// reemplazarlos (por ejemplo, con otra sucursal: <c>config.Emisor with { CodigoSucursal = 3 }</c>).
/// </summary>
public sealed record Emisor
{
    /// <summary>RUT del emisor (12 dígitos, sin puntos ni guión).</summary>
    public string Rut { get; init; } = string.Empty;

    /// <summary>Razón social (hasta 150 caracteres).</summary>
    public string RazonSocial { get; init; } = string.Empty;

    /// <summary>Nombre comercial (hasta 30 caracteres, opcional).</summary>
    public string? NombreComercial { get; init; }

    /// <summary>Giro o actividad económica (hasta 100 caracteres, opcional).</summary>
    public string? Giro { get; init; }

    /// <summary>Domicilio fiscal (hasta 70 caracteres).</summary>
    public string DomicilioFiscal { get; init; } = string.Empty;

    /// <summary>Ciudad (hasta 30 caracteres).</summary>
    public string Ciudad { get; init; } = string.Empty;

    /// <summary>Departamento (hasta 30 caracteres).</summary>
    public string Departamento { get; init; } = string.Empty;

    /// <summary>Código de sucursal asignado por la DGI (<c>CdgDGISucur</c>, 1 a 9999). Casa central suele ser 1.</summary>
    public int CodigoSucursal { get; init; } = 1;

    /// <summary>Nombre de la sucursal (<c>EmiSucursal</c>, hasta 20 caracteres, opcional).</summary>
    public string? Sucursal { get; init; }

    /// <summary>Teléfonos (hasta 2, de hasta 20 caracteres).</summary>
    public IReadOnlyList<string> Telefonos { get; init; } = [];

    /// <summary>Correo electrónico (hasta 80 caracteres, opcional).</summary>
    public string? Correo { get; init; }

    /// <summary>Información adicional (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; init; }

    /// <summary>
    /// Leyenda "Fecha emisor" de la representación impresa: fecha de incorporación al régimen
    /// (dd/MM/yyyy) o, si se incorporó por resolución, "Res. N/año". Opcional.
    /// </summary>
    public string? FechaIncorporacion { get; init; }

    /// <summary>
    /// Sitio de verificación que se imprime en los e-Tickets ("Puede verificar comprobante en …").
    /// Si se omite, se imprime el de la DGI.
    /// </summary>
    public string? UrlVerificacion { get; init; }

    internal IEnumerable<string> Validar()
    {
        if (!UruFactura.Rut.EsValido(Rut))
            yield return "Emisor.Rut debe tener 12 dígitos y un dígito verificador válido.";
        if (string.IsNullOrWhiteSpace(RazonSocial) || RazonSocial.Length > 150)
            yield return "Emisor.RazonSocial es obligatoria (máx. 150 caracteres).";
        if (NombreComercial is { Length: > 30 })
            yield return "Emisor.NombreComercial admite hasta 30 caracteres.";
        if (Giro is { Length: > 100 })
            yield return "Emisor.Giro admite hasta 100 caracteres.";
        if (string.IsNullOrWhiteSpace(DomicilioFiscal) || DomicilioFiscal.Length > 70)
            yield return "Emisor.DomicilioFiscal es obligatorio (máx. 70 caracteres).";
        if (string.IsNullOrWhiteSpace(Ciudad) || Ciudad.Length > 30)
            yield return "Emisor.Ciudad es obligatoria (máx. 30 caracteres).";
        if (string.IsNullOrWhiteSpace(Departamento) || Departamento.Length > 30)
            yield return "Emisor.Departamento es obligatorio (máx. 30 caracteres).";
        if (CodigoSucursal is < 1 or > 9999)
            yield return "Emisor.CodigoSucursal debe estar entre 1 y 9999.";
        if (Sucursal is { Length: > 20 })
            yield return "Emisor.Sucursal admite hasta 20 caracteres.";
        if (Telefonos.Count > 2 || Telefonos.Any(t => string.IsNullOrWhiteSpace(t) || t.Length > 20))
            yield return "Emisor.Telefonos admite hasta 2 teléfonos de hasta 20 caracteres.";
        if (Correo is { Length: > 80 })
            yield return "Emisor.Correo admite hasta 80 caracteres.";
        if (InfoAdicional is { Length: > 150 })
            yield return "Emisor.InfoAdicional admite hasta 150 caracteres.";
    }
}
