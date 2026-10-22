using System.Globalization;
using System.Xml;
using UruFactura.Exceptions;
using UruFactura.Models;
using UruFactura.Xml;

namespace UruFactura.Soap;

/// <summary>
/// Interpreta las respuestas de la DGI: el SOAP con <c>Dataout/xmlData</c> y los mensajes
/// <c>ACKSobre</c>, <c>ACKCFE</c>, <c>ACKRepDiario</c> y <c>Ackconsultaestadocfe</c>.
/// </summary>
internal static class DgiRespuestaParser
{
    private static XmlDocument Cargar(string xml) => XmlSeguro.Cargar(xml);

    private static XmlNode? Nodo(XmlNode origen, string nombreLocal) =>
        origen.SelectSingleNode($".//*[local-name()='{nombreLocal}']");

    private static string? Texto(XmlNode origen, string nombreLocal) =>
        Nodo(origen, nombreLocal)?.InnerText.Trim();

    /// <summary>
    /// Carga el SOAP y devuelve el Fault, si lo hay, o lo que <paramref name="interpretar"/> obtenga
    /// del contenido. Cualquier error se informa como <see cref="DgiCommunicationException"/>.
    /// </summary>
    private static RespuestaDgi Procesar(string soapXml, string operacion, Func<XmlDocument, RespuestaDgi> interpretar)
    {
        try
        {
            var soap = Cargar(soapXml);
            var respuesta = Fault(soap) ?? interpretar(soap);
            respuesta.XmlRespuesta = soapXml;
            return respuesta;
        }
        catch (Exception ex) when (ex is not DgiCommunicationException)
        {
            throw new DgiCommunicationException($"Error al procesar la respuesta de {operacion}.", ex);
        }
    }

    /// <summary>Un <c>RespuestaDgi</c> de error si el SOAP es un Fault; si no, null.</summary>
    private static RespuestaDgi? Fault(XmlDocument soap) =>
        Nodo(soap, "Fault") is { } fault
            ? RespuestaDgi.Error("SOAP_FAULT", Texto(fault, "faultstring") ?? "Error SOAP sin detalle.")
            : null;

    /// <summary>El ACK embebido en <c>Dataout/xmlData</c> (texto o CDATA).</summary>
    private static XmlDocument Ack(XmlDocument soap)
    {
        var data = Nodo(soap, "xmlData");
        if (data is null || string.IsNullOrWhiteSpace(data.InnerText))
            throw new DgiCommunicationException("La respuesta no contiene Dataout/xmlData.");

        return Cargar(data.InnerText.Trim());
    }

    private static string Rechazo(string documento, string estado, List<string> motivos) =>
        $"{documento} rechazado ({estado}){(motivos.Count > 0 ? ": " + string.Join("; ", motivos) : ".")}";

    internal static RespuestaDgi ProcesarSobre(string soapXml) => Procesar(soapXml, "EFACRECEPCIONSOBRE", soap =>
    {
        var ack = Ack(soap);
        var estado = Texto(ack, "Estado") ?? string.Empty;
        var exitoso = estado == "AS";
        return new RespuestaDgi
        {
            Codigo = estado,
            Exitoso = exitoso,
            IdReceptor = Texto(ack, "IDReceptor"),
            Token = Texto(ack, "Token"),
            ConsultarDesde = FechaHora(Texto(ack, "Fechahora")),
            Mensaje = exitoso
                ? "Sobre recibido por la DGI. Consulte el estado de los comprobantes con el token."
                : Rechazo("Sobre", estado, Motivos(ack, "MotivosRechazo")),
        };
    });

    internal static RespuestaDgi ProcesarReporte(string soapXml) => Procesar(soapXml, "EFACRECEPCIONREPORTE", soap =>
    {
        var ack = Ack(soap);
        var estado = Texto(ack, "Estado") ?? string.Empty;
        var exitoso = estado == "AR";
        return new RespuestaDgi
        {
            Codigo = estado,
            Exitoso = exitoso,
            IdReceptor = Texto(ack, "IDReceptor"),
            Mensaje = exitoso
                ? "Reporte diario recibido por la DGI."
                : Rechazo("Reporte", estado, Motivos(ack, "MotivosRechazo")),
        };
    });

