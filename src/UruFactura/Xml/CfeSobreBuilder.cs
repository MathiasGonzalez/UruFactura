using System.Globalization;
using System.Text;
using System.Xml;
using UruFactura.Exceptions;

namespace UruFactura.Xml;

/// <summary>
/// Arma el "sobre" <c>EnvioCFE</c> que se envía a la DGI (esquema <c>EnvioCFE.xsd</c>):
/// una carátula con los datos del envío y el certificado, más de 1 a 250 CFE firmados.
/// </summary>
public static class CfeSobreBuilder
{
    /// <summary>RUT de la DGI como receptor de los sobres (el que usa la documentación oficial).</summary>
    public const string RutDgi = "214844360018";

    /// <summary>Cantidad máxima de CFE por sobre.</summary>
    public const int MaxCfePorSobre = 250;

    /// <summary>
    /// Genera el XML del sobre.
    /// </summary>
    /// <param name="rutEmisor">RUT del emisor (12 dígitos).</param>
    /// <param name="idEmisor">Número asignado por el emisor al envío (hasta 10 dígitos).</param>
    /// <param name="fecha">Fecha y hora de creación del sobre.</param>
    /// <param name="certificadoDer">Certificado público (DER) con el que se firmaron los CFE.</param>
    /// <param name="cfesFirmados">XML de 1 a 250 CFE ya firmados.</param>
    /// <param name="rutReceptor">RUT receptor del sobre (por defecto la DGI).</param>
    public static string Generar(
        string rutEmisor,
        long idEmisor,
        DateTimeOffset fecha,
        byte[] certificadoDer,
        IReadOnlyList<string> cfesFirmados,
        string rutReceptor = RutDgi)
    {
        if (cfesFirmados.Count is < 1 or > MaxCfePorSobre)
            throw new UruFacturaException(
                $"Un sobre debe contener entre 1 y {MaxCfePorSobre} CFE (recibidos: {cfesFirmados.Count}).");

        if (idEmisor is < 0 or > 9_999_999_999)
            throw new UruFacturaException("El Idemisor del sobre debe tener hasta 10 dígitos.");

        var doc = new XmlDocument();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", null));

        var envio = doc.CreateElement("EnvioCFE", CfeXmlBuilder.NsUri);
        envio.SetAttribute("version", "1.0");
        doc.AppendChild(envio);

        var caratula = AddEl(doc, envio, "Caratula", null);
        caratula.SetAttribute("version", "1.0");
        AddEl(doc, caratula, "RutReceptor", rutReceptor);
        AddEl(doc, caratula, "RUCEmisor", rutEmisor);
        AddEl(doc, caratula, "Idemisor", idEmisor.ToString(CultureInfo.InvariantCulture));
        AddEl(doc, caratula, "CantCFE", cfesFirmados.Count.ToString(CultureInfo.InvariantCulture));
        AddEl(doc, caratula, "Fecha", CfeXmlBuilder.TimestampUruguay(fecha));
        AddEl(doc, caratula, "X509Certificate", Convert.ToBase64String(certificadoDer));

        foreach (var xml in cfesFirmados)
        {
            var cfeDoc = new XmlDocument { PreserveWhitespace = true };
            cfeDoc.LoadXml(xml);
            envio.AppendChild(doc.ImportNode(cfeDoc.DocumentElement!, true));
        }

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
               {
                   Encoding = new UTF8Encoding(false),
                   Indent = false,
               }))
        {
            doc.Save(writer);
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static XmlElement AddEl(XmlDocument doc, XmlElement parent, string name, string? text)
    {
        var el = doc.CreateElement(name, CfeXmlBuilder.NsUri);
        if (text is not null)
            el.InnerText = text;
        parent.AppendChild(el);
        return el;
    }
}
