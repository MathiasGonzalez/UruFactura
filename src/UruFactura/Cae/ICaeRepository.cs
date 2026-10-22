namespace UruFactura.Cae;

/// <summary>
/// Contrato para la persistencia de CAE (Constancias de Autorización de Emisión).
/// <para>
/// En producción, implementar esta interfaz con una base de datos o archivo de configuración
/// para garantizar que el estado de los CAEs (especialmente <c>UltimoNroUsado</c>) sobreviva
/// reinicios de la aplicación. Al arrancar, cargar los CAEs y registrarlos en el
/// <see cref="ICaeManager"/> con <c>RegistrarCaes</c>.
/// </para>
/// <para>
/// La implementación predeterminada <see cref="InMemoryCaeRepository"/> almacena los datos en
/// memoria y es adecuada para pruebas o escenarios donde la persistencia no es necesaria.
/// </para>
/// </summary>
/// <example>
/// <code>
/// // Al iniciar la aplicación:
/// var caes = await caeRepository.CargarTodosAsync();
/// client.Cae.RegistrarCaes(caes);
///
/// // Al emitir un CFE:
/// var (cae, numero) = client.Cae.ObtenerProximoNumero(TipoCfe.ETicket);
/// await caeRepository.ActualizarUltimoNroUsadoAsync(cae.NroSerie, cae.UltimoNroUsado);
/// </code>
/// </example>
public interface ICaeRepository
{
    /// <summary>
    /// Carga todos los CAEs persistidos.
    /// </summary>
    /// <returns>Colección de CAEs, vacía si no hay ninguno registrado.</returns>
    ValueTask<IEnumerable<Models.Cae>> CargarTodosAsync();

    /// <summary>
    /// Persiste un nuevo CAE o sobreescribe uno existente con el mismo <see cref="Models.Cae.NroSerie"/>.
    /// El <see cref="Models.Cae.UltimoNroUsado"/> guardado nunca debe retroceder: si el existente es
    /// mayor, se conserva (volver a guardar un CAE con un contador viejo repetiría números).
    /// </summary>
    /// <param name="cae">CAE a guardar.</param>
    ValueTask GuardarCaeAsync(Models.Cae cae);

    /// <summary>
    /// Actualiza el último número de comprobante utilizado de un CAE ya guardado.
    /// Debe llamarse inmediatamente después de cada emisión exitosa. Un valor menor al guardado se ignora.
    /// </summary>
    /// <param name="nroSerie">Número de serie del CAE a actualizar.</param>
    /// <param name="ultimoNroUsado">Último número emitido.</param>
    /// <exception cref="KeyNotFoundException">Si no existe un CAE con ese <paramref name="nroSerie"/>.</exception>
    ValueTask ActualizarUltimoNroUsadoAsync(string nroSerie, long ultimoNroUsado);
}
