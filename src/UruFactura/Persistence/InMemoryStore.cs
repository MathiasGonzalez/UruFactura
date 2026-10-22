namespace UruFactura;

/// <summary>
/// <see cref="IUruFacturaStore"/> en memoria, el que usa el cliente si no se indica otro. Sirve para
/// un solo proceso: los datos se pierden al reiniciar (si la aplicación se reinicia, se repetirían
/// números). En producción use un almacenamiento durable, como el de <c>UruFactura.EntityFrameworkCore</c>.
/// Es thread-safe.
/// </summary>
public sealed class InMemoryStore : IUruFacturaStore
{
    private readonly List<Cae> _caes = [];
    private readonly Dictionary<(TipoCfe, string, long), CfeEmitido> _cfes = [];
    private readonly Lock _lock = new();
    private long _ultimoIdEnvio;

    /// <summary>
    /// Días de CFE que se conservan, contados desde la fecha de emisión más reciente (por defecto 7).
    /// Alcanza para el reporte diario y evita que la memoria crezca sin límite.
    /// </summary>
    public int DiasRetencionCfe
    {
        get;
        init => field = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Debe ser al menos 1.");
    } = 7;

    /// <inheritdoc />
    public ValueTask GuardarCaeAsync(Cae cae, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cae);
        lock (_lock)
        {
            var i = _caes.FindIndex(c => c.EsElMismo(cae));
            if (i < 0)
                _caes.Add(cae);
            else
                _caes[i] = cae with { UltimoNroUsado = Math.Max(cae.UltimoNroUsado, _caes[i].UltimoNroUsado) };
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Cae>> ListarCaesAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
            return ValueTask.FromResult<IReadOnlyList<Cae>>([.. _caes]);
    }

    /// <inheritdoc />
    public ValueTask<Cae?> ReservarNumeroAsync(TipoCfe tipo, DateOnly hoy, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (Cae.Elegir(_caes, tipo, hoy) is not { } cae)
                return ValueTask.FromResult<Cae?>(null);

            var reservado = cae with { UltimoNroUsado = cae.ProximoNumero };
            _caes[_caes.IndexOf(cae)] = reservado;
            return ValueTask.FromResult<Cae?>(reservado);
        }
    }

    /// <inheritdoc />
    /// <remarks>Basado en la hora, para no repetir números de envíos anteriores a un reinicio.</remarks>
    public ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _ultimoIdEnvio = Math.Max(_ultimoIdEnvio + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            return ValueTask.FromResult(_ultimoIdEnvio);
        }
    }

    /// <inheritdoc />
    public ValueTask GuardarCfesAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cfes);
        lock (_lock)
        {
            foreach (var cfe in cfes)
                _cfes[(cfe.Tipo, cfe.Serie, cfe.Numero)] = cfe;

            if (_cfes.Count > 0)
            {
                var limite = _cfes.Values.Max(c => c.FechaEmision).AddDays(-DiasRetencionCfe);
                foreach (var viejo in _cfes.Where(c => c.Value.FechaEmision <= limite).Select(c => c.Key).ToList())
                    _cfes.Remove(viejo);
            }
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CfeEmitido>> ListarCfesAsync(DateOnly fechaEmision, CancellationToken cancellationToken = default)
    {
        lock (_lock)
            return ValueTask.FromResult<IReadOnlyList<CfeEmitido>>(
                [.. _cfes.Values.Where(c => c.FechaEmision == fechaEmision).OrderBy(c => c.Tipo).ThenBy(c => c.Serie, StringComparer.Ordinal).ThenBy(c => c.Numero)]);
    }
}
