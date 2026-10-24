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

    private static string Rechazo(string documento, string? codigo, List<string> motivos) =>
        $"{documento} rechazado ({codigo}){(motivos.Count > 0 ? ": " + string.Join("; ", motivos) : ".")}";

    // Estados de EstadoACKSobreType, EstadoACKRepType y EstadoACKCFEType (DGITypes.xsd): un código
    // fuera del esquema no es un resultado que se pueda interpretar.

    private static EstadoSobre EstadoSobreDe(string? codigo) => codigo switch
    {
        "AS" => EstadoSobre.Recibido,
        "BS" => EstadoSobre.Rechazado,
        "BA" => EstadoSobre.ArchivoRechazado,
        _ => throw new DgiCommunicationException($"Estado de ACKSobre desconocido: '{codigo}'."),
    };

    private static EstadoReporte EstadoReporteDe(string? codigo) => codigo switch
    {
        "AR" => EstadoReporte.Recibido,
        "BR" => EstadoReporte.Rechazado,
        "BA" => EstadoReporte.ArchivoRechazado,
        _ => throw new DgiCommunicationException($"Estado de ACKRepDiario desconocido: '{codigo}'."),
    };

    private static EstadoCfe EstadoCfeDe(string? codigo) => codigo switch
    {
        "AE" => EstadoCfe.Aceptado,
        "BE" => EstadoCfe.Rechazado,
        "CE" => EstadoCfe.Observado,
        "FE" => EstadoCfe.Reliquidado,
        _ => throw new DgiCommunicationException($"Estado de comprobante desconocido: '{codigo}'."),
    };

    internal static AcuseSobre ProcesarSobre(string soapXml) => Procesar(soapXml, "EFACRECEPCIONSOBRE", soap =>
    {
        var ack = Ack(soap);
        var codigo = Texto(ack, "Estado");
        var estado = EstadoSobreDe(codigo);
        var motivos = Motivos(ack, "MotivosRechazo");
        return new AcuseSobre
        {
            Estado = estado,
            Motivos = motivos,
            IdReceptor = Texto(ack, "IDReceptor"),
            Token = Texto(ack, "Token"),
            ConsultarDesde = FechaHora(Texto(ack, "Fechahora")),
            Mensaje = estado == EstadoSobre.Recibido
                ? "Sobre recibido por la DGI. El resultado de cada comprobante se consulta con ConsultarEnvioAsync."
                : Rechazo("Sobre", codigo, motivos),
            XmlRespuesta = soapXml,
        };
    });

    internal static AcuseReporte ProcesarReporte(string soapXml) => Procesar(soapXml, "EFACRECEPCIONREPORTE", soap =>
    {
        var ack = Ack(soap);
        var codigo = Texto(ack, "Estado");
        var estado = EstadoReporteDe(codigo);
        var motivos = Motivos(ack, "MotivosRechazo");
        return new AcuseReporte
        {
            Estado = estado,
            Motivos = motivos,
            IdReceptor = Texto(ack, "IDReceptor"),
            Mensaje = estado == EstadoReporte.Recibido ? "Reporte diario recibido por la DGI." : Rechazo("Reporte", codigo, motivos),
            XmlRespuesta = soapXml,
        };
    });

    internal static EstadoEnvio ProcesarEstadoEnvio(string soapXml) => Procesar(soapXml, "EFACCONSULTARESTADOENVIO", soap =>
    {
        var ack = Ack(soap);
        var comprobantes = ack.SelectNodes("//*[local-name()='ACKCFE_Det']")!.Cast<XmlNode>()
            .Select(det => new EstadoComprobante
            {
                // Nro_ordinal es lo único obligatorio del detalle: con él se asocia el resultado al CFE.
                NroOrdinal = Entero(Texto(det, "Nro_ordinal")) is { } ordinal ? (int)ordinal : null,
                TipoCfe = Entero(Texto(det, "TipoCFE")) is { } tipo ? (int)tipo : null,
                Serie = Texto(det, "Serie"),
                Numero = Entero(Texto(det, "NroCFE")),
                Estado = EstadoCfeDe(Texto(det, "Estado")),
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
            Estado = EstadoCfeDe(Texto(soap, "EstadoCFE") ?? throw new DgiCommunicationException("La respuesta no contiene EstadoCFE.")),
            Motivos = Motivos(soap, "MotivosRechazoCF"),
            XmlRespuesta = soapXml,
        });

    private static long? Entero(string? texto) =>
        long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) ? valor : null;

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
