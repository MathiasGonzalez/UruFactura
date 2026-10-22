using Microsoft.EntityFrameworkCore;
using UruFactura.Cae;
using UruFactura.Enums;
using UruFactura.Exceptions;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// <see cref="ICaeRepositoryCompartido"/> sobre Entity Framework Core: varias instancias de la
/// aplicación pueden numerar CFE contra la misma base sin repetir números.
/// <para>
/// La reserva es un compare-and-swap (<c>UPDATE ... WHERE UltimoNroUsado = @leido</c>) que se
/// reintenta si otra instancia ganó; funciona igual en SQL Server, PostgreSQL, MySQL o SQLite,
/// sin bloqueos explícitos ni niveles de aislamiento especiales.
/// </para>
/// Es thread-safe: cada operación usa su propio <see cref="DbContext"/>, creado con la fábrica.
/// </summary>
/// <typeparam name="TContext">Contexto que incluye el modelo de UruFactura (<see cref="UruFacturaDbContext"/> o uno propio con <see cref="UruFacturaModelBuilderExtensions.ApplyUruFacturaModel"/>).</typeparam>
public class EfCaeRepository<TContext>(IDbContextFactory<TContext> contextFactory) : ICaeRepositoryCompartido
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

    /// <inheritdoc />
    public virtual async ValueTask<IEnumerable<Models.Cae>> CargarTodosAsync()
    {
        await using var db = await contextFactory.CreateDbContextAsync();
        var entidades = await db.Set<CaeEntity>().AsNoTracking().ToListAsync();
        return entidades.Select(e => e.ToCae()).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Si el CAE ya existe se actualizan sus datos, pero <c>UltimoNroUsado</c> nunca retrocede:
    /// otra instancia pudo haber reservado números después de que se leyó <paramref name="cae"/>.
    /// </remarks>
    public virtual async ValueTask GuardarCaeAsync(Models.Cae cae)
    {
        ArgumentNullException.ThrowIfNull(cae);

        await using var db = await contextFactory.CreateDbContextAsync();
        var caes = db.Set<CaeEntity>();

        if (await ActualizarDatosAsync(caes, cae) == 0)
        {
            caes.Add(CaeEntity.Desde(cae));
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException)
            {
                // Otra instancia lo insertó al mismo tiempo: se actualiza el existente.
                db.ChangeTracker.Clear();
                if (await ActualizarDatosAsync(caes, cae) == 0)
                    throw;
            }
        }

        await caes
            .Where(c => c.NroSerie == cae.NroSerie && c.UltimoNroUsado < cae.UltimoNroUsado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimoNroUsado, cae.UltimoNroUsado));
    }

    /// <inheritdoc />
    /// <remarks>El valor solo se actualiza si es mayor que el guardado (nunca retrocede).</remarks>
    public virtual async ValueTask ActualizarUltimoNroUsadoAsync(string nroSerie, long ultimoNroUsado)
    {
        ArgumentNullException.ThrowIfNull(nroSerie);

        await using var db = await contextFactory.CreateDbContextAsync();
        var caes = db.Set<CaeEntity>();

        var filas = await caes
            .Where(c => c.NroSerie == nroSerie && c.UltimoNroUsado < ultimoNroUsado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.UltimoNroUsado, ultimoNroUsado));

        if (filas == 0 && !await caes.AnyAsync(c => c.NroSerie == nroSerie))
            throw new KeyNotFoundException($"No se encontró un CAE con NroSerie '{nroSerie}'.");
    }

    /// <inheritdoc />
    public virtual ValueTask<(Models.Cae Cae, long Numero)?> ReservarProximoNumeroAsync(
        TipoCfe tipoCfe, DateOnly hoy, CancellationToken cancellationToken = default) =>
        ReintentarAsync<(Models.Cae Cae, long Numero)?>($"reservar un número para el tipo {tipoCfe}", async db =>
        {
            var caes = db.Set<CaeEntity>();

            // Mismo criterio que CaeManager: vencimiento más lejano y, a igual vencimiento, más números libres.
            // Un CAE con el rango invertido se ignora: su número quedaría fuera del rango.
            var candidato = await caes.AsNoTracking()
                .Where(c => c.TipoCfe == tipoCfe && c.FechaVencimiento >= hoy
                            && c.UltimoNroUsado < c.RangoHasta && c.RangoDesde <= c.RangoHasta)
                .OrderByDescending(c => c.FechaVencimiento)
                .ThenByDescending(c => c.RangoHasta - c.UltimoNroUsado)
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
            return (true, (candidato.ToCae(), numero));
        }, cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Basado en la hora (como el cliente sin almacenamiento compartido) para no repetir números
    /// de envíos hechos antes de usar la base, y estrictamente creciente entre todas las instancias.
    /// </remarks>
    public virtual ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default) =>
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

        throw new UruFacturaException(
            $"No se pudo {operacion} tras {MaxIntentos} intentos (contención entre instancias).");
    }

    private static Task<int> ActualizarDatosAsync(DbSet<CaeEntity> caes, Models.Cae cae) =>
        caes.Where(c => c.NroSerie == cae.NroSerie).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.NumeroAutorizacion, cae.NumeroAutorizacion)
            .SetProperty(c => c.Serie, cae.Serie)
            .SetProperty(c => c.TipoCfe, cae.TipoCfe)
            .SetProperty(c => c.RangoDesde, cae.RangoDesde)
            .SetProperty(c => c.RangoHasta, cae.RangoHasta)
            .SetProperty(c => c.FechaVencimiento, cae.FechaVencimiento));
}
