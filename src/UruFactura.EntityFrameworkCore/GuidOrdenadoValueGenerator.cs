using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace UruFactura.EntityFrameworkCore;

/// <summary>
/// Claves <see cref="Guid"/> ordenadas por tiempo, generadas en el cliente (sin ida y vuelta a la
/// base) y únicas entre instancias, para que el índice primario no se fragmente:
/// UUID versión 7 (RFC 9562) y, en SQL Server, el GUID secuencial de EF Core, porque SQL Server
/// ordena <c>uniqueidentifier</c> empezando por los últimos bytes y un v7 llegaría desordenado.
/// </summary>
internal sealed class GuidOrdenadoValueGenerator : ValueGenerator<Guid>
{
    private const string ProveedorSqlServer = "Microsoft.EntityFrameworkCore.SqlServer";

    private readonly SequentialGuidValueGenerator _sqlServer = new();

    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) =>
        entry.Context.Database.ProviderName == ProveedorSqlServer
            ? _sqlServer.Next(entry)
            : Guid.CreateVersion7();
}
