using Microsoft.EntityFrameworkCore;

namespace UruFactura.EntityFrameworkCore;

/// <summary>Modelo de base de datos de UruFactura para Entity Framework Core.</summary>
public static class UruFacturaModelBuilderExtensions
{
    /// <summary>
    /// Agrega las tablas de UruFactura (<c>UruFacturaCaes</c>, <c>UruFacturaCfes</c> y
    /// <c>UruFacturaSecuencias</c>) al modelo. Llamarlo desde <c>OnModelCreating</c> para usar el
    /// <see cref="DbContext"/> propio de la aplicación en lugar de <see cref="UruFacturaDbContext"/>.
    /// </summary>
    public static ModelBuilder ApplyUruFacturaModel(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<CaeEntity>(e =>
        {
            e.ToTable("UruFacturaCaes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasValueGenerator((_, _) => new GuidOrdenadoValueGenerator());
            e.HasIndex(c => c.NroSerie).IsUnique();
            e.HasIndex(c => c.TipoCfe);
            e.Property(c => c.NroSerie).HasMaxLength(50).IsRequired();
            e.Property(c => c.NumeroAutorizacion).HasMaxLength(11).IsRequired();
            e.Property(c => c.Serie).HasMaxLength(2).IsRequired();
        });

        modelBuilder.Entity<CfeEntity>(e =>
        {
            e.ToTable("UruFacturaCfes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasValueGenerator((_, _) => new GuidOrdenadoValueGenerator());
            // Respaldo en la base: un mismo número de CFE no puede registrarse dos veces.
            e.HasIndex(c => new { c.TipoCfe, c.Serie, c.Numero }).IsUnique();
            e.HasIndex(c => c.FechaEmision);
            e.HasIndex(c => c.NroInterno);
            e.Property(c => c.Serie).HasMaxLength(2).IsRequired();
            e.Property(c => c.NroInterno).HasMaxLength(50);
            e.Property(c => c.ReceptorDocumento).HasMaxLength(20);
            // Montos con 2 decimales, como en el XML; el tipo de cambio lleva 3.
            foreach (var monto in e.Metadata.GetProperties().Where(p => p.ClrType == typeof(decimal)))
                e.Property(monto.Name).HasPrecision(18, 2);
            e.Property(c => c.TipoCambio).HasPrecision(10, 3);
            e.Property(c => c.CodigoSeguridad).HasMaxLength(100);
            e.Property(c => c.IdReceptorDgi).HasMaxLength(20);
            e.Property(c => c.CodigoRespuestaDgi).HasMaxLength(20);
        });

        modelBuilder.Entity<SecuenciaEntity>(e =>
        {
            e.ToTable("UruFacturaSecuencias");
            e.HasKey(s => s.Nombre);
            e.Property(s => s.Nombre).HasMaxLength(50);
        });

        return modelBuilder;
    }
}