    internal static RespuestaDgi ProcesarEstadoEnvio(string soapXml) => Procesar(soapXml, "EFACCONSULTARESTADOENVIO", soap =>
    {
        var ack = Ack(soap);
        var comprobantes = ack.SelectNodes("//*[local-name()='ACKCFE_Det']")!.Cast<XmlNode>()
            .Select(det => new EstadoComprobante
            {
                TipoCfe = int.TryParse(Texto(det, "TipoCFE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null,
                Serie = Texto(det, "Serie"),
                Numero = long.TryParse(Texto(det, "NroCFE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
                Estado = Texto(det, "Estado") ?? string.Empty,
                Motivos = [.. Motivos(det, "MotivosRechazoCF"), .. Motivos(det, "ObsCFC")],
            })
            .ToList();

        // Si la DGI todavía no procesó el sobre, la respuesta puede no traer detalle.
        if (comprobantes.Count == 0)
            return new RespuestaDgi
            {
                Codigo = Texto(ack, "Estado") ?? string.Empty,
                Mensaje = "La respuesta de la DGI no incluye el estado de comprobantes (¿el sobre aún se está procesando?).",
            };

        var rechazado = comprobantes.FirstOrDefault(c => c.Estado != "AE");
        var resumen = string.Join(", ", comprobantes.GroupBy(c => c.Estado).Select(g => $"{g.Count()} {g.Key}"));
        return new RespuestaDgi
        {
            Codigo = rechazado?.Estado ?? "AE",
            Exitoso = rechazado is null,
            Comprobantes = comprobantes,
            Mensaje = rechazado is null
                ? "Todos los comprobantes fueron aceptados (AE)."
                : $"Comprobantes: {resumen}. " + string.Join(" ", comprobantes.SelectMany(c => c.Motivos)),
        };
    });

    internal static RespuestaDgi ProcesarEstadoCfe(string soapXml) => Procesar(soapXml, "EFACCONSULTARESTADOCFE", soap =>
    {
        var estado = Texto(soap, "EstadoCFE")
            ?? throw new DgiCommunicationException("La respuesta no contiene EstadoCFE.");

        return new RespuestaDgi
        {
            Codigo = estado,
            Exitoso = estado == "AE",
            IdReceptor = Texto(soap, "IdReceptor"),
            Token = Texto(soap, "Token"),
            ConsultarDesde = FechaHora(Texto(soap, "Fechahora")),
            Mensaje = estado switch
            {
                "AE" => "Comprobante recibido (AE).",
                "BE" => "Comprobante rechazado (BE).",
                "CE" => "Comprobante observado (CE).",
                "FE" => "Comprobante reliquidado (FE).",
                _ => $"Estado del comprobante: {estado}.",
            },
        };
    });

    /// <summary>Códigos y glosas de los motivos de rechazo/observación bajo <paramref name="contenedor"/>.</summary>
    private static List<string> Motivos(XmlNode origen, string contenedor)
    {
        var lista = new List<string>();
        foreach (XmlNode nodo in origen.SelectNodes($".//*[local-name()='{contenedor}']")!)
        {
            var motivo = Texto(nodo, "Motivo");
            var glosa = Texto(nodo, "Glosa");
            if (!string.IsNullOrEmpty(motivo) || !string.IsNullOrEmpty(glosa))
                lista.Add(string.IsNullOrEmpty(glosa) ? motivo! : $"{motivo} {glosa}".Trim());
        }
        return lista;
    }

    /// <summary>
    /// Fecha y hora de la DGI. Si no trae offset es hora de Uruguay; asumir la zona del servidor
    /// (en general UTC) la correría 3 horas.
    /// </summary>
    internal static DateTimeOffset? FechaHora(string? texto)
    {
        if (!DateTime.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fecha))
            return null;

        return fecha.Kind == DateTimeKind.Unspecified
            ? new DateTimeOffset(fecha, HoraUruguay.Offset)
            : DateTimeOffset.Parse(texto!, CultureInfo.InvariantCulture);
    }
}
