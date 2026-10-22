namespace UruFactura;

/// <summary>Referencia a otro CFE (obligatoria en las notas de crédito y débito).</summary>
public sealed record RefCfe
{
    /// <summary>Tipo del CFE referenciado.</summary>
    public TipoCfe TipoCfe { get; init; }

    /// <summary>Serie del CFE referenciado.</summary>
    public string Serie { get; init; } = string.Empty;

    /// <summary>Número del CFE referenciado.</summary>
    public long NroCfe { get; init; }

    /// <summary>Fecha de emisión del CFE referenciado.</summary>
    public DateOnly FechaCfe { get; init; }

    /// <summary>
    /// Monto total del CFE referenciado (F-C8). Obligatorio en notas de crédito/débito desde el
    /// formato CFE v25.01.
    /// </summary>
    public decimal? MontoCfeRef { get; init; }

    /// <summary>Moneda del CFE referenciado (F-C9). Obligatoria junto con <see cref="MontoCfeRef"/>.</summary>
    public Moneda? MonedaCfeRef { get; init; }

    /// <summary>Tipo de cambio del CFE referenciado (F-C10), si la moneda no es el peso uruguayo.</summary>
    public decimal? TipoCambioCfeRef { get; init; }

    /// <summary>Motivo de la referencia.</summary>
    public string? Razon { get; init; }
}
