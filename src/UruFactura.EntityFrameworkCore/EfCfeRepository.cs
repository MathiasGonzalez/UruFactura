using Microsoft.EntityFrameworkCore;
using UruFactura.Models;
using UruFactura.Persistence;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// <see cref="ICfeRepository"/> sobre Entity Framework Core: registro de los CFE emitidos por todas
/// las instancias, del que sale el reporte diario. Es thread-safe: cada operación usa su propio
/// <see cref="DbContext"/>, creado con la fábrica.
/// </summary>
/// <typeparam name="TContext">Contexto que incluye el modelo de UruFactura (<see cref="UruFacturaDbContext"/> o uno propio con <see cref="UruFacturaModelBuilderExtensions.ApplyUruFacturaModel"/>).</typeparam>
public class EfCfeRepository<TContext>(IDbContextFactory<TContext> contextFactory) : ICfeRepository
    where TContext : DbContext
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">Si algún CFE no tiene serie y número asignados.</exception>
    public virtual async ValueTask GuardarAsync(IReadOnlyCollection<Cfe> cfes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cfes);
        if (cfes.Count == 0)
            return;
        if (cfes.Any(c => string.IsNullOrWhiteSpace(c.Serie) || c.Numero <= 0))
            throw new ArgumentException(
                "Solo se pueden guardar CFE con serie y número asignados (AsignarCaeAsync).", nameof(cfes));

        for (var intento = 1; ; intento++)
        {
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var registros = db.Set<CfeEntity>();

            var numeros = cfes.Select(c => c.Numero).Distinct().ToList();
            var existentes = await registros.Where(e => numeros.Contains(e.Numero)).ToListAsync(cancellationToken);
            var ahora = DateTimeOffset.UtcNow;

            foreach (var cfe in cfes)
            {
                var entidad = existentes.FirstOrDefault(e =>
                    e.TipoCfe == cfe.Tipo && e.Serie == cfe.Serie && e.Numero == cfe.Numero);
                if (entidad is null)
                {
                    entidad = new CfeEntity { TipoCfe = cfe.Tipo, Serie = cfe.Serie!, Numero = cfe.Numero, FechaRegistro = ahora };
                    registros.Add(entidad);
                    existentes.Add(entidad);
                }
                entidad.CopiarDesde(cfe, ahora);
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
    public virtual async ValueTask<IReadOnlyList<Cfe>> ObtenerPorFechaEmisionAsync(
        DateOnly fechaEmision, CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var entidades = await db.Set<CfeEntity>().AsNoTracking()
            .Where(e => e.FechaEmision == fechaEmision)
            .OrderBy(e => e.TipoCfe).ThenBy(e => e.Serie).ThenBy(e => e.Numero)
            .ToListAsync(cancellationToken);
        return entidades.Select(e => e.ToCfe()).ToList();
    }
}
