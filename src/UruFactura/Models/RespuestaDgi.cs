namespace UruFactura.Models;

/// <summary>
/// Resultado de una operación contra los servicios web de la DGI.
/// </summary>
public class RespuestaDgi
{
    /// <summary>
    /// Código de estado devuelto por la DGI: <c>AS</c>/<c>BS</c>/<c>BA</c> (sobre),
    /// <c>AR</c>/<c>BR</c>/<c>BA</c> (reporte), <c>AE</c>/<c>BE</c>/<c>CE</c>/<c>FE</c> (comprobante),
    /// <c>SOAP_FAULT</c> o el código HTTP cuando no hubo respuesta SOAP.
    /// </summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Mensaje descriptivo de la respuesta (incluye los motivos de rechazo, si los hay).</summary>
    public string Mensaje { get; set; } = string.Empty;

    /// <summary>
    /// Indica si la DGI aceptó el envío: sobre/reporte recibido (<c>AS</c>/<c>AR</c>),
    /// o todos los comprobantes aceptados (<c>AE</c>).
    /// Un sobre recibido todavía debe consultarse con el <see cref="Token"/> para conocer
    /// el resultado de cada CFE.
    /// </summary>
    public bool Exitoso { get; set; }

    /// <summary>Timestamp de la respuesta.</summary>
    public DateTime FechaRespuesta { get; set; } = DateTime.UtcNow;

    /// <summary>XML de respuesta en bruto (opcional).</summary>
    public string? XmlRespuesta { get; set; }

    /// <summary>Número asignado por la DGI al envío (<c>IDReceptor</c>); se usa para consultar el estado.</summary>
    public string? IdReceptor { get; set; }

    /// <summary>Token para consultar el estado del envío (<c>ParamConsulta/Token</c>).</summary>
    public string? Token { get; set; }

    /// <summary>Fecha y hora a partir de la cual se puede consultar el resultado del envío.</summary>
    public DateTimeOffset? ConsultarDesde { get; set; }

    /// <summary>Estado de cada comprobante del sobre (solo en la consulta del estado del envío).</summary>
    public List<EstadoComprobante> Comprobantes { get; set; } = [];

    public static RespuestaDgi Exito(string codigo, string mensaje, string? xmlRespuesta = null) =>
        new() { Codigo = codigo, Mensaje = mensaje, Exitoso = true, XmlRespuesta = xmlRespuesta };

    public static RespuestaDgi Error(string codigo, string mensaje, string? xmlRespuesta = null) =>
        new() { Codigo = codigo, Mensaje = mensaje, Exitoso = false, XmlRespuesta = xmlRespuesta };
}

/// <summary>Estado de un comprobante informado por la DGI.</summary>
public class EstadoComprobante
{
    /// <summary>Tipo de CFE (101, 111, ...).</summary>
    public int? TipoCfe { get; set; }

    /// <summary>Serie del comprobante.</summary>
    public string? Serie { get; set; }

    /// <summary>Número del comprobante.</summary>
    public long? Numero { get; set; }

    /// <summary><c>AE</c> recibido, <c>BE</c> rechazado, <c>CE</c> observado, <c>FE</c> reliquidado.</summary>
    public string Estado { get; set; } = string.Empty;

    /// <summary>Motivos de rechazo u observaciones (códigos y glosas), si los hay.</summary>
    public List<string> Motivos { get; set; } = [];
}
