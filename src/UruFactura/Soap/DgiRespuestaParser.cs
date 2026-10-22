using System.Globalization;
using System.Xml;
using UruFactura.Xml;

namespace UruFactura.Soap;

/// <summary>
/// Interpreta las respuestas de la DGI: el SOAP con <c>Dataout/xmlData</c> y los mensajes
/// <c>ACKSobre</c>, <c>ACKCFE</c>, <c>ACKRepDiario</c> y <c>Ackconsultaestadocfe</c>.
/// Un SOAP Fault o una respuesta que no se puede interpretar se lanza como <see cref="DgiCommunicationException"/>.
/// </summary>
internal static class DgiRespuestaParser
{
    private static XmlNode? Nodo(XmlNode origen, string nombreLocal) =>
        origen.SelectSingleNode($".//*[local-name()='{nombreLocal}']");

    private static string? Texto(XmlNode origen, string nombreLocal) =>
        Nodo(origen, nombreLocal)?.InnerText.Trim();

    /// <summary>Carga el SOAP, lanza su Fault si lo tiene y si no aplica <paramref name="interpretar"/>.</summary>
    private static T Procesar<T>(string soapXml, string operacion, Func<XmlDocument, T> interpretar)
    {
        XmlDocument soap;
        try
        {
            soap = XmlSeguro.Cargar(soapXml);
        }
        catch (Exception ex)
        {
            throw new DgiCommunicationException($"La respuesta de {operacion} no es XML válido.", ex);
        }

        if (Nodo(soap, "Fault") is { } fault)
            throw new DgiCommunicationException(
                $"La DGI respondió con un SOAP Fault en {operacion}: {Texto(fault, "faultstring") ?? "sin detalle"}.",
                "SOAP_FAULT", soapXml);

        try
        {
            return interpretar(soap);
        }
        catch (Exception ex) when (ex is not DgiCommunicationException)
        {
            throw new DgiCommunicationException($"Error al procesar la respuesta de {operacion}.", ex);
        }
    }

    /// <summary>El ACK embebido en <c>Dataout/xmlData</c> (texto o CDATA).</summary>
    private static XmlDocument Ack(XmlDocument soap)
    {
        var data = Nodo(soap, "xmlData");
        if (data is null || string.IsNullOrWhiteSpace(data.InnerText))
            throw new DgiCommunicationException("La respuesta no contiene Dataout/xmlData.");

        return XmlSeguro.Cargar(data.InnerText.Trim());
    }

    private static string Rechazo(string documento, string estado, List<string> motivos) =>
        $"{documento} rechazado ({estado}){(motivos.Count > 0 ? ": " + string.Join("; ", motivos) : ".")}";

    internal static AcuseSobre ProcesarSobre(string soapXml) => Procesar(soapXml, "EFACRECEPCIONSOBRE", soap =>
    {
        var ack = Ack(soap);
        var estado = Texto(ack, "Estado") ?? string.Empty;
        var motivos = Motivos(ack, "MotivosRechazo");
        return new AcuseSobre
        {
            Estado = estado,
            Motivos = motivos,
            IdReceptor = Texto(ack, "IDReceptor"),
            Token = Texto(ack, "Token"),
            ConsultarDesde = FechaHora(Texto(ack, "Fechahora")),
            Mensaje = estado == "AS"
                ? "Sobre recibido por la DGI. El resultado de cada comprobante se consulta con ConsultarEnvioAsync."
                : Rechazo("Sobre", estado, motivos),
            XmlRespuesta = soapXml,
        };
    });

    internal static AcuseReporte ProcesarReporte(string soapXml) => Procesar(soapXml, "EFACRECEPCIONREPORTE", soap =>
    {
        var ack = Ack(soap);
        var estado = Texto(ack, "Estado") ?? string.Empty;
        var motivos = Motivos(ack, "MotivosRechazo");
        return new AcuseReporte
        {
            Estado = estado,
            Motivos = motivos,
            IdReceptor = Texto(ack, "IDReceptor"),
            Mensaje = estado == "AR" ? "Reporte diario recibido por la DGI." : Rechazo("Reporte", estado, motivos),
            XmlRespuesta = soapXml,
        };
    });

    internal static EstadoEnvio ProcesarEstadoEnvio(string soapXml) => Procesar(soapXml, "EFACCONSULTARESTADOENVIO", soap =>
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

        var resumen = string.Join(", ", comprobantes.GroupBy(c => c.Estado).Select(g => $"{g.Count()} {g.Key}"));
        return new EstadoEnvio
        {
            Comprobantes = comprobantes,
            // Si la DGI todavía no procesó el sobre, la respuesta puede no traer detalle.
            Mensaje = comprobantes.Count == 0
                ? "La DGI todavía no informó el estado de los comprobantes (el sobre se está procesando)."
                : comprobantes.All(c => c.Aceptado)
                    ? "Todos los comprobantes fueron aceptados (AE)."
                    : $"Comprobantes: {resumen}. " + string.Join(" ", comprobantes.SelectMany(c => c.Motivos)),
            XmlRespuesta = soapXml,
        };
    });

    internal static EstadoComprobante ProcesarEstadoCfe(string soapXml) => Procesar(soapXml, "EFACCONSULTARESTADOCFE", soap =>
        new EstadoComprobante
        {
            Estado = Texto(soap, "EstadoCFE") ?? throw new DgiCommunicationException("La respuesta no contiene EstadoCFE."),
            Motivos = Motivos(soap, "MotivosRechazoCF"),
            XmlRespuesta = soapXml,
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
