namespace UruFactura.EntityFrameworkCore;

/// <summary>CAE persistido (tabla <c>UruFacturaCaes</c>). Tipo, serie y comienzo de rango son únicos.</summary>
public class CaeEntity
{
    /// <summary>Clave ordenada por tiempo (UUID v7; secuencial en SQL Server), generada al agregar la entidad.</summary>
    public Guid Id { get; set; }

    /// <summary>Número de autorización (<c>CAE_ID</c>).</summary>
    public string NumeroAutorizacion { get; set; } = string.Empty;

    public TipoCfe TipoCfe { get; set; }
    public string Serie { get; set; } = string.Empty;
    public long RangoDesde { get; set; }
    public long RangoHasta { get; set; }
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>
    /// Último número usado. Es token de concurrencia: solo se modifica con <c>UPDATE</c> condicionales
    /// (junto con el registro del CFE que lo usa), nunca hacia atrás.
    /// </summary>
    public long UltimoNroUsado { get; set; }

    /// <summary>Convierte la entidad al modelo de UruFactura.</summary>
    public Cae ToCae() => new()
    {
        NumeroAutorizacion = NumeroAutorizacion,
        TipoCfe = TipoCfe,
        Serie = Serie,
        RangoDesde = RangoDesde,
        RangoHasta = RangoHasta,
        FechaVencimiento = FechaVencimiento,
        UltimoNroUsado = UltimoNroUsado,
    };

    internal static CaeEntity Desde(Cae cae) => new()
    {
        NumeroAutorizacion = cae.NumeroAutorizacion,
        TipoCfe = cae.TipoCfe,
        Serie = cae.Serie,
        RangoDesde = cae.RangoDesde,
        RangoHasta = cae.RangoHasta,
        FechaVencimiento = cae.FechaVencimiento,
        UltimoNroUsado = cae.UltimoNroUsado,
    };
}
