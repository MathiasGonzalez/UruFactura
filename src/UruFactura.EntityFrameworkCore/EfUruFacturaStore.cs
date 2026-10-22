using Microsoft.EntityFrameworkCore;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// <see cref="IUruFacturaStore"/> sobre Entity Framework Core: varias instancias de la aplicación
/// pueden numerar y emitir contra la misma base sin repetir números, y el reporte diario incluye los
/// CFE de todas.
/// <para>
/// Cada reserva es un compare-and-swap (<c>UPDATE ... WHERE UltimoNroUsado = @leido</c>) que se
/// reintenta si otra instancia ganó; funciona igual en SQL Server, PostgreSQL, MySQL o SQLite, sin
/// bloqueos explícitos ni niveles de aislamiento especiales.
/// </para>
/// Es thread-safe: cada operación usa su propio <see cref="DbContext"/>, creado con la fábrica.
/// </summary>
/// <typeparam name="TContext">
/// Contexto con el modelo de UruFactura (<see cref="UruFacturaDbContext"/> o uno propio que llame a
/// <see cref="UruFacturaModelBuilderExtensions.ApplyUruFacturaModel"/>).
/// </typeparam>
public sealed class EfUruFacturaStore<TContext>(IDbContextFactory<TContext> contextFactory) : IUruFacturaStore
    where TContext : DbContext
{
    private const string SecuenciaIdEnvio = "IdEnvio";

    /// <summary>Intentos de una reserva ante contención con otras instancias antes de fallar (por defecto 100).</summary>
    public int MaxIntentos
    {
        get;
        init => field = value >= 1
            ? value
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Debe ser al menos 1.");
    } = 100;

    // -----------------------------------------------------------------------
    // CAE
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask GuardarCaeAsync(Cae cae, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cae);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var caes = db.Set<CaeEntity>();

        if (await ActualizarDatosAsync(caes, cae, cancellationToken) == 0)
        {
            caes.Add(CaeEntity.Desde(cae));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException)
            {
                // Otra instancia lo insertó al mismo tiempo: se actualiza el existente.
                db.ChangeTracker.Clear();
                if (await ActualizarDatosAsync(caes, cae, cancellationToken) == 0)
                    throw;
            }
        }

        // El contador nunca retrocede: otra instancia pudo haber reservado números mientras tanto.
        await Del(caes, cae)
            .Where(c => c.UltimoNroUsado < cae.UltimoNroUsado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimoNroUsado, cae.UltimoNroUsado), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<Cae>> ListarCaesAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entidades = await db.Set<CaeEntity>().AsNoTracking()
            .OrderBy(c => c.TipoCfe).ThenBy(c => c.Serie).ThenBy(c => c.RangoDesde)
            .ToListAsync(cancellationToken);
        return [.. entidades.Select(e => e.ToCae())];
    }

    /// <inheritdoc />
    public ValueTask<Cae?> ReservarNumeroAsync(TipoCfe tipo, DateOnly hoy, CancellationToken cancellationToken = default) =>
        ReintentarAsync<Cae?>($"reservar un número para {tipo}", async db =>
        {
            var caes = db.Set<CaeEntity>();

            // Mismo criterio que Cae.Elegir: vencimiento más lejano y, a igual vencimiento, más números libres.
            var candidato = await caes.AsNoTracking()
                .Where(c => c.TipoCfe == tipo && c.FechaVencimiento >= hoy
                            && c.UltimoNroUsado < c.RangoHasta && c.RangoDesde <= c.RangoHasta)
                .OrderByDescending(c => c.FechaVencimiento)
                .ThenByDescending(c => c.RangoHasta - (c.UltimoNroUsado >= c.RangoDesde ? c.UltimoNroUsado : c.RangoDesde - 1))
                .FirstOrDefaultAsync(cancellationToken);
            if (candidato is null)
                return (true, null);

            var leido = candidato.UltimoNroUsado;
            var numero = Math.Max(leido + 1, candidato.RangoDesde);

            // Compare-and-swap: solo una instancia logra pasar del valor leído al siguiente.
            var filas = await caes
                .Where(c => c.Id == candidato.Id && c.UltimoNroUsado == leido)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimoNroUsado, numero), cancellationToken);
            if (filas == 0)
                return (false, null);

            candidato.UltimoNroUsado = numero;
            return (true, candidato.ToCae());
        }, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Basado en la hora (para no repetir números de envíos anteriores a usar la base) y estrictamente
    /// creciente entre todas las instancias.
    /// </remarks>
    public ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default) =>
        ReintentarAsync("reservar el número de envío", async db =>
        {
            var secuencias = db.Set<SecuenciaEntity>();
            var actual = await secuencias.AsNoTracking()
                .Where(s => s.Nombre == SecuenciaIdEnvio)
                .Select(s => (long?)s.Valor)
                .FirstOrDefaultAsync(cancellationToken);
            var siguiente = Math.Max((actual ?? 0) + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            if (actual is not null)
            {
                var filas = await secuencias
                    .Where(s => s.Nombre == SecuenciaIdEnvio && s.Valor == actual.Value)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.Valor, siguiente), cancellationToken);
                return (filas == 1, siguiente);
            }

            secuencias.Add(new SecuenciaEntity { Nombre = SecuenciaIdEnvio, Valor = siguiente });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return (true, siguiente);
            }
            catch (DbUpdateException)
            {
                // Otra instancia creó la secuencia al mismo tiempo: se reintenta como actualización.
                return (false, 0L);
            }
        }, cancellationToken);

    // -----------------------------------------------------------------------
    // CFE
    // -----------------------------------------------------------------------

    /// <inheritdoc />
    public async ValueTask GuardarCfesAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cfes);
        if (cfes.Count == 0)
            return;

        for (var intento = 1; ; intento++)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var registros = db.Set<CfeEntity>();

            var numeros = cfes.Select(c => c.Numero).Distinct().ToList();
            var existentes = await registros.Where(e => numeros.Contains(e.Numero)).ToListAsync(cancellationToken);
            var ahora = DateTimeOffset.UtcNow;

            foreach (var cfe in cfes)
            {
                var entidad = existentes.FirstOrDefault(e => e.TipoCfe == cfe.Tipo && e.Serie == cfe.Serie && e.Numero == cfe.Numero);
                if (entidad is null)
                {
                    entidad = CfeEntity.Desde(cfe, ahora);
                    registros.Add(entidad);
                    existentes.Add(entidad);
                }
                else
                    entidad.CopiarEstado(cfe, ahora);
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException) when (intento < 3)
            {
                // Otra instancia insertó alguno de estos CFE al mismo tiempo: se reintenta como actualización.
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CfeEmitido>> ListarCfesAsync(DateOnly fechaEmision, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entidades = await db.Set<CfeEntity>().AsNoTracking()
            .Where(e => e.FechaEmision == fechaEmision)
            .OrderBy(e => e.TipoCfe).ThenBy(e => e.Serie).ThenBy(e => e.Numero)
            .ToListAsync(cancellationToken);
        return [.. entidades.Select(e => e.ToCfeEmitido())];
    }

    // -----------------------------------------------------------------------

    /// <summary>
    /// Ejecuta <paramref name="intento"/> con un contexto nuevo hasta que termine (otra instancia pudo
    /// ganar el compare-and-swap), con una espera breve y aleatoria entre intentos para no volver a chocar.
    /// </summary>
    private async ValueTask<T> ReintentarAsync<T>(
        string operacion, Func<TContext, Task<(bool Terminado, T Resultado)>> intento, CancellationToken cancellationToken)
    {
        for (var i = 1; i <= MaxIntentos; i++)
        {
            await using (var db = await contextFactory.CreateDbContextAsync(cancellationToken))
            {
                var (terminado, resultado) = await intento(db);
                if (terminado)
                    return resultado;
            }

            await Task.Delay(Random.Shared.Next(1, Math.Min(50, 5 * i) + 1), cancellationToken);
        }

        throw new UruFacturaException($"No se pudo {operacion} tras {MaxIntentos} intentos (contención entre instancias).");
    }

    private static IQueryable<CaeEntity> Del(DbSet<CaeEntity> caes, Cae cae) =>
        caes.Where(c => c.TipoCfe == cae.TipoCfe && c.Serie == cae.Serie && c.RangoDesde == cae.RangoDesde);

    private static Task<int> ActualizarDatosAsync(DbSet<CaeEntity> caes, Cae cae, CancellationToken cancellationToken) =>
        Del(caes, cae).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.NumeroAutorizacion, cae.NumeroAutorizacion)
            .SetProperty(c => c.RangoHasta, cae.RangoHasta)
            .SetProperty(c => c.FechaVencimiento, cae.FechaVencimiento), cancellationToken);
}
