namespace UruFactura;

/// <summary>
/// Cliente de facturación electrónica de la DGI. El flujo es:
/// <see cref="RegistrarCaeAsync"/> (una vez por CAE) → <see cref="EmitirAsync"/> → <see cref="EnviarAsync(CfeEmitido, CancellationToken)"/>
/// → <see cref="ConsultarEnvioAsync"/>, y al cierre del día <see cref="EnviarReporteDiarioAsync"/>. Con los receptores
/// electrónicos, además, <see cref="GenerarSobreReceptorAsync"/> y sus respuestas; y con los proveedores,
/// <see cref="RecibirSobreAsync"/> → <see cref="ResponderCfesAsync"/>. Es thread-safe: regístrelo como singleton.
/// </summary>
public interface IUruFacturaClient : IDisposable
{
    // --- CAE ---

    /// <summary>Registra un CAE entregado por la DGI (o actualiza uno ya registrado: mismo tipo, serie y comienzo de rango).</summary>
    /// <exception cref="CaeException">Si los datos no son válidos o el rango se superpone con otro CAE del mismo tipo y serie.</exception>
    Task RegistrarCaeAsync(Cae cae, CancellationToken cancellationToken = default);

    /// <summary>Elimina un CAE registrado por error (mismo tipo, serie y comienzo de rango) que todavía no numeró ningún CFE.</summary>
    /// <exception cref="CaeException">Si no está registrado o ya numeró comprobantes.</exception>
    Task EliminarCaeAsync(Cae cae, CancellationToken cancellationToken = default);

    /// <summary>Devuelve los CAE registrados con su último número usado.</summary>
    Task<IReadOnlyList<Cae>> ObtenerCaesAsync(CancellationToken cancellationToken = default);

