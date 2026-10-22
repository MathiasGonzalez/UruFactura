using UruFactura.Enums;

namespace UruFactura.EntityFrameworkCore;

/// <summary>CAE persistido (tabla <c>UruFacturaCaes</c>).</summary>
public class CaeEntity
{
    /// <summary>Clave ordenada por tiempo (UUID v7; secuencial en SQL Server), generada al agregar la entidad.</summary>
    public Guid Id { get; set; }

    /// <summary>Número de serie del CAE (único).</summary>
    public string NroSerie { get; set; } = string.Empty;

    /// <summary>Número de autorización (<c>CAE_ID</c>).</summary>
    public string NumeroAutorizacion { get; set; } = string.Empty;

    /// <summary>Serie de los CFE autorizados.</summary>
    public string Serie { get; set; } = string.Empty;

    public TipoCfe TipoCfe { get; set; }
    public long RangoDesde { get; set; }
    public long RangoHasta { get; set; }
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>
    /// Último número reservado. Solo se modifica con <c>UPDATE</c> condicionales (compare-and-swap),
    /// nunca hacia atrás.
    /// </summary>
    public long UltimoNroUsado { get; set; }

    /// <summary>Convierte la entidad al modelo del SDK.</summary>
    public Models.Cae ToCae() => new()
    {
        NroSerie = NroSerie,
        NumeroAutorizacion = NumeroAutorizacion,
        Serie = Serie,
        TipoCfe = TipoCfe,
        RangoDesde = RangoDesde,
        RangoHasta = RangoHasta,
        FechaVencimiento = FechaVencimiento,
        UltimoNroUsado = UltimoNroUsado,
    };

    internal static CaeEntity Desde(Models.Cae cae) => new()
    {
        NroSerie = cae.NroSerie,
        NumeroAutorizacion = cae.NumeroAutorizacion,
        Serie = cae.Serie,
        TipoCfe = cae.TipoCfe,
        RangoDesde = cae.RangoDesde,
        RangoHasta = cae.RangoHasta,
        FechaVencimiento = cae.FechaVencimiento,
        UltimoNroUsado = cae.UltimoNroUsado,
    };
}
