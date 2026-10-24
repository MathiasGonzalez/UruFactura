namespace UruFactura;

/// <summary>
/// La comunicación con los servicios de la DGI falló: error de red o timeout, SOAP Fault (por ejemplo,
/// firma WS-Security inválida) o respuesta HTTP sin un acuse interpretable. Los rechazos de la DGI
/// (sobre <c>BS</c>, reporte <c>BR</c>, comprobante <c>BE</c>) no son excepciones: vienen en el acuse.
/// </summary>
public class DgiCommunicationException : UruFacturaException
{
    /// <summary><c>SOAP_FAULT</c>, el código HTTP (p. ej. <c>502</c>) o <c>null</c> si no hubo respuesta.</summary>
    public string? CodigoRespuesta { get; }

    /// <summary>Cuerpo de la respuesta de la DGI, si lo hubo.</summary>
    public string? Respuesta { get; }

    public DgiCommunicationException(string message, string? codigoRespuesta = null, string? respuesta = null)
        : base(message)
    {
        CodigoRespuesta = codigoRespuesta;
        Respuesta = respuesta;
    }

    public DgiCommunicationException(string message, Exception innerException)
        : base(message, innerException) { }
}
