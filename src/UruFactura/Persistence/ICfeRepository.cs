using UruFactura.Models;

namespace UruFactura.Persistence;

/// <summary>
/// Registro durable de los CFE emitidos, compartido por todas las instancias de la aplicación.
/// El cliente lo actualiza al enviar comprobantes y al consultar su estado, y lo lee para armar
/// el reporte diario con los CFE de todas las instancias.
/// <para>
/// Un CFE se identifica por tipo, serie y número. Los CFE que devuelve
/// <see cref="ObtenerPorFechaEmisionAsync"/> pueden no tener <see cref="Cfe.Detalle"/>: alcanza con
/// los totales, la sucursal, la fecha y la numeración (y, para reenviarlos, el XML firmado).
/// </para>
/// </summary>
public interface ICfeRepository
{
    /// <summary>
    /// Inserta o actualiza los CFE (por tipo, serie y número). Los datos que lleguen vacíos
    /// (por ejemplo, el XML firmado de un CFE reconstruido sin él) no deben borrar los guardados.
    /// </summary>
    ValueTask GuardarAsync(IReadOnlyCollection<Cfe> cfes, CancellationToken cancellationToken = default);

    /// <summary>Devuelve los CFE con la fecha de emisión indicada.</summary>
    ValueTask<IReadOnlyList<Cfe>> ObtenerPorFechaEmisionAsync(DateOnly fechaEmision, CancellationToken cancellationToken = default);
}
