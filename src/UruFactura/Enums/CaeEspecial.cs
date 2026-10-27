namespace UruFactura;

/// <summary>Tipo de CAE especial que entrega la DGI (<c>CAEEspecial</c>). El valor es el código de la DGI.</summary>
public enum CaeEspecial
{
    /// <summary>
    /// Exonerado (1): exige la causal (<see cref="Cae.CausalEspecial"/>) y no admite líneas gravadas
    /// (tasa mínima, básica u otra), con impuesto percibido ni con IVA en suspenso.
    /// </summary>
    Exonerado = 1,

    /// <summary>Contribuyente IVA mínimo (2): sus líneas usan <see cref="IndicadorFacturacion.IvaMinimoMonotributo"/>.</summary>
    IvaMinimo = 2,

    /// <summary>Monotributo (3): sus líneas usan <see cref="IndicadorFacturacion.IvaMinimoMonotributo"/>.</summary>
    Monotributo = 3,

    /// <summary>Monotributo Social MIDES (4): sus líneas usan <see cref="IndicadorFacturacion.IvaMinimoMonotributo"/>.</summary>
    MonotributoSocialMides = 4,
}
