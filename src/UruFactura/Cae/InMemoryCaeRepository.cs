using UruFactura.Enums;

namespace UruFactura.Cae;

/// <summary>
/// Implementación en memoria de <see cref="ICaeRepository"/>.
/// Los datos se pierden al reiniciar la aplicación; use esta implementación
/// únicamente en tests o en escenarios donde la persistencia no sea necesaria.
/// <para>
/// Para producción, implemente <see cref="ICaeRepository"/> con una base de datos
/// u otro mecanismo de almacenamiento duradero.
/// </para>
/// Esta clase es thread-safe.
/// </summary>
public class InMemoryCaeRepository : ICaeRepository
{
    private readonly Dictionary<string, CaeSnapshot> _datos = new();
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public virtual ValueTask<IEnumerable<Models.Cae>> CargarTodosAsync()
    {
        lock (_lock)
        {
            var caes = _datos.Values.Select(s => s.ToCae()).ToList();
            return new ValueTask<IEnumerable<Models.Cae>>(caes);
        }
    }

    /// <inheritdoc/>
    public virtual ValueTask GuardarCaeAsync(Models.Cae cae)
    {
        ArgumentNullException.ThrowIfNull(cae);

        lock (_lock)
        {
            _datos[cae.NroSerie] = CaeSnapshot.FromCae(cae);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask ActualizarUltimoNroUsadoAsync(string nroSerie, long ultimoNroUsado)
    {
        ArgumentNullException.ThrowIfNull(nroSerie);

        lock (_lock)
        {
            if (!_datos.TryGetValue(nroSerie, out var snapshot))
                throw new KeyNotFoundException(
                    $"No se encontró un CAE con NroSerie '{nroSerie}'.");

            snapshot.UltimoNroUsado = ultimoNroUsado;
        }

        return ValueTask.CompletedTask;
    }

    // Clase privada con estado mutable para almacenar datos del CAE en memoria.
    // Necesaria porque Models.Cae.UltimoNroUsado solo tiene setter init.
    private sealed class CaeSnapshot
    {
        public string NroSerie { get; set; } = string.Empty;
        public string NumeroAutorizacion { get; set; } = string.Empty;
        public string Serie { get; set; } = string.Empty;
        public TipoCfe TipoCfe { get; set; }
        public long RangoDesde { get; set; }
        public long RangoHasta { get; set; }
        public DateOnly FechaVencimiento { get; set; }
        public long UltimoNroUsado { get; set; }

        public static CaeSnapshot FromCae(Models.Cae cae) => new()
        {
            NroSerie         = cae.NroSerie,
            NumeroAutorizacion = cae.NumeroAutorizacion,
            Serie            = cae.Serie,
            TipoCfe          = cae.TipoCfe,
            RangoDesde       = cae.RangoDesde,
            RangoHasta       = cae.RangoHasta,
            FechaVencimiento = cae.FechaVencimiento,
            UltimoNroUsado   = cae.UltimoNroUsado,
        };

        public Models.Cae ToCae() => new()
        {
            NroSerie         = NroSerie,
            NumeroAutorizacion = NumeroAutorizacion,
            Serie            = Serie,
            TipoCfe          = TipoCfe,
            RangoDesde       = RangoDesde,
            RangoHasta       = RangoHasta,
            FechaVencimiento = FechaVencimiento,
            UltimoNroUsado   = UltimoNroUsado,
        };
    }
}
