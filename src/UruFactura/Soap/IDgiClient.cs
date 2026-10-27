namespace UruFactura;

/// <summary>
/// Transporte hacia los servicios web de la DGI (<c>ws_efactura</c> y <c>ws_consultas</c>) y hacia los
/// servicios de intercambio de otros emisores electrónicos (<c>ws_efactura_intercambio</c>). El cliente
/// usa por defecto uno SOAP con WS-Security; implementar esta interfaz permite reemplazarlo (por
/// ejemplo, en tests o detrás de un proxy propio).
/// </summary>
/// <remarks>
/// Los rechazos de la DGI se informan en el resultado; los errores de comunicación (red, timeout,
/// SOAP Fault, respuesta HTTP sin acuse) se lanzan como <see cref="DgiCommunicationException"/>.
/// </remarks>
public interface IDgiClient
{
    /// <summary>Envía un sobre <c>EnvioCFE</c> firmado (<c>EFACRECEPCIONSOBRE</c>).</summary>
    Task<AcuseSobre> EnviarSobreAsync(string sobreXml, CancellationToken cancellationToken = default);

    /// <summary>Consulta el resultado de un sobre (<c>EFACCONSULTARESTADOENVIO</c>).</summary>
    Task<EstadoEnvio> ConsultarEnvioAsync(string idReceptor, string token, CancellationToken cancellationToken = default);

    /// <summary>Consulta el estado de un CFE por tipo, serie y número (<c>EFACCONSULTARESTADOCFE</c>).</summary>
    Task<EstadoComprobante> ConsultarCfeAsync(TipoCfe tipo, string serie, long numero, CancellationToken cancellationToken = default);

    /// <summary>Envía el reporte diario firmado (<c>EFACRECEPCIONREPORTE</c>).</summary>
    Task<AcuseReporte> EnviarReporteAsync(string reporteFirmadoXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el padrón de emisores electrónicos (<c>EFACCONSULTARRUCEMISORESTRANSICIONCOMPRIMIDOPROV</c>):
    /// los RUT que contienen <paramref name="filtroRut"/>, con su correo y servicio web de intercambio.
    /// </summary>
    Task<IReadOnlyList<EmisorElectronico>> ConsultarEmisoresAsync(string filtroRut, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía un mensaje al servicio web de intercambio de otro emisor electrónico (<c>ws_efactura_intercambio</c>):
    /// un sobre, un acuse, una respuesta a comprobantes o una comunicación de anulación.
    /// </summary>
    /// <param name="url">URL del servicio del otro emisor (la del padrón de emisores).</param>
    /// <param name="operacion">Operación.</param>
    /// <param name="xml">Mensaje (sobre o mensaje firmado).</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    Task<RespuestaIntercambio> EnviarIntercambioAsync(
        string url, OperacionIntercambio operacion, string xml, CancellationToken cancellationToken = default);
}
