using System.Globalization;
using System.Xml;
using UruFactura.Exceptions;
using UruFactura.Models;

namespace UruFactura.Soap;

/// <summary>
/// Interpreta las respuestas de la DGI: el SOAP con <c>Dataout/xmlData</c> y los mensajes
/// <c>ACKSobre</c>, <c>ACKCFE</c>, <c>ACKRepDiario</c> y <c>Ackconsultaestadocfe</c>.
/// </summary>
internal static class DgiRespuestaParser
{
    private static XmlDocument Cargar(string xml)
    {
        var doc = new XmlDocument { XmlResolver = null };
        doc.LoadXml(xml);
        return doc;
    }

    private static XmlNode? Nodo(XmlNode origen, string nombreLocal) =>
        origen.SelectSingleNode($".//*[local-name()='{nombreLocal}']");

    private static string? Texto(XmlNode origen, string nombreLocal) =>
        Nodo(origen, nombreLocal)?.InnerText.Trim();

    /// <summary>Devuelve un <c>RespuestaDgi</c> de error si el SOAP es un Fault; si no, null.</summary>
    private static RespuestaDgi? ComoFault(XmlDocument doc, string xmlOriginal)
    {
        var fault = Nodo(doc, "Fault");
        if (fault is null)
            return null;

        var detalle = Texto(fault, "faultstring") ?? "Error SOAP sin detalle.";
        return RespuestaDgi.Error("SOAP_FAULT", detalle, xmlOriginal);
    }

    /// <summary>Extrae el XML embebido en <c>Dataout/xmlData</c> (texto o CDATA).</summary>
    private static XmlDocument? ContenidoDataout(XmlDocument soap)
    {
        var data = Nodo(soap, "xmlData");
        if (data is null || string.IsNullOrWhiteSpace(data.InnerText))
            return null;

        return Cargar(data.InnerText.Trim());
    }

    internal static RespuestaDgi ProcesarSobre(string soapXml)
    {
        try
        {
            var soap = Cargar(soapXml);
            if (ComoFault(soap, soapXml) is { } fault)
                return fault;

            var ack = ContenidoDataout(soap)
                ?? throw new DgiCommunicationException("La respuesta no contiene Dataout/xmlData.");

            var estado = Texto(ack, "Estado") ?? string.Empty;
            var respuesta = new RespuestaDgi
            {
                Codigo = estado,
                Exitoso = estado == "AS",
                XmlRespuesta = soapXml,
                IdReceptor = Texto(ack, "IDReceptor"),
                Token = Texto(ack, "Token"),
                ConsultarDesde = FechaHora(Texto(ack, "Fechahora")),
            };

            var motivos = Motivos(ack, "MotivosRechazo");
            respuesta.Mensaje = respuesta.Exitoso
                ? "Sobre recibido por la DGI. Consulte el estado de los comprobantes con el token."
                : $"Sobre rechazado ({estado}){(motivos.Count > 0 ? ": " + string.Join("; ", motivos) : ".")}";
            return respuesta;
        }
        catch (Exception ex) when (ex is not DgiCommunicationException)
        {
            throw new DgiCommunicationException("Error al procesar la respuesta de EFACRECEPCIONSOBRE.", ex);
        }
    }

    internal static RespuestaDgi ProcesarReporte(string soapXml)
    {
        try
        {
            var soap = Cargar(soapXml);
            if (ComoFault(soap, soapXml) is { } fault)
                return fault;

            var ack = ContenidoDataout(soap)
                ?? throw new DgiCommunicationException("La respuesta no contiene Dataout/xmlData.");

            var estado = Texto(ack, "Estado") ?? string.Empty;
            var motivos = Motivos(ack, "MotivosRechazo");
            var exitoso = estado == "AR";
            return new RespuestaDgi
            {
                Codigo = estado,
                Exitoso = exitoso,
                XmlRespuesta = soapXml,
                IdReceptor = Texto(ack, "IDReceptor"),
                Mensaje = exitoso
                    ? "Reporte diario recibido por la DGI."
                    : $"Reporte rechazado ({estado}){(motivos.Count > 0 ? ": " + string.Join("; ", motivos) : ".")}",
            };
        }
        catch (Exception ex) when (ex is not DgiCommunicationException)
        {
            throw new DgiCommunicationException("Error al procesar la respuesta de EFACRECEPCIONREPORTE.", ex);
        }
    }

    internal static RespuestaDgi ProcesarEstadoEnvio(string soapXml)
    {
        try
        {
            var soap = Cargar(soapXml);
            if (ComoFault(soap, soapXml) is { } fault)
                return fault;

            var ack = ContenidoDataout(soap)
                ?? throw new DgiCommunicationException("La respuesta no contiene Dataout/xmlData.");

            var comprobantes = new List<EstadoComprobante>();
            foreach (XmlNode det in ack.SelectNodes("//*[local-name()='ACKCFE_Det']")!)
            {
                comprobantes.Add(new EstadoComprobante
                {
                    TipoCfe = int.TryParse(Texto(det, "TipoCFE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) ? t : null,
                    Serie = Texto(det, "Serie"),
                    Numero = long.TryParse(Texto(det, "NroCFE"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null,
                    Estado = Texto(det, "Estado") ?? string.Empty,
                    Motivos = Motivos(det, "MotivosRechazoCF").Concat(Motivos(det, "ObsCFC")).ToList(),
                });
            }

            // Si la DGI todavía no procesó el sobre, la respuesta puede no traer detalle.
            if (comprobantes.Count == 0)
            {
                var estadoGlobal = Texto(ack, "Estado") ?? string.Empty;
                return new RespuestaDgi
                {
                    Codigo = estadoGlobal,
                    Exitoso = false,
                    XmlRespuesta = soapXml,
                    Mensaje = "La respuesta de la DGI no incluye el estado de comprobantes (¿el sobre aún se está procesando?).",
                };
            }

            var todosAceptados = comprobantes.All(c => c.Estado == "AE");
            var resumen = string.Join(", ", comprobantes
                .GroupBy(c => c.Estado)
                .Select(g => $"{g.Count()} {g.Key}"));

            return new RespuestaDgi
            {
                Codigo = todosAceptados ? "AE" : comprobantes.First(c => c.Estado != "AE").Estado,
                Exitoso = todosAceptados,
                XmlRespuesta = soapXml,
                Comprobantes = comprobantes,
                Mensaje = todosAceptados
                    ? "Todos los comprobantes fueron aceptados (AE)."
                    : $"Comprobantes: {resumen}. " + string.Join(" ", comprobantes.SelectMany(c => c.Motivos)),
            };
        }
        catch (Exception ex) when (ex is not DgiCommunicationException)
        {
            throw new DgiCommunicationException("Error al procesar la respuesta de EFACCONSULTARESTADOENVIO.", ex);
        }
    }

    internal static RespuestaDgi ProcesarEstadoCfe(string soapXml)
    {
        try
        {
            var soap = Cargar(soapXml);
            if (ComoFault(soap, soapXml) is { } fault)
                return fault;

            var estado = Texto(soap, "EstadoCFE")
                ?? throw new DgiCommunicationException("La respuesta no contiene EstadoCFE.");

            return new RespuestaDgi
            {
                Codigo = estado,
                Exitoso = estado == "AE",
                XmlRespuesta = soapXml,
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
        }
        catch (Exception ex) when (ex is not DgiCommunicationException)
        {
            throw new DgiCommunicationException("Error al procesar la respuesta de EFACCONSULTARESTADOCFE.", ex);
        }
    }

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

    private static DateTimeOffset? FechaHora(string? texto) =>
        DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var f) ? f : null;
}
