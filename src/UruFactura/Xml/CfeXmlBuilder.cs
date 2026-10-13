using System.Globalization;
using System.Text;
using System.Xml;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Formatting;
using UruFactura.Models;

namespace UruFactura.Xml;

/// <summary>
/// Genera el XML de un CFE conforme al esquema XSD oficial de la DGI (<c>CFEType.xsd</c>,
/// namespace <c>http://cfe.dgi.gub.uy</c>, atributo <c>version="1.0"</c>).
/// Soporta e-Ticket, e-Factura, e-Factura de exportación, e-Remito y e-Remito de exportación
/// con sus notas de corrección.
/// </summary>
public class CfeXmlBuilder : ICfeXmlBuilder
{
    internal const string NsUri = "http://cfe.dgi.gub.uy";

    /// <summary>
    /// Genera el XML sin firmar del CFE. La firma XML (<c>ds:Signature</c>) se agrega después
    /// con <see cref="Signature.ICfeFirmante"/>.
    /// </summary>
    /// <exception cref="CfeValidationException">Si el CFE tiene errores de validación.</exception>
    public virtual string Generar(Cfe cfe)
    {
        // Los totales se derivan siempre del detalle (el cálculo es idempotente).
        cfe.CalcularTotales();

        var errores = cfe.Validar();
        if (errores.Count > 0)
            throw new CfeValidationException(errores);

        // MemoryStream + UTF8 sin BOM: la declaración dice encoding="utf-8" y el resultado
        // se puede cargar directamente con XmlDocument.LoadXml.
        using var ms = new MemoryStream();
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            OmitXmlDeclaration = false,
        };

        using (var writer = XmlWriter.Create(ms, settings))
        {
            EscribirCfe(writer, cfe);
            writer.Flush();
        }

