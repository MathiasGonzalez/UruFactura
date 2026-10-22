using System.Text;
using System.Xml;
using UruFactura.Formatting;

namespace UruFactura.Xml;

/// <summary>Escritura de los documentos XML de la DGI (CFE, sobre y reporte diario).</summary>
internal static class XmlCfe
{
    private static readonly XmlWriterSettings Settings = new()
    {
        // UTF-8 sin BOM: la declaración dice encoding="utf-8" y el resultado se puede cargar
        // directamente con XmlDocument.LoadXml. Sin indentar: los documentos se firman.
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = false,
    };

    /// <summary>Escribe el documento con <paramref name="contenido"/> y lo devuelve como texto.</summary>
    internal static string Escribir(Action<XmlWriter> contenido)
    {
        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, Settings))
            contenido(writer);
        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    /// <summary>Escribe <c>&lt;nombre&gt;valor&lt;/nombre&gt;</c> en el namespace de los CFE.</summary>
    internal static void El(XmlWriter w, string nombre, string valor)
    {
        w.WriteStartElement(nombre, CfeXml.NsUri);
        w.WriteString(valor);
        w.WriteEndElement();
    }

    /// <summary>Monto con 2 decimales.</summary>
    internal static string M2(decimal valor) => CfeFormat.DecimalInvariant(valor, "F2");
}
