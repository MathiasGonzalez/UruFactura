using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using UruFactura.Formatting;
using UruFactura.Soap;
using static UruFactura.Xml.XmlCfe;

namespace UruFactura.Xml;

/// <summary>
/// Mensajes del intercambio de CFE entre emisores electrónicos (formato del sobre v05 y del mensaje de
/// respuesta v19): lectura y control de un sobre recibido (<c>EnvioCFE_entreEmpresas</c>), acuse del sobre
/// (<c>ACKSobre</c>), respuesta a los comprobantes (<c>ACKCFE</c>, <c>ACKCFE_Partes.xsd</c>), comunicación
/// de CFE anulados por rechazo de la DGI (<c>CFEs_Anulados_x_RechazoDGI</c>) y lectura de las respuestas
/// de los receptores.
/// </summary>
internal static class Intercambio
{
    /// <summary>Motivos de rechazo de un sobre que usa el receptor (tabla 4.2; S08 es solo de la DGI).</summary>
    internal static readonly IReadOnlyDictionary<string, string> MotivosSobre = new Dictionary<string, string>
    {
        ["S01"] = "Formato del archivo no es el indicado",
        ["S02"] = "No coincide RUC de Sobre, Certificado, envío o CFE",
        ["S03"] = "Certificado electrónico no es válido",
        ["S04"] = "No cumple validaciones según Formato de sobre",
        ["S05"] = "No coinciden cantidad CFE de carátula y contenido",
        ["S06"] = "No coinciden certificado de sobre y comprobantes",
        ["S07"] = "Sobre enviado supera el tamaño máximo admitido",
        ["S20"] = "Sobre enviado ya existe en los registros del receptor",
    };

    /// <summary>Glosas de los motivos de rechazo de un CFE que puede usar el receptor (tabla 4.3).</summary>
    internal static readonly IReadOnlyDictionary<string, string> MotivosCfe = new Dictionary<string, string>
    {
        ["E02"] = "Tipo y Nº de CFE ya existe en los registros",
        ["E03"] = "Tipo y Nº de CFE no se corresponden con el CAE",
        ["E04"] = "Firma electrónica no es válida",
        ["E05"] = "No cumple validaciones de Formato comprobantes",
        ["E06"] = "Importe informado en el CFE excede montos habituales",
        ["E07"] = "Fecha Firma de CFE no se corresponde con fecha CAE",
        ["E10"] = "Tipo de cambio no válido",
        ["E11"] = "Tipo de CAE especial del CFE no se corresponde con el CAE",
        ["E12"] = "Información en Zona de Referencia no existe en los registros",
    };

