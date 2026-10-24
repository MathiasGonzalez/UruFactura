namespace UruFactura;

/// <summary>Medio de pago informado en el comprobante (zona <c>MediosPago</c>).</summary>
public sealed record MedioPago
{
    /// <summary>Identificación detallada del medio de pago (hasta 100 caracteres).</summary>
    public string Glosa { get; init; } = string.Empty;

    /// <summary>Valor del pago.</summary>
    public decimal Valor { get; init; }

    /// <summary>Código asignado al medio de pago (hasta 3 dígitos, opcional).</summary>
    public int? Codigo { get; init; }

    /// <summary>Orden de impresión (1-99, opcional).</summary>
    public int? Orden { get; init; }
}
