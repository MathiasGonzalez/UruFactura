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

    /// <summary>
    /// Indica si el emisor tiene la constancia de estar al día con el IVA (por defecto, sí). Si no la
    /// tiene, los CFE lo informan (<c>IVAalDia</c>) y la representación impresa no imprime la constancia.
    /// No corresponde a los contribuyentes Monotributo.
    /// </summary>
    public bool IvaAlDia { get; init; } = true;

    /// <summary>
    /// Norma que otorga la exoneración, que se imprime junto a la leyenda "Contribuyente Exonerado" en los
    /// CFE numerados con un CAE exonerado (por ejemplo, "Ley 16.906"). Hasta 100 caracteres, opcional.
    /// </summary>
    public string? NormaExoneracion { get; init; }

    internal List<ErrorValidacion> Validar()
    {
        var v = new Validacion();
        if (!UruFactura.Rut.EsValido(Rut))
            v.Error("Emisor.Rut", "Debe tener 12 dígitos y un dígito verificador válido.");
        v.Texto("Emisor.RazonSocial", RazonSocial, 150, obligatorio: true);
        v.Texto("Emisor.NombreComercial", NombreComercial, 30);
        v.Texto("Emisor.Giro", Giro, 100);
        v.Texto("Emisor.DomicilioFiscal", DomicilioFiscal, 70, obligatorio: true);
        v.Texto("Emisor.Ciudad", Ciudad, 30, obligatorio: true);
        v.Texto("Emisor.Departamento", Departamento, 30, obligatorio: true);
        if (CodigoSucursal is < 1 or > 9999)
            v.Error("Emisor.CodigoSucursal", "Debe estar entre 1 y 9999.");
        v.Texto("Emisor.Sucursal", Sucursal, 20);
        if (Telefonos.Count > 2)
            v.Error("Emisor.Telefonos", "Admite hasta 2 teléfonos.");
        for (var i = 0; i < Telefonos.Count; i++)
            v.Texto($"Emisor.Telefonos[{i}]", Telefonos[i], 20, obligatorio: true);
        v.Texto("Emisor.Correo", Correo, 80);
        v.Texto("Emisor.InfoAdicional", InfoAdicional, 150);
        v.Texto("Emisor.NormaExoneracion", NormaExoneracion, 100);
        return v.Errores;
    }
}
