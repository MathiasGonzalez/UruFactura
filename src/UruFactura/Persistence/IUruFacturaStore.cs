namespace UruFactura;

/// <summary>
/// Almacenamiento de UruFactura: los CAE con su numeración, el número de envío del emisor y los CFE
/// emitidos (de los que sale el reporte diario). Es la fuente de verdad de la numeración: cada
/// reserva debe ser atómica para que dos instancias de la aplicación nunca obtengan el mismo número.
/// <para>
/// Implementaciones incluidas: <see cref="InMemoryStore"/> (por defecto; un solo proceso, sin
/// persistencia) y <c>EfUruFacturaStore</c> del paquete <c>UruFactura.EntityFrameworkCore</c>.
/// </para>
/// </summary>
public interface IUruFacturaStore
{
    /// <summary>
    /// Agrega un CAE o actualiza el existente (mismo tipo, serie y comienzo de rango).
    /// <see cref="Cae.UltimoNroUsado"/> nunca retrocede: si el guardado es mayor, se conserva.
    /// </summary>
    ValueTask GuardarCaeAsync(Cae cae, CancellationToken cancellationToken = default);

    /// <summary>Devuelve todos los CAE con su último número usado.</summary>
    ValueTask<IReadOnlyList<Cae>> ListarCaesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva de forma atómica el próximo número para <paramref name="tipo"/>, con el CAE vigente en
    /// <paramref name="hoy"/> de vencimiento más lejano y, a igual vencimiento, con más números libres.
    /// </summary>
    /// <returns>El CAE con <see cref="Cae.UltimoNroUsado"/> igual al número reservado, o <c>null</c> si no hay CAE disponible.</returns>
    ValueTask<Cae?> ReservarNumeroAsync(TipoCfe tipo, DateOnly hoy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva el número de envío del emisor (<c>Idemisor</c> del sobre e <c>IDEmisor</c> del reporte
    /// diario, hasta 10 dígitos), estrictamente creciente y único entre instancias.
    /// </summary>
    ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Agrega los CFE o actualiza su estado ante la DGI (se identifican por tipo, serie y número).
    /// </summary>
    ValueTask GuardarCfesAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default);

    /// <summary>Devuelve los CFE emitidos en la fecha indicada.</summary>
    ValueTask<IReadOnlyList<CfeEmitido>> ListarCfesAsync(DateOnly fechaEmision, CancellationToken cancellationToken = default);
}
