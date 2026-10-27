using System.Globalization;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace UruFactura.Xml;

/// <summary>
/// Arma los "sobres" con CFE firmados (formato del sobre v05): <c>EnvioCFE</c> para la DGI
/// (<c>EnvioCFE.xsd</c>) y <c>EnvioCFE_entreEmpresas</c> para un receptor electrónico
/// (<c>EnvioCFE_entreEmpresas.xsd</c>, con la adenda de cada CFE). Una carátula con los datos del envío y
/// el certificado con que se firmaron los CFE, más de 1 a 250 CFE.
/// </summary>
internal static class CfeSobre
{
    /// <summary>RUT de la DGI como receptor de los sobres (el que usa la documentación oficial).</summary>
    internal const string RutDgi = "214844360018";

    /// <summary>Cantidad máxima de CFE por sobre.</summary>
    internal const int MaxCfePorSobre = 250;

    /// <summary>Tamaño máximo del sobre: 2 MB (formato del sobre v05; motivo de rechazo S07).</summary>
    internal const int TamanoMaximo = 2_000_000;

    /// <summary>Genera el sobre <c>EnvioCFE</c> para la DGI.</summary>
    /// <param name="rutEmisor">RUT del emisor (12 dígitos).</param>
    /// <param name="idEmisor">Número asignado por el emisor al envío (hasta 10 dígitos).</param>
    /// <param name="fecha">Fecha y hora de creación del sobre.</param>
    /// <param name="certificadoDer">
    /// Certificado público (DER) para la carátula si los CFE no lo incluyen en su firma (los de UruFactura
    /// lo incluyen, y la carátula lleva ese).
    /// </param>
    /// <param name="cfesFirmados">XML de 1 a 250 CFE ya firmados.</param>
    internal static string Generar(
        string rutEmisor, long idEmisor, DateTimeOffset fecha, byte[] certificadoDer, IReadOnlyList<string> cfesFirmados) =>
        Generar("EnvioCFE", RutDgi, rutEmisor, idEmisor, fecha, certificadoDer, [.. cfesFirmados.Select(xml => (xml, (string?)null))]);

    /// <summary>Genera el sobre <c>EnvioCFE_entreEmpresas</c> para un receptor electrónico, con la adenda de cada CFE.</summary>
    /// <param name="rutReceptor">RUT del receptor electrónico al que van dirigidos los CFE.</param>
    /// <param name="rutEmisor">RUT del emisor (12 dígitos).</param>
    /// <param name="idEmisor">Número asignado por el emisor al envío (hasta 10 dígitos).</param>
    /// <param name="fecha">Fecha y hora de creación del sobre.</param>
    /// <param name="certificadoDer">Certificado para la carátula si los CFE no lo incluyen en su firma.</param>
    /// <param name="cfes">XML de 1 a 250 CFE ya firmados y su adenda (o <c>null</c>).</param>
    internal static string GenerarParaReceptor(
        string rutReceptor, string rutEmisor, long idEmisor, DateTimeOffset fecha, byte[] certificadoDer,
        IReadOnlyList<(string Xml, string? Adenda)> cfes) =>
        Generar("EnvioCFE_entreEmpresas", rutReceptor, rutEmisor, idEmisor, fecha, certificadoDer, cfes);

    /// <summary>Nombre del archivo del sobre: <c>Sob_RUC_AAAAMMDD_IdEmisor.xml</c>, con la fecha de la carátula.</summary>
    internal static string NombreArchivo(string rutEmisor, DateTimeOffset fecha, long idEmisor) =>
        $"Sob_{rutEmisor}_{fecha.ToOffset(HoraUruguay.Offset):yyyyMMdd}_{idEmisor.ToString(CultureInfo.InvariantCulture)}.xml";