    /// <summary>Advertencias de los CAE: vencidos, agotados, por vencer o con el rango casi usado.</summary>
    /// <param name="diasAlertaVencimiento">Días de anticipación para alertar el vencimiento.</param>
    /// <param name="porcentajeAlertaUso">Porcentaje de uso del rango a partir del cual alertar.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<IReadOnlyList<string>> ObtenerAdvertenciasCaeAsync(
        int diasAlertaVencimiento = 7, decimal porcentajeAlertaUso = 80m, CancellationToken cancellationToken = default);

    // --- Emisión ---

    /// <summary>
    /// Devuelve los errores que impedirían emitir el CFE, con la propiedad afectada de cada uno (vacía
    /// si es válido). No reserva números.
    /// </summary>
    IReadOnlyList<ErrorValidacion> Validar(Cfe cfe);

    /// <summary>
    /// Emite el CFE: lo valida, le asigna el próximo número del CAE vigente, genera el XML, lo firma y
    /// lo registra en el almacenamiento (del que sale el reporte diario). No lo envía a la DGI.
    /// El número y el registro van juntos: si la emisión falla o se cancela, no se consume ningún número.
    /// </summary>
    /// <exception cref="CfeValidationException">Si el CFE no es válido.</exception>
    /// <exception cref="CaeException">Si no hay un CAE vigente con números disponibles para el tipo.</exception>
    /// <exception cref="CfeDuplicadoException">
    /// Si ya se emitió un CFE del mismo tipo con el mismo <see cref="Cfe.NroInterno"/> que la DGI no rechazó
    /// (por ejemplo, al reintentar una emisión que sí se completó).
    /// </exception>
    Task<CfeEmitido> EmitirAsync(Cfe cfe, CancellationToken cancellationToken = default);

    // --- DGI ---

    /// <summary>Envía el CFE a la DGI en un sobre. El acuse indica si la DGI recibió el sobre, no si aceptó el CFE.</summary>
    /// <exception cref="InvalidOperationException">Si el CFE ya fue recibido por la DGI o tiene resultado (no se reenvía).</exception>
    Task<AcuseSobre> EnviarAsync(CfeEmitido cfe, CancellationToken cancellationToken = default);

    /// <summary>Envía de 1 a 250 CFE en un único sobre.</summary>
    /// <exception cref="InvalidOperationException">Si alguno ya fue recibido por la DGI o tiene resultado (no se reenvía).</exception>
    Task<AcuseSobre> EnviarAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el resultado de un sobre recibido y actualiza el estado (<see cref="CfeEmitido.EstadoDgi"/>)
    /// de sus comprobantes. Si la DGI todavía no lo procesó, <see cref="EstadoEnvio.Procesado"/> es <c>false</c>
    /// (ver <see cref="AcuseSobre.ConsultarDesde"/>).
    /// </summary>
    Task<EstadoEnvio> ConsultarEnvioAsync(AcuseSobre acuse, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el estado de un CFE (servicio <c>ws_consultas</c>) y actualiza su <see cref="CfeEmitido.EstadoDgi"/>.
    /// Es la forma de saber si la DGI recibió un CFE cuyo envío terminó en error de comunicación.
    /// </summary>
    Task<EstadoComprobante> ConsultarCfeAsync(CfeEmitido cfe, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía el reporte diario (resumen firmado de montos, cantidades y rangos) de los CFE emitidos en
    /// <paramref name="fecha"/>, tomados del almacenamiento.
    /// </summary>
    /// <param name="fecha">Fecha de emisión a resumir (en general, <c>HoraUruguay.Hoy().AddDays(-1)</c>).</param>
    /// <param name="secEnvio">
    /// Secuencia del envío de esa fecha: 1 el primero; para corregir un reporte ya recibido se reenvía
    /// completo con la secuencia anterior + 1.
    /// </param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <exception cref="UruFacturaException">Si la fecha es futura o el almacenamiento no tiene completos los CFE de esa fecha.</exception>
    Task<AcuseReporte> EnviarReporteDiarioAsync(DateOnly fecha, int secEnvio = 1, CancellationToken cancellationToken = default);

    // --- Intercambio con otros emisores electrónicos ---

    /// <summary>
    /// Consulta en el padrón de la DGI si un RUT es emisor electrónico (y por lo tanto receptor electrónico, al
    /// que se le envían los CFE), con su correo y su servicio web de intercambio. <c>null</c> si no figura.
    /// </summary>
    /// <exception cref="ArgumentException">Si el RUT no es válido.</exception>
    Task<EmisorElectronico?> ConsultarEmisorElectronicoAsync(string rut, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arma el sobre para un receptor electrónico (<c>EnvioCFE_entreEmpresas</c>) con los CFE firmados y su
    /// adenda. Los CFE deben ser de un mismo receptor identificado con su RUT, no de contingencia (el CFC no se
    /// envía al receptor) y, salvo los e-Tickets, haberse enviado antes a la DGI.
    /// </summary>
    /// <exception cref="UruFacturaException">Si los CFE no se pueden enviar juntos a un receptor.</exception>
    /// <exception cref="InvalidOperationException">Si alguno todavía no se envió a la DGI.</exception>
    Task<SobreReceptor> GenerarSobreReceptorAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lee el acuse de recibo (<c>ACKSobre</c>) que un receptor electrónico envió por un sobre de este emisor;
    /// <see cref="AcuseSobre.IdEmisor"/> identifica el sobre.
    /// </summary>
    /// <exception cref="UruFacturaException">Si la firma no es válida o el acuse no es de un sobre de este emisor.</exception>
    AcuseSobre LeerAcuseReceptor(string xml);

    /// <summary>
    /// Lee la respuesta a los comprobantes (<c>ACKCFE</c>) que un receptor electrónico envió: la aceptación o el
    /// rechazo comercial de cada CFE del sobre (<see cref="EstadoEnvio.IdEmisor"/>). No cambia el estado ante la
    /// DGI: un CFE que la DGI recibió es válido y, si corresponde, se corrige con una nota.
    /// </summary>
    /// <exception cref="UruFacturaException">Si la firma no es válida o la respuesta no es de un sobre de este emisor.</exception>
    EstadoEnvio LeerRespuestaReceptor(string xml);

    /// <summary>
    /// Arma la comunicación firmada al receptor electrónico de los CFE que se le enviaron y la DGI rechazó, que
    /// quedan anulados (<see cref="CfeEmitido.AnuladoPorRechazo"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Si alguno no está anulado por un rechazo de la DGI.</exception>
    Task<NotificacionAnulacion> GenerarNotificacionAnulacionAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recibe un sobre de un proveedor (<c>EnvioCFE_entreEmpresas</c>): lo controla (carátula, RUT, certificado,
    /// cantidad, tamaño) y controla cada CFE (firma, formato, CAE), y devuelve sus CFE con el acuse de recibo
    /// firmado (<c>ACKSobre</c>) para devolverle. Los sobres repetidos (mismo RUT emisor y número) los detecta
    /// la aplicación, que es la que los conserva.
    /// </summary>
    /// <param name="xml">XML del sobre.</param>
    /// <param name="nombreArchivo">Nombre del archivo recibido por correo (<c>null</c> si llegó por servicio web).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <exception cref="UruFacturaException">Si no es XML o no tiene el RUT del emisor y el número de envío (no se puede responder).</exception>
    Task<SobreRecibido> RecibirSobreAsync(string xml, string? nombreArchivo = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Arma la respuesta firmada (<c>ACKCFE</c>) a los comprobantes de un sobre recibido: rechaza los que no
    /// pasaron los controles y los que indique <paramref name="rechazos"/> (motivos comerciales, por
    /// <see cref="CfeRecibido.NroOrdinal"/>), y acepta el resto.
    /// </summary>
    /// <exception cref="InvalidOperationException">Si el sobre fue rechazado.</exception>
    /// <exception cref="ArgumentException">Si un rechazo no corresponde a un CFE del sobre o su código no es de los que usa un receptor.</exception>
    Task<RespuestaCfes> ResponderCfesAsync(
        SobreRecibido sobre, IReadOnlyDictionary<int, IReadOnlyList<MotivoRechazo>>? rechazos = null, CancellationToken cancellationToken = default);

    /// <summary>Lee la comunicación de CFE anulados por rechazo de la DGI que envió un proveedor.</summary>
    /// <exception cref="UruFacturaException">Si la firma no es válida o no está dirigida a este receptor.</exception>
    IReadOnlyList<CfeAnulado> LeerNotificacionAnulacion(string xml);

    /// <summary>
    /// Envía un mensaje del intercambio (sobre, acuse, respuesta o comunicación de anulación) al servicio web de
    /// otro emisor (<see cref="EmisorElectronico.UrlServicioWeb"/>), con WS-Security.
    /// </summary>
    Task<RespuestaIntercambio> EnviarIntercambioAsync(
        string url, OperacionIntercambio operacion, string xml, CancellationToken cancellationToken = default);
}
