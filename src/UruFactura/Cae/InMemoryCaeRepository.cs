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
    // Guarda y devuelve copias: el CaeManager incrementa el contador de las instancias que registra.
    private readonly Dictionary<string, Models.Cae> _datos = new();
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public virtual ValueTask<IEnumerable<Models.Cae>> CargarTodosAsync()
    {
        lock (_lock)
            return ValueTask.FromResult<IEnumerable<Models.Cae>>(
                _datos.Values.Select(c => Copia(c, c.UltimoNroUsado)).ToList());
    }

    /// <inheritdoc/>
    public virtual ValueTask GuardarCaeAsync(Models.Cae cae)
    {
        ArgumentNullException.ThrowIfNull(cae);

        lock (_lock)
        {
            var ultimo = _datos.TryGetValue(cae.NroSerie, out var existente)
                ? Math.Max(cae.UltimoNroUsado, existente.UltimoNroUsado)
                : cae.UltimoNroUsado;
            _datos[cae.NroSerie] = Copia(cae, ultimo);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask ActualizarUltimoNroUsadoAsync(string nroSerie, long ultimoNroUsado)
    {
        ArgumentNullException.ThrowIfNull(nroSerie);

        lock (_lock)
        {
            if (!_datos.TryGetValue(nroSerie, out var existente))
                throw new KeyNotFoundException(
                    $"No se encontró un CAE con NroSerie '{nroSerie}'.");

            _datos[nroSerie] = Copia(existente, Math.Max(existente.UltimoNroUsado, ultimoNroUsado));
        }

        return ValueTask.CompletedTask;
    }

    private static Models.Cae Copia(Models.Cae cae, long ultimoNroUsado) => new()
    {
        NroSerie = cae.NroSerie,
        NumeroAutorizacion = cae.NumeroAutorizacion,
        Serie = cae.Serie,
        TipoCfe = cae.TipoCfe,
        RangoDesde = cae.RangoDesde,
        RangoHasta = cae.RangoHasta,
        FechaVencimiento = cae.FechaVencimiento,
        UltimoNroUsado = ultimoNroUsado,
    };
}