    private static string Generar(
        string raiz, string rutReceptor, string rutEmisor, long idEmisor, DateTimeOffset fecha, byte[] certificadoDer,
        IReadOnlyList<(string Xml, string? Adenda)> cfes)
    {
        if (cfes.Count is < 1 or > MaxCfePorSobre)
            throw new UruFacturaException(
                $"Un sobre debe contener entre 1 y {MaxCfePorSobre} CFE (recibidos: {cfes.Count}).");

        if (idEmisor is < 0 or > 9_999_999_999)
            throw new UruFacturaException("El Idemisor del sobre debe tener hasta 10 dígitos.");

        // PreserveWhitespace: los CFE ya están firmados y cualquier espacio que se pierda al
        // guardar el sobre invalidaría su firma.
        var cfeDocs = cfes.Select(c =>
        {
            var cfeDoc = new XmlDocument { PreserveWhitespace = true };
            cfeDoc.LoadXml(c.Xml);
            return (Doc: cfeDoc, c.Adenda);
        }).ToList();

        // Todos los CFE de un sobre van firmados con el certificado de la carátula (motivo de rechazo S06):
        // la carátula lleva el de sus firmas, que puede no ser el actual si se renovó después de emitirlos.
        var certificados = cfeDocs.Select(c => CertificadoDeLaFirma(c.Doc)).OfType<string>().Distinct().ToList();
        if (certificados.Count > 1)
            throw new UruFacturaException(
                "Los CFE están firmados con certificados distintos y un sobre lleva uno solo: envíelos en sobres separados.");

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", null));

        var envio = doc.CreateElement(raiz, CfeXml.NsUri);
        envio.SetAttribute("version", "1.0");
        doc.AppendChild(envio);

        var caratula = AddEl(doc, envio, "Caratula", null);
        caratula.SetAttribute("version", "1.0");
        AddEl(doc, caratula, "RutReceptor", rutReceptor);
        AddEl(doc, caratula, "RUCEmisor", rutEmisor);
        AddEl(doc, caratula, "Idemisor", idEmisor.ToString(CultureInfo.InvariantCulture));
        AddEl(doc, caratula, "CantCFE", cfes.Count.ToString(CultureInfo.InvariantCulture));
        AddEl(doc, caratula, "Fecha", CfeXml.TimestampUruguay(fecha));
        AddEl(doc, caratula, "X509Certificate", certificados.SingleOrDefault() ?? Convert.ToBase64String(certificadoDer));

        var entreEmpresas = raiz != "EnvioCFE";
        foreach (var (cfeDoc, adenda) in cfeDocs)
        {
            var cfe = doc.ImportNode(cfeDoc.DocumentElement!, true);
            if (!entreEmpresas)
            {
                envio.AppendChild(cfe);
                continue;
            }

            // CFE_Adenda: el CFE firmado y, fuera de la firma, la adenda (no se envía a la DGI).
            var conAdenda = AddEl(doc, envio, "CFE_Adenda", null);
            conAdenda.AppendChild(cfe);
            if (!string.IsNullOrEmpty(adenda))
                AddEl(doc, conAdenda, "Adenda", adenda);
        }

        var xml = XmlCfe.Escribir(doc.Save);
        if (Encoding.UTF8.GetByteCount(xml) > TamanoMaximo)
            throw new UruFacturaException(
                $"El sobre supera los {TamanoMaximo / 1_000_000} MB que admite el formato: envíe menos CFE por sobre.");
        return xml;
    }

    /// <summary>El certificado (base64) del <c>KeyInfo</c> de la firma del CFE, o <c>null</c> si no lo incluye.</summary>
    internal static string? CertificadoDeLaFirma(XmlNode cfe)
    {
        var nodo = cfe.SelectSingleNode($".//*[local-name()='X509Certificate' and namespace-uri()='{SignedXml.XmlDsigNamespaceUrl}']");
        return nodo is null ? null : string.Concat(nodo.InnerText.Where(c => !char.IsWhiteSpace(c)));
    }

    private static XmlElement AddEl(XmlDocument doc, XmlElement parent, string name, string? text)
    {
        var el = doc.CreateElement(name, CfeXml.NsUri);
        if (text is not null)
            el.InnerText = text;
        parent.AppendChild(el);
        return el;
    }
}
