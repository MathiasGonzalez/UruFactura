using UruFactura.Enums;

namespace UruFactura.Models;

/// <summary>
/// Constancia de Autorización de Emisión (CAE) emitida por la DGI.
/// </summary>
public class Cae
{
    private readonly Lock _lock = new();
    private long _ultimoNroUsado;

    /// <summary>Número de serie del CAE.</summary>
    public string NroSerie { get; set; } = string.Empty;

    /// <summary>
    /// Número de autorización del CAE (<c>CAE_ID</c>, hasta 11 dígitos). Se incluye en el
    /// bloque <c>CAEData</c> de cada CFE.
    /// </summary>
    public string NumeroAutorizacion { get; set; } = string.Empty;

    /// <summary>
    /// Serie de los CFE autorizados por este CAE (1 o 2 caracteres: "A", "AB" o "1A").
    /// </summary>
    public string Serie { get; set; } = string.Empty;

    /// <summary>Tipo de CFE al que aplica este CAE.</summary>
    public TipoCfe TipoCfe { get; set; }

    /// <summary>Número inicial del rango autorizado.</summary>
    public long RangoDesde { get; set; }

    /// <summary>Número final del rango autorizado.</summary>
    public long RangoHasta { get; set; }

    /// <summary>Fecha de vencimiento del CAE.</summary>
    public DateOnly FechaVencimiento { get; set; }

    /// <summary>
    /// Último número utilizado dentro del rango.
    /// Use <c>init</c> en inicializadores de objeto; el valor se incrementa
    /// internamente por <see cref="ObtenerProximoNumero"/>.
    /// </summary>
    public long UltimoNroUsado
    {
        get => _ultimoNroUsado;
        init => _ultimoNroUsado = value;
    }

    /// <summary>
    /// Indica si el CAE está vigente (no vencido).
    /// </summary>
    public bool EsVigente => DateOnly.FromDateTime(DateTime.Today) <= FechaVencimiento;

    /// <summary>
    /// Indica si el CAE tiene números disponibles.
    /// </summary>
    public bool TieneNumerosDisponibles => _ultimoNroUsado < RangoHasta;

    /// <summary>
    /// Porcentaje de uso del rango del CAE (0-100).
    /// </summary>
    public decimal PorcentajeUso
    {
        get
        {
            long total = RangoHasta - RangoDesde + 1;
            if (total <= 0) return 100m;
            long usados = _ultimoNroUsado - RangoDesde + 1;
            if (usados <= 0) return 0m;
            return Math.Round((decimal)usados / total * 100, 2);
        }
    }

    /// <summary>
    /// Obtiene el próximo número disponible e incrementa el contador.
    /// Este método es thread-safe.
    /// </summary>
    /// <exception cref="Exceptions.CaeException">Si no hay números disponibles o el CAE está vencido.</exception>
    public long ObtenerProximoNumero()
    {
        lock (_lock)
        {
            if (!EsVigente)
                throw new Exceptions.CaeException(
                    $"El CAE {NroSerie} venció el {FechaVencimiento:dd/MM/yyyy}.");

            if (!TieneNumerosDisponibles)
                throw new Exceptions.CaeException(
                    $"El CAE {NroSerie} no tiene números disponibles (rango hasta {RangoHasta}).");

            _ultimoNroUsado = _ultimoNroUsado == 0 ? RangoDesde : _ultimoNroUsado + 1;
            return _ultimoNroUsado;
        }
    }

    /// <summary>
    /// Devuelve una advertencia si el CAE está por vencer o por agotar su rango.
    /// </summary>
    public string? ObtenerAdvertencia(int diasAlertaVencimiento = 7, decimal porcentajeAlertaUso = 80m)
    {
        if (!EsVigente)
            return $"⚠️ CAE {NroSerie} VENCIDO el {FechaVencimiento:dd/MM/yyyy}.";

        var diasRestantes = FechaVencimiento.DayNumber - DateOnly.FromDateTime(DateTime.Today).DayNumber;
        if (diasRestantes <= diasAlertaVencimiento)
            return $"⚠️ CAE {NroSerie} vence en {diasRestantes} día(s) ({FechaVencimiento:dd/MM/yyyy}).";

        if (PorcentajeUso >= porcentajeAlertaUso)
            return $"⚠️ CAE {NroSerie} ha utilizado el {PorcentajeUso}% de su rango autorizado.";

        return null;
    }
}
