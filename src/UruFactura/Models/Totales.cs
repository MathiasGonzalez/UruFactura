namespace UruFactura;

/// <summary>Totales de un CFE, en la moneda del comprobante (los calcula <see cref="Cfe.CalcularTotales"/>).</summary>
public sealed record Totales
{
    /// <summary>Monto neto exento de IVA (<c>MntNoGrv</c>).</summary>
    public decimal MontoNetoExento { get; init; }

    /// <summary>Monto neto con IVA en suspenso (<c>MntIVaenSusp</c>).</summary>
    public decimal MontoNetoSuspendido { get; init; }

    /// <summary>Monto neto gravado a la tasa mínima (10%).</summary>
    public decimal MontoNetoMinimo { get; init; }

    /// <summary>Monto neto gravado a la tasa básica (22%).</summary>
    public decimal MontoNetoBasico { get; init; }

    /// <summary>IVA a la tasa mínima.</summary>
    public decimal IvaMinimo { get; init; }

    /// <summary>IVA a la tasa básica.</summary>
    public decimal IvaBasico { get; init; }

    /// <summary>Monto de exportación y asimiladas (solo tipos de exportación).</summary>
    public decimal MontoExportacion { get; init; }

    /// <summary>Monto total del comprobante (netos + IVA). En el e-Resguardo es cero.</summary>
    public decimal MontoTotal { get; init; }

    /// <summary>Total retenido/percibido (e-Resguardo).</summary>
    public decimal MontoRetenido { get; init; }

    /// <summary>Suma de los montos netos (base del tope de 5.000 UI de los e-Tickets).</summary>
    public decimal MontoNeto => MontoNetoExento + MontoNetoSuspendido + MontoNetoMinimo + MontoNetoBasico;
}
