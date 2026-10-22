namespace UruFactura;

/// <summary>
/// Cliente de facturación electrónica de la DGI. El flujo es:
/// <see cref="RegistrarCaeAsync"/> (una vez por CAE) → <see cref="EmitirAsync"/> → <see cref="EnviarAsync(CfeEmitido, CancellationToken)"/>
/// → <see cref="ConsultarEnvioAsync"/>, y al cierre del día <see cref="EnviarReporteDiarioAsync"/>.
/// Es thread-safe: regístrelo como singleton.
/// </summary>
public interface IUruFacturaClient : IDisposable
{
    // --- CAE ---

    /// <summary>Registra un CAE entregado por la DGI (o actualiza uno ya registrado).</summary>
    /// <exception cref="CaeException">Si los datos del CAE no son válidos.</exception>
    Task RegistrarCaeAsync(Cae cae, CancellationToken cancellationToken = default);

    /// <summary>Devuelve los CAE registrados con su último número usado.</summary>
    Task<IReadOnlyList<Cae>> ObtenerCaesAsync(CancellationToken cancellationToken = default);

    /// <summary>Advertencias de los CAE: vencidos, agotados, por vencer o con el rango casi usado.</summary>
    /// <param name="diasAlertaVencimiento">Días de anticipación para alertar el vencimiento.</param>
    /// <param name="porcentajeAlertaUso">Porcentaje de uso del rango a partir del cual alertar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<IReadOnlyList<string>> ObtenerAdvertenciasCaeAsync(
        int diasAlertaVencimiento = 7, decimal porcentajeAlertaUso = 80m, CancellationToken cancellationToken = default);

    // --- Emisión ---

    /// <summary>Devuelve los errores que impedirían emitir el CFE (vacía si es válido). No reserva números.</summary>
    IReadOnlyList<string> Validar(Cfe cfe);

    /// <summary>
    /// Emite el CFE: lo valida, le asigna el próximo número del CAE vigente, genera el XML, lo firma y
    /// lo registra en el almacenamiento (del que sale el reporte diario). No lo envía a la DGI.
    /// </summary>
    /// <exception cref="CfeValidationException">Si el CFE no es válido (no se consume ningún número).</exception>
    /// <exception cref="CaeException">Si no hay un CAE vigente con números disponibles para el tipo.</exception>
    Task<CfeEmitido> EmitirAsync(Cfe cfe, CancellationToken cancellationToken = default);

    // --- DGI ---

    /// <summary>Envía el CFE a la DGI en un sobre. El acuse indica si la DGI recibió el sobre, no si aceptó el CFE.</summary>
    Task<AcuseSobre> EnviarAsync(CfeEmitido cfe, CancellationToken cancellationToken = default);

    /// <summary>Envía de 1 a 250 CFE en un único sobre.</summary>
    Task<AcuseSobre> EnviarAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el resultado de un sobre recibido y actualiza el estado (<see cref="CfeEmitido.EstadoDgi"/>)
    /// de sus comprobantes. Si la DGI todavía no lo procesó, <see cref="EstadoEnvio.Procesado"/> es <c>false</c>
    /// (ver <see cref="AcuseSobre.ConsultarDesde"/>).
    /// </summary>
    Task<EstadoEnvio> ConsultarEnvioAsync(AcuseSobre acuse, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado de un CFE (servicio <c>ws_consultas</c>) y actualiza su <see cref="CfeEmitido.EstadoDgi"/>.</summary>
    Task<EstadoComprobante> ConsultarCfeAsync(CfeEmitido cfe, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía el reporte diario (resumen firmado de montos, cantidades y rangos) de los CFE emitidos en
    /// <paramref name="fecha"/>, tomados del almacenamiento.
    /// </summary>
    /// <param name="fecha">Fecha de emisión a resumir (en general, <c>HoraUruguay.Hoy().AddDays(-1)</c>).</param>
    /// <param name="secEnvio">Secuencia del envío del día (0 el primero, 1 si se reenvía, ...).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<AcuseReporte> EnviarReporteDiarioAsync(DateOnly fecha, int secEnvio = 0, CancellationToken cancellationToken = default);
}
