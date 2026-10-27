namespace UruFactura;

/// <summary>
/// Referencia a otro CFE (obligatoria en las notas de crédito y débito). Identifica el comprobante por
/// tipo, serie y número o, si no se puede, es una referencia global (<see cref="Global"/>) con el motivo
/// en <see cref="Razon"/>.
/// </summary>
public sealed record RefCfe
{
    /// <summary>
    /// Referencia global (<c>IndGlobal</c>): cuando no se puede identificar el CFE referenciado, por
    /// ejemplo un documento no codificado o una liquidación. Exige <see cref="Razon"/> y no admite los
    /// datos del comprobante (tipo, serie, número, fecha, monto, moneda ni tipo de cambio).
    /// </summary>
    public bool Global { get; init; }

    /// <summary>Tipo del CFE referenciado (obligatorio salvo en la referencia global).</summary>
    public TipoCfe? TipoCfe { get; init; }

    /// <summary>Serie del CFE referenciado (obligatoria salvo en la referencia global).</summary>
    public string? Serie { get; init; }

    /// <summary>Número del CFE referenciado (obligatorio salvo en la referencia global).</summary>
    public long? NroCfe { get; init; }

    /// <summary>
    /// Fecha del CFE referenciado (<c>FechaCFEref</c>, opcional). Si se informa, la DGI controla que sea
    /// la del comprobante referenciado.
    /// </summary>
    public DateOnly? FechaCfe { get; init; }

    /// <summary>
    /// Monto (total o parcial) del CFE referenciado que se ajusta (<c>MntCFEref</c>), en su moneda.
    /// Obligatorio en las notas de crédito y débito desde el formato CFE v25; no corresponde en el e-Remito.
    /// </summary>
    public decimal? MontoCfeRef { get; init; }

    /// <summary>Moneda del CFE referenciado (<c>TpoMonedaRef</c>). Obligatoria junto con <see cref="MontoCfeRef"/>.</summary>
    public Moneda? MonedaCfeRef { get; init; }

    /// <summary>
    /// Tipo de cambio del CFE referenciado (<c>TpoCambioRef</c>): obligatorio si <see cref="MonedaCfeRef"/>
    /// no es el peso uruguayo, y debe ser el del comprobante referenciado.
    /// </summary>
    public decimal? TipoCambioCfeRef { get; init; }

    /// <summary>Motivo de la referencia (hasta 90 caracteres; obligatorio en la referencia global).</summary>
    public string? Razon { get; init; }
}