    /// <summary>Indica si un receptor puede rechazar un CFE con el código: E02 a E07, E10 a E12 y los comerciales E20 a E60.</summary>
    internal static bool EsMotivoDeReceptor(string? codigo) =>
        codigo is { Length: 3 } && codigo[0] == 'E' && int.TryParse(codigo.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
        && n is >= 2 and <= 7 or >= 10 and <= 12 or >= 20 and <= 60;

    // -------------------------------------------------------------------------
    // Sobre recibido
    // -------------------------------------------------------------------------

    /// <summary>Lo que se lee de un sobre recibido: su carátula, los motivos de rechazo del sobre y los CFE controlados.</summary>
    internal sealed record SobreLeido(
        string RutEmisor, long IdEmisor, DateTimeOffset? Fecha, int Cantidad, List<MotivoRechazo> Motivos, List<CfeRecibido> Comprobantes);

    /// <summary>
    /// Lee y controla un sobre <c>EnvioCFE_entreEmpresas</c> dirigido a <paramref name="rutPropio"/>. Si el
    /// sobre tiene motivos de rechazo (S01 a S07) no se procesan sus CFE.
    /// </summary>
    /// <exception cref="UruFacturaException">
    /// Si no es XML o su carátula no tiene el RUT del emisor y el número de envío: sin ellos no se puede responder.
    /// </exception>
    internal static SobreLeido LeerSobre(string xml, string rutPropio)
    {
        XmlDocument doc;
        try
        {
            // Viene de un tercero: se carga sin DTD, y conservando los espacios de los CFE firmados.
            doc = XmlSeguro.Cargar(xml, preservarEspacios: true);
        }
        catch (Exception ex)
        {
            throw new UruFacturaException("El sobre recibido no es un XML válido: no se puede responder (S01).", ex);
        }

        var raiz = doc.DocumentElement!;
        var caratula = Hijo(raiz, "Caratula");
        var rutEmisor = Texto(caratula, "RUCEmisor");
        var numeroEnvio = Entero(Texto(caratula, "Idemisor"));
        if (caratula is null || !Rut.EsValido(rutEmisor) || numeroEnvio is not (>= 0 and <= 9_999_999_999))
            throw new UruFacturaException(
                "El sobre recibido no tiene una carátula con el RUT del emisor y el número de envío: no se puede responder (S01).");
        var idEmisor = numeroEnvio.Value;

        var detalles = new Dictionary<string, List<string>>();
        void Motivo(string codigo, string detalle)
        {
            if (!detalles.TryGetValue(codigo, out var lista))
                detalles[codigo] = lista = [];
            lista.Add(detalle);
        }

        if (raiz.LocalName != "EnvioCFE_entreEmpresas" || raiz.NamespaceURI != CfeXml.NsUri)
            Motivo("S01", $"Se esperaba un sobre EnvioCFE_entreEmpresas y llegó {raiz.LocalName}.");
        if (Encoding.UTF8.GetByteCount(xml) > CfeSobre.TamanoMaximo)
            Motivo("S07", $"Supera los {CfeSobre.TamanoMaximo / 1_000_000} MB.");

        var fecha = DgiRespuestaParser.FechaHora(Texto(caratula, "Fecha"));
        var cantidad = Entero(Texto(caratula, "CantCFE"));
        if (raiz.GetAttribute("version") != "1.0" || caratula.GetAttribute("version") != "1.0")
            Motivo("S04", "La versión del sobre o de la carátula no es 1.0.");
        if (fecha is null)
            Motivo("S04", "La carátula no tiene una fecha válida.");

        var rutReceptor = Texto(caratula, "RutReceptor");
        if (rutReceptor != rutPropio)
            Motivo("S02", $"El sobre está dirigido al RUT {rutReceptor}.");

        var items = raiz.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "CFE_Adenda" && e.NamespaceURI == CfeXml.NsUri).ToList();
        if (cantidad != items.Count)
            Motivo("S05", $"La carátula indica {cantidad?.ToString(CultureInfo.InvariantCulture) ?? "(sin cantidad)"} CFE y el sobre trae {items.Count}.");
        if (items.Count is < 1 or > CfeSobre.MaxCfePorSobre)
            Motivo("S04", $"Un sobre trae de 1 a {CfeSobre.MaxCfePorSobre} CFE.");

        var certificado = string.Concat((Texto(caratula, "X509Certificate") ?? "").Where(c => !char.IsWhiteSpace(c)));
        X509Certificate2? cert = null;
        try
        {
            cert = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificado));
            if (fecha is { } f && (f < cert.NotBefore || f > cert.NotAfter))
                Motivo("S03", $"El certificado no estaba vigente el {f.ToOffset(HoraUruguay.Offset):dd/MM/yyyy}.");
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException)
        {
            Motivo("S03", "El certificado de la carátula no se puede leer.");
        }
        using var certificadoCaratula = cert;

        var cfes = new List<(XmlElement Cfe, XmlElement? Adenda)>();
        for (var i = 0; i < items.Count; i++)
        {
            if (Hijo(items[i], "CFE") is not { } cfe)
            {
                Motivo("S04", $"El elemento {i + 1} no tiene CFE.");
                continue;
            }
            cfes.Add((cfe, Hijo(items[i], "Adenda")));

            if (Texto(Hijo(cfe, "Emisor", profundo: true), "RUCEmisor") is { } rucCfe && rucCfe != rutEmisor)
                Motivo("S02", $"El CFE {i + 1} es del RUT {rucCfe}.");
            if (Texto(Hijo(cfe, "Receptor", profundo: true), "DocRecep") is { } documento && documento != rutPropio)
                Motivo("S02", $"El CFE {i + 1} está dirigido al documento {documento}.");
            if (CfeSobre.CertificadoDeLaFirma(cfe) is { } certificadoCfe ? certificadoCfe != certificado
                : certificadoCaratula is not null && SerialDeLaFirma(cfe) is { } serial && !FirmaDigital.MismoSerial(serial, certificadoCaratula))
                Motivo("S06", $"El CFE {i + 1} está firmado con otro certificado.");
        }

        var motivos = detalles
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .Select(d => new MotivoRechazo(d.Key, MotivosSobre[d.Key], Recortar(string.Join(" ", d.Value), 500)))
            .ToList();
        if (motivos.Count > 0)
            return new SobreLeido(rutEmisor!, idEmisor, fecha, items.Count, motivos, []);

        var vistos = new HashSet<(string?, string?, long?)>();
        return new SobreLeido(rutEmisor!, idEmisor, fecha, items.Count, motivos,
            [.. cfes.Select((c, i) => LeerCfe(c.Cfe, c.Adenda, i + 1, vistos, certificadoCaratula))]);
    }

    /// <summary>El número de serie (decimal) de <c>X509IssuerSerial</c> en la firma del CFE, o <c>null</c> si no lo indica.</summary>
    private static string? SerialDeLaFirma(XmlNode cfe) =>
        cfe.SelectSingleNode($".//*[local-name()='X509SerialNumber' and namespace-uri()='{SignedXml.XmlDsigNamespaceUrl}']")?.InnerText.Trim();

    /// <summary>
    /// Lee un CFE del sobre y lo controla: firma (E04), formato básico (E05), número dentro del CAE (E03),
    /// firma dentro de la vigencia del CAE (E07) y repetidos en el sobre (E02). Una firma que no incluye el
    /// certificado se verifica con el de la carátula.
    /// </summary>
    private static CfeRecibido LeerCfe(
        XmlElement cfe, XmlElement? adenda, int ordinal, HashSet<(string?, string?, long?)> vistos, X509Certificate2? certificadoCaratula)
    {
        // El CFE como documento propio, tal como se firmó.
        var propio = new XmlDocument { PreserveWhitespace = true };
        propio.AppendChild(propio.ImportNode(cfe, true));
        var xml = propio.OuterXml;

        var idDoc = Hijo(cfe, "IdDoc", profundo: true);
        var caeData = Hijo(cfe, "CAEData", profundo: true);
        var totales = Hijo(cfe, "Totales", profundo: true);
        var tipo = Entero(Texto(idDoc, "TipoCFE"));
        var serie = Texto(idDoc, "Serie");
        var numero = Entero(Texto(idDoc, "Nro"));
        var fechaEmision = Fecha(Texto(idDoc, "FchEmis"));
        var firma = DgiRespuestaParser.FechaHora(Texto(Hijo(cfe, "TmstFirma", profundo: true), null));

        var motivos = new List<MotivoRechazo>();
        void Motivo(string codigo, string? detalle = null) => motivos.Add(new MotivoRechazo(codigo, MotivosCfe[codigo], detalle));

        var firmante = FirmaDigital.VerificarFirma(xml, certificadoCaratula);
        if (firmante is null)
            Motivo("E04");
        firmante?.Dispose();

        if (tipo is null || serie is null || numero is null || fechaEmision is null || firma is null)
            Motivo("E05", "Faltan el tipo, la serie, el número, la fecha o la fecha de firma.");
        if (numero is { } nro && (Entero(Texto(caeData, "DNro")) is not { } desde || Entero(Texto(caeData, "HNro")) is not { } hasta
                                  || nro < desde || nro > hasta))
            Motivo("E03", "El número no está en el rango del CAE.");
        if (firma is { } tmst && Fecha(Texto(caeData, "FecVenc")) is { } vencimiento
            && DateOnly.FromDateTime(tmst.ToOffset(HoraUruguay.Offset).DateTime) > vencimiento)
            Motivo("E07", "Se firmó después del vencimiento del CAE.");
        if (!vistos.Add((Texto(idDoc, "TipoCFE"), serie, numero)))
            Motivo("E02", "Está repetido en el sobre.");

        return new CfeRecibido
        {
            NroOrdinal = ordinal,
            Xml = xml,
            Adenda = adenda is null ? null
                : adenda.ChildNodes.OfType<XmlElement>().Any() ? adenda.InnerXml : adenda.InnerText,
            RutEmisor = Texto(Hijo(cfe, "Emisor", profundo: true), "RUCEmisor"),
            Tipo = tipo is { } t && t <= int.MaxValue ? (TipoCfe)(int)t : null,
            Serie = serie,
            Numero = numero,
            FechaEmision = fechaEmision,
            FechaFirma = firma,
            Moneda = Texto(totales, "TpoMoneda"),
            Monto = Decimal(Texto(totales, "MntPagar") ?? Texto(totales, "MntTotal") ?? Texto(totales, "MntTotRetenido")),
            Estado = motivos.Count == 0 ? EstadoCfe.Aceptado : EstadoCfe.Rechazado,
            Motivos = motivos,
        };
    }

    // -------------------------------------------------------------------------
    // Mensajes del receptor
    // -------------------------------------------------------------------------

    /// <summary>Acuse de recibo de un sobre (<c>ACKSobre</c>, sin firmar): recibido (AS) o rechazado (BS) con sus motivos.</summary>
    internal static string AcuseSobre(
        string rutPropio, SobreLeido sobre, long idRespuesta, long idReceptor, string? nombreArchivo, DateTimeOffset recibido, DateTimeOffset tmst) =>
        Escribir(w =>
        {
            w.WriteStartDocument();
            w.WriteStartElement("ACKSobre", CfeXml.NsUri);
            w.WriteAttributeString("version", "1.0");

            w.WriteStartElement("Caratula", CfeXml.NsUri);
            El(w, "RUCReceptor", rutPropio);
            El(w, "RUCEmisor", sobre.RutEmisor);
            El(w, "IDRespuesta", Numero(idRespuesta));
            El(w, "NomArch", Recortar(nombreArchivo ?? "", 40));
            El(w, "FecHRecibido", CfeXml.TimestampUruguay(recibido));
            El(w, "IDEmisor", Numero(sobre.IdEmisor));
            El(w, "IDReceptor", Numero(idReceptor));
            El(w, "CantidadCFE", Math.Min(sobre.Cantidad, CfeSobre.MaxCfePorSobre).ToString(CultureInfo.InvariantCulture));
            El(w, "Tmst", CfeXml.TimestampUruguay(tmst));
            w.WriteEndElement();

            w.WriteStartElement("Detalle", CfeXml.NsUri);
            El(w, "Estado", sobre.Motivos.Count == 0 ? "AS" : "BS");
            foreach (var motivo in sobre.Motivos.Take(30))
                EscribirMotivo(w, "MotivosRechazo", motivo);
            w.WriteEndElement();

            w.WriteEndElement();
            w.WriteEndDocument();
        });

    /// <summary>Respuesta a los comprobantes de un sobre recibido (<c>ACKCFE</c>, sin firmar): AE o BE por CFE.</summary>
    internal static string RespuestaCfes(
        string rutPropio, SobreRecibido sobre, long idRespuesta, IReadOnlyList<(CfeRecibido Cfe, IReadOnlyList<MotivoRechazo> Motivos)> resultados,
        DateTimeOffset tmst) =>
        Escribir(w =>
        {
            w.WriteStartDocument();
            w.WriteStartElement("ACKCFE", CfeXml.NsUri);
            w.WriteAttributeString("version", "1.0");

            var rechazados = resultados.Count(r => r.Motivos.Count > 0);
            w.WriteStartElement("Caratula", CfeXml.NsUri);
            El(w, "RUCReceptor", rutPropio);
            El(w, "RUCEmisor", sobre.RutEmisor);
            El(w, "IDRespuesta", Numero(idRespuesta));
            El(w, "NomArch", Recortar(sobre.NombreArchivo ?? "", 40));
            El(w, "FecHRecibido", CfeXml.TimestampUruguay(sobre.FechaRecepcion));
            El(w, "IDEmisor", Numero(sobre.IdEmisor));
            El(w, "IDReceptor", Numero(sobre.IdReceptor));
            El(w, "CantenSobre", sobre.Comprobantes.Count.ToString(CultureInfo.InvariantCulture));
            El(w, "CantResponden", resultados.Count.ToString(CultureInfo.InvariantCulture));
            El(w, "CantCFEAceptados", (resultados.Count - rechazados).ToString(CultureInfo.InvariantCulture));
            El(w, "CantCFERechazados", rechazados.ToString(CultureInfo.InvariantCulture));
            El(w, "Tmst", CfeXml.TimestampUruguay(tmst));
            w.WriteEndElement();

            foreach (var (cfe, motivos) in resultados)
            {
                w.WriteStartElement("ACKCFE_Det", CfeXml.NsUri);
                El(w, "Nro_ordinal", cfe.NroOrdinal.ToString(CultureInfo.InvariantCulture));
                // Los datos del CFE son opcionales: se informan solo si son válidos para el esquema.
                if (cfe.Tipo is { } tipo && Enum.IsDefined(tipo))
                    El(w, "TipoCFE", ((int)tipo).ToString(CultureInfo.InvariantCulture));
                if (Validacion.EsSerie(cfe.Serie))
                    El(w, "Serie", cfe.Serie!);
                if (cfe.Numero is >= 1 and <= 9_999_999)
                    El(w, "NroCFE", Numero(cfe.Numero.Value));
                if (cfe.FechaEmision is { } fecha && fecha >= Validacion.FechaMinima && fecha <= Validacion.FechaMaxima)
                    El(w, "FechaCFE", CfeFormat.DateIso(fecha));
                if (cfe.FechaFirma is { } firma)
                    El(w, "TmstCFE", CfeXml.TimestampUruguay(firma));
                El(w, "Estado", motivos.Count == 0 ? "AE" : "BE");
                foreach (var motivo in motivos.Take(10))
                    EscribirMotivo(w, "MotivosRechazoCF", motivo);
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndDocument();
        });

    /// <summary>Comunicación de CFE anulados por rechazo de la DGI (<c>CFEs_Anulados_x_RechazoDGI</c>, sin firmar).</summary>
    internal static string NotificacionAnulacion(
        string rutPropio, string rutReceptor, long idComunicacion, IReadOnlyList<CfeEmitido> cfes, DateTimeOffset tmst) =>
        Escribir(w =>
        {
            w.WriteStartDocument();
            w.WriteStartElement("CFEs_Anulados_x_RechazoDGI", CfeXml.NsUri);
            w.WriteAttributeString("version", "1.0");

            w.WriteStartElement("Caratula", CfeXml.NsUri);
            El(w, "RUCEmisor", rutPropio);
            El(w, "RUCReceptor", rutReceptor);
            El(w, "IDComunicacion", Numero(idComunicacion));
            El(w, "CantCFEAnulados", cfes.Count.ToString(CultureInfo.InvariantCulture));
            El(w, "Tmst", CfeXml.TimestampUruguay(tmst));
            w.WriteEndElement();

            foreach (var cfe in cfes)
            {
                w.WriteStartElement("CFEAnulado", CfeXml.NsUri);
                El(w, "TipoCFE", ((int)cfe.Tipo).ToString(CultureInfo.InvariantCulture));
                El(w, "Serie", cfe.Serie);
                El(w, "NroCFE", Numero(cfe.Numero));
                El(w, "FechaCFE", CfeFormat.DateIso(cfe.FechaEmision));
                El(w, "Motivo_Cod", "RD");
                El(w, "Motivo_Glosa", "Comprobante anulado por rechazo de DGI");
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndDocument();
        });

    private static void EscribirMotivo(XmlWriter w, string elemento, MotivoRechazo motivo)
    {
        w.WriteStartElement(elemento, CfeXml.NsUri);
        El(w, "Motivo", motivo.Codigo);
        El(w, "Glosa", Recortar(motivo.Glosa, 50));
        if (!string.IsNullOrWhiteSpace(motivo.Detalle))
            El(w, "Detalle", Recortar(motivo.Detalle, 500));
        w.WriteEndElement();
    }

    // -------------------------------------------------------------------------
    // Mensajes de los receptores (lado emisor) y del emisor (lado receptor)
    // -------------------------------------------------------------------------

    /// <summary>Lee el acuse de recibo (<c>ACKSobre</c>) que un receptor envió por un sobre de <paramref name="rutPropio"/>.</summary>
    /// <exception cref="UruFacturaException">Si la firma no es válida o el acuse no es de un sobre de este emisor.</exception>
    internal static AcuseSobre LeerAcuseSobre(string xml, string rutPropio)
    {
        try
        {
            return DgiRespuestaParser.AcuseSobreDe(Firmado(xml, "ACKSobre", "RUCEmisor", rutPropio), xml,
                "Sobre recibido por el receptor electrónico. Su respuesta a cada comprobante llega aparte (ACKCFE).");
        }
        catch (DgiCommunicationException ex)
        {
            throw new UruFacturaException($"El acuse del receptor no se puede interpretar: {ex.Message}", ex);
        }
    }

    /// <summary>Lee la respuesta a los comprobantes (<c>ACKCFE</c>) que un receptor envió por un sobre de <paramref name="rutPropio"/>.</summary>
    /// <exception cref="UruFacturaException">Si la firma no es válida o la respuesta no es de un sobre de este emisor.</exception>
    internal static EstadoEnvio LeerRespuestaCfes(string xml, string rutPropio)
    {
        try
        {
            return DgiRespuestaParser.EstadoEnvioDe(Firmado(xml, "ACKCFE", "RUCEmisor", rutPropio), xml);
        }
        catch (DgiCommunicationException ex)
        {
            throw new UruFacturaException($"La respuesta del receptor no se puede interpretar: {ex.Message}", ex);
        }
    }

    /// <summary>Lee la comunicación de CFE anulados por rechazo de la DGI que un proveedor envió a <paramref name="rutPropio"/>.</summary>
    /// <exception cref="UruFacturaException">Si la firma no es válida, no está dirigida a este receptor o no se puede interpretar.</exception>
    internal static IReadOnlyList<CfeAnulado> LeerNotificacionAnulacion(string xml, string rutPropio)
    {
        var doc = Firmado(xml, "CFEs_Anulados_x_RechazoDGI", "RUCReceptor", rutPropio);
        var rutEmisor = Texto(Hijo(doc.DocumentElement!, "Caratula"), "RUCEmisor") ?? "";
        return
        [
            .. doc.DocumentElement!.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "CFEAnulado").Select(e =>
                Entero(Texto(e, "TipoCFE")) is { } tipo && Texto(e, "Serie") is { } serie && Entero(Texto(e, "NroCFE")) is { } numero
                && Fecha(Texto(e, "FechaCFE")) is { } fecha
                    ? new CfeAnulado(rutEmisor, (TipoCfe)(int)tipo, serie, numero, fecha)
                    : throw new UruFacturaException("La comunicación de anulación tiene un CFE sin tipo, serie, número o fecha.")),
        ];
    }

    /// <summary>
    /// Carga un mensaje firmado de otro emisor y comprueba la firma, el elemento raíz y que el RUT de
    /// <paramref name="campoRut"/> en la carátula sea el propio.
    /// </summary>
    private static XmlDocument Firmado(string xml, string raiz, string campoRut, string rutPropio)
    {
        using (var firmante = FirmaDigital.VerificarFirma(xml))
        {
            if (firmante is null)
                throw new UruFacturaException($"La firma del {raiz} no es válida.");
        }

        var doc = XmlSeguro.Cargar(xml, preservarEspacios: true);
        if (doc.DocumentElement is not { } elemento || elemento.LocalName != raiz || elemento.NamespaceURI != CfeXml.NsUri)
            throw new UruFacturaException($"Se esperaba un {raiz} y llegó {doc.DocumentElement?.LocalName}.");
        var rut = Texto(Hijo(elemento, "Caratula"), campoRut);
        if (rut != rutPropio)
            throw new UruFacturaException($"El {raiz} no es para este emisor: su {campoRut} es {rut}.");
        return doc;
    }

    // -------------------------------------------------------------------------

    /// <summary>Nombre de los archivos de respuesta a un sobre: <c>{prefijo}_IDRespuesta_Sob_RUC_AAAAMMDD_IdEmisor.xml</c>.</summary>
    internal static string NombreRespuesta(string prefijo, long idRespuesta, string rutEmisor, DateTimeOffset fechaSobre, long idEmisor) =>
        $"{prefijo}_{Numero(idRespuesta)}_{Path.GetFileNameWithoutExtension(CfeSobre.NombreArchivo(rutEmisor, fechaSobre, idEmisor))}.xml";

    private static XmlElement? Hijo(XmlNode? padre, string nombre, bool profundo = false) =>
        padre is null ? null
        : profundo ? padre.SelectSingleNode($".//*[local-name()='{nombre}']") as XmlElement
        : padre.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == nombre);

    /// <summary>Texto de un hijo directo (o del propio nodo si <paramref name="nombre"/> es <c>null</c>).</summary>
    private static string? Texto(XmlNode? nodo, string? nombre)
    {
        var elemento = nombre is null ? nodo : Hijo(nodo, nombre);
        var texto = elemento?.InnerText.Trim();
        return string.IsNullOrEmpty(texto) ? null : texto;
    }

    private static long? Entero(string? texto) =>
        long.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor) ? valor : null;

    private static decimal? Decimal(string? texto) =>
        decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor) ? valor : null;

    private static DateOnly? Fecha(string? texto) =>
        DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fecha) ? fecha : null;

    private static string Numero(long valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
