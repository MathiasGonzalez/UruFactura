using UruFactura.Models;

namespace UruFactura.Soap;

/// <summary>
/// Cliente de los servicios web de la DGI (<c>ws_efactura</c> y <c>ws_consultas</c>),
/// protegidos con WS-Security.
/// </summary>
public interface IDgiSoapClient : IDisposable
{
    /// <summary>
    /// Envía un sobre <c>EnvioCFE</c> (operación <c>EFACRECEPCIONSOBRE</c>).
    /// La respuesta es un <c>ACKSobre</c>: si fue recibido, trae el token para consultar el
    /// resultado de cada comprobante.
    /// </summary>
    /// <param name="sobreXml">XML del sobre (ver <see cref="Xml.CfeSobreBuilder"/>).</param>
    Task<RespuestaDgi> EnviarSobreAsync(string sobreXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el resultado de un sobre enviado (operación <c>EFACCONSULTARESTADOENVIO</c>).
    /// </summary>
    /// <param name="idReceptor">Número asignado por la DGI al envío (<see cref="RespuestaDgi.IdReceptor"/>).</param>
    /// <param name="token">Token del envío (<see cref="RespuestaDgi.Token"/>).</param>
    Task<RespuestaDgi> ConsultarEstadoEnvioAsync(
        string idReceptor,
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consulta el estado de un CFE por tipo, serie y número (operación
    /// <c>EFACCONSULTARESTADOCFE</c> de <c>ws_consultas</c>).
    /// </summary>
    Task<RespuestaDgi> ConsultarEstadoCfeAsync(
        int tipoCfe,
        string serie,
        long numero,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envía el reporte diario firmado (operación <c>EFACRECEPCIONREPORTE</c>).
    /// </summary>
    /// <param name="reporteFirmadoXml">XML <c>Reporte</c> firmado (ver <see cref="Xml.ReporteDiarioBuilder"/>).</param>
    Task<RespuestaDgi> EnviarReporteDiarioAsync(
        string reporteFirmadoXml,
        CancellationToken cancellationToken = default);

    /// <summary>Reemplaza el <see cref="HttpClient"/> usado para las llamadas.</summary>
    IDgiSoapClient WithHttpClient(HttpClient httpClient);
}