        return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }

    private enum Familia { Ticket, Factura, FacturaExp, Remito, RemitoExp }

    private static Familia FamiliaDe(TipoCfe tipo) => tipo switch
    {
        TipoCfe.ETicket or TipoCfe.NotaCreditoETicket or TipoCfe.NotaDebitoETicket => Familia.Ticket,
        TipoCfe.EFactura or TipoCfe.NotaCreditoEFactura or TipoCfe.NotaDebitoEFactura => Familia.Factura,
        TipoCfe.EFacturaExportacion or TipoCfe.NotaCreditoEFacturaExportacion
            or TipoCfe.NotaDebitoEFacturaExportacion => Familia.FacturaExp,
        TipoCfe.ERemito => Familia.Remito,
        TipoCfe.ERemitoExportacion => Familia.RemitoExp,
        _ => throw new CfeValidationException([$"El tipo de CFE {tipo} no está soportado."]),
    };

    private static string ElementoRaiz(Familia f) => f switch
    {
        Familia.Ticket => "eTck",
        Familia.Factura => "eFact",
        Familia.FacturaExp => "eFact_Exp",
        Familia.Remito => "eRem",
        _ => "eRem_Exp",
    };

    private static void EscribirCfe(XmlWriter w, Cfe cfe)
    {
        var familia = FamiliaDe(cfe.Tipo);

        w.WriteStartDocument();
        w.WriteStartElement("CFE", NsUri);
        w.WriteAttributeString("version", "1.0");
        w.WriteStartElement(ElementoRaiz(familia), NsUri);

        El(w, "TmstFirma", TimestampUruguay(DateTimeOffset.UtcNow));

        w.WriteStartElement("Encabezado", NsUri);
        EscribirIdDoc(w, cfe, familia);
        EscribirEmisor(w, cfe);
        if (cfe.Receptor != null)
            EscribirReceptor(w, cfe, familia);
        EscribirTotales(w, cfe, familia);
        w.WriteEndElement(); // Encabezado

        w.WriteStartElement("Detalle", NsUri);
        for (int i = 0; i < cfe.Detalle.Count; i++)
            EscribirLinea(w, cfe.Detalle[i], i + 1, familia);
        w.WriteEndElement(); // Detalle

        if (cfe.Referencias.Count > 0)
        {
            w.WriteStartElement("Referencia", NsUri);
            for (int i = 0; i < cfe.Referencias.Count; i++)
                EscribirReferencia(w, cfe.Referencias[i], i + 1);
            w.WriteEndElement(); // Referencia
        }

        EscribirCaeData(w, cfe.CaeData!);

        w.WriteEndElement(); // eXxx
        w.WriteEndElement(); // CFE
        w.WriteEndDocument();
    }

    /// <summary>Fecha/hora con el offset de Uruguay (UTC-03:00, sin horario de verano).</summary>
    internal static string TimestampUruguay(DateTimeOffset instante) =>
        instante.ToOffset(TimeSpan.FromHours(-3))
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static void EscribirIdDoc(XmlWriter w, Cfe cfe, Familia familia)
    {
        w.WriteStartElement("IdDoc", NsUri);
        El(w, "TipoCFE", Int(cfe.Tipo));
        El(w, "Serie", cfe.Serie!);
        El(w, "Nro", cfe.Numero.ToString(CultureInfo.InvariantCulture));
        El(w, "FchEmis", CfeFormat.DateIso(cfe.FechaEmision));

        switch (familia)
        {
            case Familia.Remito:
                El(w, "TipoTraslado", TipoTraslado(cfe));
                break;

            case Familia.RemitoExp:
                El(w, "TipoTraslado", TipoTraslado(cfe));
                if (!string.IsNullOrWhiteSpace(cfe.ClausulaVenta))
                    El(w, "ClauVenta", cfe.ClausulaVenta);
                El(w, "ModVenta", cfe.ModalidadVenta!.Value.ToString(CultureInfo.InvariantCulture));
                El(w, "ViaTransp", cfe.ViaTransporte!.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case Familia.FacturaExp:
                El(w, "FmaPago", Int(cfe.FormaPago));
                El(w, "ClauVenta", cfe.ClausulaVenta!);
                if (cfe.ModalidadVenta.HasValue)
                    El(w, "ModVenta", cfe.ModalidadVenta.Value.ToString(CultureInfo.InvariantCulture));
                El(w, "ViaTransp", cfe.ViaTransporte!.Value.ToString(CultureInfo.InvariantCulture));
                break;

            default:
                // Los precios del detalle son netos (sin IVA): MntBruto se omite.
                El(w, "FmaPago", Int(cfe.FormaPago));
                break;
        }

        w.WriteEndElement(); // IdDoc
    }

    /// <summary>
    /// <c>TipoTraslado</c> del e-Remito: 1 = Venta, 2 = Traslados internos.
    /// </summary>
    private static string TipoTraslado(Cfe cfe) =>
        cfe.IndTraslado == Enums.IndTraslado.TrasladoPorVenta ? "1" : "2";

    private static void EscribirEmisor(XmlWriter w, Cfe cfe)
    {
        w.WriteStartElement("Emisor", NsUri);
        El(w, "RUCEmisor", cfe.RutEmisor);
        El(w, "RznSoc", cfe.RazonSocialEmisor);
        if (!string.IsNullOrWhiteSpace(cfe.NombreComercialEmisor))
            El(w, "NomComercial", cfe.NombreComercialEmisor);
        if (!string.IsNullOrWhiteSpace(cfe.Giro))
            El(w, "GiroEmis", cfe.Giro);
        El(w, "CdgDGISucur", cfe.CodigoSucursal.ToString(CultureInfo.InvariantCulture));
        El(w, "DomFiscal", cfe.DomicilioFiscalEmisor);
        El(w, "Ciudad", cfe.CiudadEmisor);
        El(w, "Departamento", cfe.DepartamentoEmisor);
        w.WriteEndElement(); // Emisor
    }

    private static void EscribirReceptor(XmlWriter w, Cfe cfe, Familia familia)
    {
        var r = cfe.Receptor!;
        var conDocumento = !string.IsNullOrWhiteSpace(r.Documento);
        var pais = string.IsNullOrWhiteSpace(r.CodigoPais) ? "UY" : r.CodigoPais.ToUpperInvariant();
        var tipoDoc = TipoDocumentoDgi(r.TipoDocumento);

        w.WriteStartElement("Receptor", NsUri);

        if (conDocumento)
        {
            if (familia == Familia.Factura)
            {
                // Receptor_Fact: documento fijo RUC (2) de Uruguay.
                El(w, "TipoDocRecep", "2");
                El(w, "CodPaisRecep", "UY");
                El(w, "DocRecep", r.Documento!);
            }
            else
            {
                El(w, "TipoDocRecep", tipoDoc);
                El(w, "CodPaisRecep", pais);
                El(w, pais == "UY" ? "DocRecep" : "DocRecepExt", r.Documento!);
            }
        }

        if (!string.IsNullOrWhiteSpace(r.RazonSocial))
            El(w, "RznSocRecep", r.RazonSocial);

        // Dirección, ciudad y departamento solo existen en el receptor de e-Ticket y e-Factura.
        if (familia is Familia.Ticket or Familia.Factura)
        {
            if (!string.IsNullOrWhiteSpace(r.Direccion))
                El(w, "DirRecep", r.Direccion);
            if (!string.IsNullOrWhiteSpace(r.Ciudad))
                El(w, "CiudadRecep", r.Ciudad);
            if (!string.IsNullOrWhiteSpace(r.Departamento))
                El(w, "DeptoRecep", r.Departamento);
        }

        if (!string.IsNullOrWhiteSpace(r.Pais))
            El(w, "PaisRecep", r.Pais);

        w.WriteEndElement(); // Receptor
    }

    /// <summary>Código de tipo de documento de la DGI (<c>DocType</c>): 2 RUC, 3 CI, 4 Otro, 5 Pasaporte.</summary>
    private static string TipoDocumentoDgi(TipoDocumentoReceptor tipo) => tipo switch
    {
        TipoDocumentoReceptor.Rut => "2",
        TipoDocumentoReceptor.CedulaIdentidad => "3",
        TipoDocumentoReceptor.Pasaporte => "5",
        TipoDocumentoReceptor.Otro or TipoDocumentoReceptor.DocumentoExterior => "4",
        TipoDocumentoReceptor.Dni => "6",
        TipoDocumentoReceptor.Nife => "7",
        _ => "4",
    };

    private static void EscribirTotales(XmlWriter w, Cfe cfe, Familia familia)
    {
        w.WriteStartElement("Totales", NsUri);

        switch (familia)
        {
            case Familia.Remito:
                El(w, "CantLinDet", Cant(cfe));
                break;

            case Familia.RemitoExp:
            case Familia.FacturaExp:
                EscribirMoneda(w, cfe);
                El(w, "MntExpoyAsim", M2(cfe.MontoExportacion));
                El(w, "MntTotal", M2(cfe.MontoTotal));
                El(w, "CantLinDet", Cant(cfe));
                if (familia == Familia.FacturaExp)
                    El(w, "MntPagar", M2(cfe.MontoTotal));
                break;

            default:
                EscribirMoneda(w, cfe);
                if (cfe.MontoNetoExento > 0)
                    El(w, "MntNoGrv", M2(cfe.MontoNetoExento));
                if (cfe.MontoNetoSuspendido > 0)
                    El(w, "MntIVaenSusp", M2(cfe.MontoNetoSuspendido));
                if (cfe.MontoNetoMinimo > 0)
                    El(w, "MntNetoIvaTasaMin", M2(cfe.MontoNetoMinimo));
                if (cfe.MontoNetoBasico > 0)
                    El(w, "MntNetoIVATasaBasica", M2(cfe.MontoNetoBasico));
                if (cfe.MontoNetoMinimo > 0)
                    El(w, "IVATasaMin", "10.000");
                if (cfe.MontoNetoBasico > 0)
                    El(w, "IVATasaBasica", "22.000");
                if (cfe.MontoNetoMinimo > 0)
                    El(w, "MntIVATasaMin", M2(cfe.IvaMinimo));
                if (cfe.MontoNetoBasico > 0)
                    El(w, "MntIVATasaBasica", M2(cfe.IvaBasico));
                El(w, "MntTotal", M2(cfe.MontoTotal));
                El(w, "CantLinDet", Cant(cfe));
                El(w, "MntPagar", M2(cfe.MontoTotal));
                break;
        }

        w.WriteEndElement(); // Totales
    }

    private static void EscribirMoneda(XmlWriter w, Cfe cfe)
    {
        El(w, "TpoMoneda", ObtenerCodigoIso4217(cfe.Moneda));
        if (cfe.Moneda != Moneda.PesoUruguayo && cfe.TipoCambio.HasValue)
            El(w, "TpoCambio", CfeFormat.DecimalInvariant(cfe.TipoCambio.Value, "F3"));
    }

    private static void EscribirLinea(XmlWriter w, LineaDetalle linea, int nro, Familia familia)
    {
        w.WriteStartElement("Item", NsUri);
        El(w, "NroLinDet", nro.ToString(CultureInfo.InvariantCulture));

        var unidad = string.IsNullOrWhiteSpace(linea.UnidadMedida) ? "N/A" : linea.UnidadMedida;

        if (familia == Familia.Remito)
        {
            El(w, "NomItem", linea.NombreItem);
            El(w, "Cantidad", CfeFormat.DecimalInvariant(linea.Cantidad, "F3"));
            El(w, "UniMed", unidad);
            w.WriteEndElement();
            return;
        }

        // Item_Rem_Exp no admite IndFact 10: en el e-Remito de exportación se omite.
        if (familia != Familia.RemitoExp)
            El(w, "IndFact", IndFact(linea, familia));
        El(w, "NomItem", linea.NombreItem);
        El(w, "Cantidad", CfeFormat.DecimalInvariant(linea.Cantidad, "F3"));
        El(w, "UniMed", unidad);
        El(w, "PrecioUnitario", CfeFormat.DecimalInvariant(linea.PrecioUnitario, "F6"));

        if (linea.DescuentoMonto > 0 && familia != Familia.RemitoExp)
            El(w, "DescuentoMonto", M2(linea.DescuentoMonto));

        if (linea.RecargoMonto > 0 && familia != Familia.RemitoExp)
            El(w, "RecargoMnt", M2(linea.RecargoMonto));

        El(w, "MontoItem", M2(linea.MontoTotal));
        w.WriteEndElement(); // Item
    }

    /// <summary>Indicador de facturación (<c>IndFact</c>) de la línea.</summary>
    private static string IndFact(LineaDetalle linea, Familia familia)
    {
        if (familia is Familia.FacturaExp or Familia.RemitoExp)
            return "10"; // Exportación y asimiladas

        return linea.IndFactIva switch
        {
            TipoIva.Exento => "1",
            TipoIva.Minimo => "2",
            TipoIva.Basico => "3",
            TipoIva.Suspendido => "12", // IVA en suspenso
            _ => throw new CfeValidationException([$"Indicador de IVA no soportado: {linea.IndFactIva}."]),
        };
    }

    private static void EscribirReferencia(XmlWriter w, RefCfe r, int nro)
    {
        w.WriteStartElement("Referencia", NsUri);
        El(w, "NroLinRef", nro.ToString(CultureInfo.InvariantCulture));
        El(w, "TpoDocRef", Int(r.TipoCfe));
        El(w, "Serie", r.Serie);
        El(w, "NroCFERef", r.NroCfe.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(r.Razon))
            El(w, "RazonRef", r.Razon);
        El(w, "FechaCFEref", CfeFormat.DateIso(r.FechaCfe));
        if (r.MontoCfeRef.HasValue)
            El(w, "MntCFEref", M2(r.MontoCfeRef.Value));
        if (r.MonedaCfeRef.HasValue)
            El(w, "TpoMonedaRef", ObtenerCodigoIso4217(r.MonedaCfeRef.Value));
        if (r.TipoCambioCfeRef.HasValue)
            El(w, "TpoCambioRef", CfeFormat.DecimalInvariant(r.TipoCambioCfeRef.Value, "F3"));
        w.WriteEndElement(); // Referencia
    }

    private static void EscribirCaeData(XmlWriter w, CaeData cae)
    {
        w.WriteStartElement("CAEData", NsUri);
        El(w, "CAE_ID", cae.NumeroAutorizacion);
        El(w, "DNro", cae.RangoDesde.ToString(CultureInfo.InvariantCulture));
        El(w, "HNro", cae.RangoHasta.ToString(CultureInfo.InvariantCulture));
        El(w, "FecVenc", CfeFormat.DateIso(cae.FechaVencimiento));
        w.WriteEndElement(); // CAEData
    }

    // -------------------------------------------------------------------------

    private static void El(XmlWriter w, string name, string value)
    {
        w.WriteStartElement(name, NsUri);
        w.WriteString(value);
        w.WriteEndElement();
    }

    private static string Int(Enum value) =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string M2(decimal value) => CfeFormat.DecimalInvariant(value, "F2");

    private static string Cant(Cfe cfe) => cfe.Detalle.Count.ToString(CultureInfo.InvariantCulture);

    /// <summary>Código ISO 4217 de 3 letras que usa la DGI (<c>TpoMoneda</c>).</summary>
    private static string ObtenerCodigoIso4217(Moneda moneda) => moneda switch
    {
        Moneda.PesoUruguayo   => "UYU",
        Moneda.DolarAmericano => "USD",
        Moneda.Euro           => "EUR",
        _ => throw new ArgumentOutOfRangeException(nameof(moneda),
                 $"Moneda '{moneda}' sin código ISO 4217 definido."),
    };
}
