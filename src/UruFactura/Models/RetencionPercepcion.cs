namespace UruFactura;

/// <summary>
/// Retención o percepción de una línea del e-Resguardo. Los códigos son los de la tabla C del
/// formato CFE de la DGI.
/// </summary>
public sealed record RetencionPercepcion
{
    /// <summary>Código de retención/percepción (hasta 8 caracteres).</summary>
    public string Codigo { get; init; } = string.Empty;

    /// <summary>Tasa en % (opcional, 0-100).</summary>
    public decimal? Tasa { get; init; }

    /// <summary>Monto sujeto a retención.</summary>
    public decimal MontoSujeto { get; init; }

    /// <summary>Valor retenido o percibido.</summary>
    public decimal Valor { get; init; }

    /// <summary>Información adicional (hasta 150 caracteres, opcional).</summary>
    public string? InfoAdicional { get; init; }
}
