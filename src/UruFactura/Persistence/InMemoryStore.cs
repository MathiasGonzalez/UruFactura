namespace UruFactura;

/// <summary>
/// <see cref="IUruFacturaStore"/> en memoria, el que usa el cliente en Homologación si no se indica
/// otro. Sirve para un solo proceso: los datos se pierden al reiniciar, así que se repetirían números
/// y no tendría los CFE de días anteriores (el reporte diario de esos días falla en lugar de declarar
/// cero comprobantes). En producción use un almacenamiento durable, como el de
/// <c>UruFactura.EntityFrameworkCore</c>. Es thread-safe.
/// </summary>
public sealed class InMemoryStore : IUruFacturaStore
{
    private readonly List<Cae> _caes = [];
    private readonly Dictionary<(TipoCfe, string, long), CfeEmitido> _cfes = [];
    private readonly Lock _lock = new();
    private readonly TimeProvider _reloj;
    private readonly DateOnly _creado;
    private long _ultimoIdEnvio;

    /// <summary>Crea el almacenamiento vacío.</summary>
    /// <param name="reloj">Reloj con el que se cuentan los días de retención (por defecto, el del sistema).</param>
    public InMemoryStore(TimeProvider? reloj = null)
    {
        _reloj = reloj ?? TimeProvider.System;
        _creado = HoraUruguay.Hoy(_reloj);
    }

    /// <summary>
    /// Días de CFE que se conservan, contando hoy (por defecto 7). Alcanza para el reporte diario y
    /// evita que la memoria crezca sin límite.
    /// </summary>
    public int DiasRetencionCfe
    {
        get;
        init => field = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Debe ser al menos 1.");
    } = 7;

    /// <summary>Primer día del que tiene todos los CFE: el de su creación o el más antiguo que conserva.</summary>
    private DateOnly CompletoDesde
    {
        get
        {
            var conservado = HoraUruguay.Hoy(_reloj).AddDays(1 - DiasRetencionCfe);
            return conservado > _creado ? conservado : _creado;
        }
    }

    // -----------------------------------------------------------------------
    // CAE
    // -----------------------------------------------------------------------

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
    public ValueTask<bool> EliminarCaeAsync(Cae cae, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cae);
        lock (_lock)
        {
            var i = _caes.FindIndex(c => c.EsElMismo(cae));
            if (i < 0 || _caes[i].UltimoNroUsado >= _caes[i].RangoDesde)
                return ValueTask.FromResult(false);

            _caes.RemoveAt(i);
            return ValueTask.FromResult(true);
        }
    }

    // -----------------------------------------------------------------------
    // Emisión
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public ValueTask<bool> RegistrarEmisionAsync(CfeEmitido emitido, long ultimoNroUsadoLeido, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emitido);
        lock (_lock)
        {
            var i = _caes.FindIndex(c => c.EsElMismo(emitido.Cae));
            if (i < 0 || _caes[i].UltimoNroUsado != ultimoNroUsadoLeido)
                return ValueTask.FromResult(false);

            var clave = (emitido.Tipo, emitido.Serie, emitido.Numero);
            if (_cfes.ContainsKey(clave))
                throw new UruFacturaException($"El CFE {emitido} ya está registrado: revise los rangos de los CAE de la serie.");

            _caes[i] = _caes[i] with { UltimoNroUsado = emitido.Numero };
            _cfes[clave] = emitido;
            Descartar();
            return ValueTask.FromResult(true);
        }
    }

    /// <inheritdoc />
    /// <remarks>Basado en la hora, para no repetir números de envíos anteriores a un reinicio.</remarks>
    public ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            _ultimoIdEnvio = Math.Max(_ultimoIdEnvio + 1, _reloj.GetUtcNow().ToUnixTimeSeconds());
            return ValueTask.FromResult(_ultimoIdEnvio);
        }
    }

    // -----------------------------------------------------------------------
    // CFE
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public ValueTask GuardarCfesAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cfes);
        lock (_lock)
        {
            foreach (var cfe in cfes)
                _cfes[(cfe.Tipo, cfe.Serie, cfe.Numero)] = cfe;
            Descartar();
        }
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<CfeEmitido>> BuscarCfesAsync(string nroInterno, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nroInterno);
        lock (_lock)
            return ValueTask.FromResult<IReadOnlyList<CfeEmitido>>(
                [.. _cfes.Values.Where(c => c.Documento.NroInterno == nroInterno).OrderBy(c => c.FechaFirma)]);
    }

    /// <inheritdoc />
    /// <exception cref="UruFacturaException">
    /// Si la fecha es anterior a la creación del almacenamiento o a los días que conserva.
    /// </exception>
    public IAsyncEnumerable<CfeEmitido> ListarCfesAsync(DateOnly fechaEmision, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (fechaEmision < CompletoDesde)
                throw new UruFacturaException(
                    $"InMemoryStore no tiene todos los CFE del {fechaEmision:dd/MM/yyyy}: solo los emitidos desde el " +
                    $"{CompletoDesde:dd/MM/yyyy} (se creó ese día o descartó los anteriores por DiasRetencionCfe).");

            return _cfes.Values.Where(c => c.FechaEmision == fechaEmision)
                .OrderBy(c => c.Tipo).ThenBy(c => c.Serie, StringComparer.Ordinal).ThenBy(c => c.Numero)
                .ToList()
                .ToAsyncEnumerable();
        }
    }

    /// <summary>Descarta los CFE anteriores a los días de retención.</summary>
    private void Descartar()
    {
        var desde = HoraUruguay.Hoy(_reloj).AddDays(1 - DiasRetencionCfe);
        foreach (var viejo in _cfes.Where(c => c.Value.FechaEmision < desde).Select(c => c.Key).ToList())
            _cfes.Remove(viejo);
    }
}
