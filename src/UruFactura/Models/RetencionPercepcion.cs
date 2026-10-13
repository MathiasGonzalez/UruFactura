namespace UruFactura.Models;

/// <summary>
/// Retención o percepción de un ítem (principalmente del e-Resguardo). Los códigos son los de la
/// tabla C del formato CFE de la DGI.
/// </summary>
public class RetencionPercepcion
{
    /// <summary>Código de retención/percepción (hasta 8 caracteres).</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Tasa en % (opcional, 0-100).</summary>
    public decimal? Tasa { get; set; }

    /// <summary>Monto sujeto a retención.</summary>
    public decimal MontoSujeto { get; set; }

    /// <summary>Valor retenido o percibido.</summary>
    public decimal Valor { get; set; }

    /// <summary>Información adicional (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; set; }
}
