namespace UruFactura;

/// <summary>
/// Almacenamiento de UruFactura: los CAE con su numeración, el número de envío del emisor y los CFE
/// emitidos (de los que sale el reporte diario). Es la fuente de verdad de la numeración: un número
/// se usa solo junto con el registro de su CFE, de forma atómica, para que dos instancias de la
/// aplicación nunca obtengan el mismo número y no queden números consumidos sin comprobante.
/// <para>
/// Implementaciones incluidas: <see cref="InMemoryStore"/> (un solo proceso, sin persistencia) y
/// <c>EfUruFacturaStore</c> del paquete <c>UruFactura.EntityFrameworkCore</c>.
/// </para>
/// </summary>
public interface IUruFacturaStore
{
    // --- CAE ---

    /// <summary>
    /// Agrega un CAE o actualiza el existente (mismo tipo, serie y comienzo de rango).
    /// <see cref="Cae.UltimoNroUsado"/> nunca retrocede: si el guardado es mayor, se conserva.
    /// </summary>
    ValueTask GuardarCaeAsync(Cae cae, CancellationToken cancellationToken = default);

    /// <summary>Devuelve todos los CAE con su último número usado.</summary>
    ValueTask<IReadOnlyList<Cae>> ListarCaesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina el CAE (mismo tipo, serie y comienzo de rango) si todavía no numeró ningún CFE
    /// (<see cref="Cae.UltimoNroUsado"/> menor que <see cref="Cae.RangoDesde"/>).
    /// </summary>
    /// <returns><c>false</c> si no existe o ya se usó.</returns>
    ValueTask<bool> EliminarCaeAsync(Cae cae, CancellationToken cancellationToken = default);

    // --- Emisión ---

    /// <summary>
    /// Registra un CFE recién emitido y avanza su CAE (<see cref="CfeEmitido.Cae"/>) a
    /// <see cref="CfeEmitido.Numero"/>, en una sola operación atómica: se hacen las dos cosas o ninguna.
    /// </summary>
    /// <param name="emitido">CFE emitido.</param>
    /// <param name="ultimoNroUsadoLeido">El <see cref="Cae.UltimoNroUsado"/> del CAE leído antes de numerar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// <c>false</c>, sin registrar nada, si el CAE ya no tiene <paramref name="ultimoNroUsadoLeido"/>
    /// (otra emisión usó el número antes) o ya no existe: hay que volver a numerar.
    /// </returns>
    ValueTask<bool> RegistrarEmisionAsync(CfeEmitido emitido, long ultimoNroUsadoLeido, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserva el número de envío del emisor (<c>Idemisor</c> del sobre e <c>IDEmisor</c> del reporte
    /// diario, hasta 10 dígitos), estrictamente creciente y único entre instancias.
    /// </summary>
    ValueTask<long> ReservarIdEnvioAsync(CancellationToken cancellationToken = default);

    // --- CFE ---

    /// <summary>
    /// Actualiza el estado ante la DGI de los CFE (se identifican por tipo, serie y número) o los
    /// agrega si no están.
    /// </summary>
    ValueTask GuardarCfesAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default);

    /// <summary>Devuelve los CFE emitidos con el número interno indicado (<see cref="Cfe.NroInterno"/>).</summary>
    ValueTask<IReadOnlyList<CfeEmitido>> BuscarCfesAsync(string nroInterno, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve, de a uno, los CFE firmados en la fecha indicada (<see cref="CfeEmitido.FechaReporte"/>: la
    /// fecha de Uruguay de la firma, que el reporte diario resume sin cargarlos todos en memoria).
    /// </summary>
    /// <exception cref="UruFacturaException">Si el almacenamiento no tiene completos los CFE de esa fecha.</exception>
    IAsyncEnumerable<CfeEmitido> ListarCfesAsync(DateOnly fechaReporte, CancellationToken cancellationToken = default);
}
