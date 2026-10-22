using Microsoft.EntityFrameworkCore;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// <see cref="DbContext"/> listo para usar con las tablas de UruFactura. Si la aplicación ya tiene
/// su propio contexto, en lugar de este llame a <see cref="UruFacturaModelBuilderExtensions.ApplyUruFacturaModel"/>
/// en su <c>OnModelCreating</c>. Las migraciones se generan en la aplicación, con su proveedor.
/// </summary>
public class UruFacturaDbContext : DbContext
{
    public UruFacturaDbContext(DbContextOptions<UruFacturaDbContext> options)
        : base(options)
    {
    }

    /// <summary>Para contextos derivados, que reciben sus propias <see cref="DbContextOptions{TContext}"/>.</summary>
    protected UruFacturaDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<CaeEntity> Caes => Set<CaeEntity>();
    public DbSet<CfeEntity> Cfes => Set<CfeEntity>();
    public DbSet<SecuenciaEntity> Secuencias => Set<SecuenciaEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyUruFacturaModel();
    }
}
