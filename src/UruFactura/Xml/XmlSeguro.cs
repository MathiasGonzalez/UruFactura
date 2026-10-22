using System.Xml;

namespace UruFactura.Xml;

/// <summary>Carga de XML que no proviene del SDK (respuestas de la DGI, CFE de terceros).</summary>
internal static class XmlSeguro
{
    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>Carga el XML sin procesar DTD (evita XXE y la expansión masiva de entidades).</summary>
    /// <param name="xml">XML a cargar.</param>
    /// <param name="preservarEspacios">Necesario para verificar firmas: los espacios forman parte de lo firmado.</param>
    internal static XmlDocument Cargar(string xml, bool preservarEspacios = false)
    {
        var doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = preservarEspacios };
        using var reader = XmlReader.Create(new StringReader(xml), Settings);
        doc.Load(reader);
        return doc;
    }
}
