using System.Globalization;
using System.Xml;
using UruFactura.Formatting;
using static UruFactura.Xml.XmlCfe;

namespace UruFactura.Xml;

/// <summary>
/// Genera el XML (sin firmar) de un CFE conforme al esquema oficial de la DGI (<c>CFEType.xsd</c>,
/// namespace <c>http://cfe.dgi.gub.uy</c>, atributo <c>version="1.0"</c>), para todos los tipos
/// soportados. El CFE ya debe estar validado.
/// </summary>
internal static class CfeXml
{
    internal const string NsUri = "http://cfe.dgi.gub.uy";

    /// <summary>Genera el XML del CFE.</summary>
    /// <param name="cfe">Contenido, con <see cref="Cfe.FechaEmision"/> y <see cref="Cfe.Emisor"/> resueltos.</param>
    /// <param name="cae">CAE con el que se numera.</param>
    /// <param name="numero">Número asignado.</param>
    /// <param name="totales">Totales del contenido.</param>
    /// <param name="tmstFirma">Fecha y hora de la firma.</param>
    internal static string Generar(Cfe cfe, Cae cae, long numero, Totales totales, DateTimeOffset tmstFirma) =>
        Escribir(w => EscribirCfe(w, new Datos(cfe, cfe.Emisor!, cae, numero, totales, cfe.Tipo.Familia()), tmstFirma));

    /// <summary>Lo que se escribe de un CFE.</summary>
    private sealed record Datos(Cfe Cfe, Emisor Emisor, Cae Cae, long Numero, Totales Totales, FamiliaCfe Familia);

    private static string ElementoRaiz(FamiliaCfe f) => f switch
    {
        FamiliaCfe.Ticket => "eTck",
        FamiliaCfe.Factura => "eFact",
        FamiliaCfe.FacturaExportacion => "eFact_Exp",
        FamiliaCfe.Remito => "eRem",
        FamiliaCfe.RemitoExportacion => "eRem_Exp",
        _ => "eResg",
    };

    private static void EscribirCfe(XmlWriter w, Datos d, DateTimeOffset tmstFirma)
    {
        var cfe = d.Cfe;

        w.WriteStartDocument();
        w.WriteStartElement("CFE", NsUri);
        w.WriteAttributeString("version", "1.0");
        w.WriteStartElement(ElementoRaiz(d.Familia), NsUri);

        El(w, "TmstFirma", TimestampUruguay(tmstFirma));

        w.WriteStartElement("Encabezado", NsUri);
        EscribirIdDoc(w, d);
        EscribirEmisor(w, d.Emisor);
        if (cfe.Receptor != null)
            EscribirReceptor(w, cfe.Receptor, d.Familia);
        EscribirTotales(w, d);
        w.WriteEndElement(); // Encabezado

        w.WriteStartElement("Detalle", NsUri);
        for (int i = 0; i < cfe.Detalle.Count; i++)
            EscribirLinea(w, cfe.Detalle[i], i + 1, d.Familia);
        w.WriteEndElement(); // Detalle

        if (cfe.DescuentosRecargos.Count > 0)
            EscribirDescuentosRecargos(w, cfe, d.Familia);

        if (cfe.MediosPago.Count > 0)
            EscribirMediosPago(w, cfe);

        if (cfe.Referencias.Count > 0)
        {
            w.WriteStartElement("Referencia", NsUri);
            for (int i = 0; i < cfe.Referencias.Count; i++)
                EscribirReferencia(w, cfe.Referencias[i], i + 1);
            w.WriteEndElement(); // Referencia
        }

        EscribirCaeData(w, d.Cae);

        if (cfe.Mandante is not null)
            EscribirComplementoFiscal(w, cfe.Mandante, d.Emisor);

        w.WriteEndElement(); // eXxx
        w.WriteEndElement(); // CFE
        w.WriteEndDocument();
    }

    /// <summary>Fecha/hora con el offset de Uruguay (UTC-03:00, sin horario de verano).</summary>
    internal static string TimestampUruguay(DateTimeOffset instante) =>
        instante.ToOffset(HoraUruguay.Offset)
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static void EscribirIdDoc(XmlWriter w, Datos d)
    {
        var cfe = d.Cfe;
        w.WriteStartElement("IdDoc", NsUri);
        El(w, "TipoCFE", Int(cfe.Tipo));
        El(w, "Serie", d.Cae.Serie);
        El(w, "Nro", d.Numero.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(cfe.NroInterno))
            El(w, "NroInterno", cfe.NroInterno);
        El(w, "FchEmis", CfeFormat.DateIso(cfe.FechaEmision!.Value));

        switch (d.Familia)
        {
            case FamiliaCfe.Remito:
                El(w, "TipoTraslado", TipoTraslado(cfe));
                break;

            case FamiliaCfe.RemitoExportacion:
                El(w, "TipoTraslado", TipoTraslado(cfe));
                if (!string.IsNullOrWhiteSpace(cfe.ClausulaVenta))
                    El(w, "ClauVenta", cfe.ClausulaVenta);
                El(w, "ModVenta", cfe.ModalidadVenta!.Value.ToString(CultureInfo.InvariantCulture));
                El(w, "ViaTransp", cfe.ViaTransporte!.Value.ToString(CultureInfo.InvariantCulture));
                break;

            case FamiliaCfe.Resguardo:
                break;

            case FamiliaCfe.FacturaExportacion:
                El(w, "FmaPago", Int(cfe.FormaPago));
                if (cfe.FechaVencimiento is { } vencExp)
                    El(w, "FchVenc", CfeFormat.DateIso(vencExp));
                El(w, "ClauVenta", cfe.ClausulaVenta!);
                if (cfe.ModalidadVenta.HasValue)
                    El(w, "ModVenta", cfe.ModalidadVenta.Value.ToString(CultureInfo.InvariantCulture));
                El(w, "ViaTransp", cfe.ViaTransporte!.Value.ToString(CultureInfo.InvariantCulture));
                break;

            default:
                // Los precios del detalle son netos (sin IVA): MntBruto se omite.
                El(w, "FmaPago", Int(cfe.FormaPago));
                if (cfe.FechaVencimiento is { } venc)
                    El(w, "FchVenc", CfeFormat.DateIso(venc));
                break;
        }

        if (!string.IsNullOrWhiteSpace(cfe.InfoAdicional))
            El(w, "InfoAdicionalDoc", cfe.InfoAdicional);

        w.WriteEndElement(); // IdDoc
    }

    /// <summary><c>TipoTraslado</c> del e-Remito: 1 = Venta, 2 = Traslados internos.</summary>
    private static string TipoTraslado(Cfe cfe) =>
        cfe.IndTraslado == IndTraslado.TrasladoPorVenta ? "1" : "2";

    private static void EscribirEmisor(XmlWriter w, Emisor e)
    {
        w.WriteStartElement("Emisor", NsUri);
        El(w, "RUCEmisor", e.Rut);
        El(w, "RznSoc", e.RazonSocial);
        if (!string.IsNullOrWhiteSpace(e.NombreComercial))
            El(w, "NomComercial", e.NombreComercial);
        if (!string.IsNullOrWhiteSpace(e.Giro))
            El(w, "GiroEmis", e.Giro);
        foreach (var tel in e.Telefonos)
            El(w, "Telefono", tel);
        if (!string.IsNullOrWhiteSpace(e.Correo))
            El(w, "CorreoEmisor", e.Correo);
        if (!string.IsNullOrWhiteSpace(e.Sucursal))
            El(w, "EmiSucursal", e.Sucursal);
        El(w, "CdgDGISucur", e.CodigoSucursal.ToString(CultureInfo.InvariantCulture));
        El(w, "DomFiscal", e.DomicilioFiscal);
        El(w, "Ciudad", e.Ciudad);
        El(w, "Departamento", e.Departamento);
        if (!string.IsNullOrWhiteSpace(e.InfoAdicional))
            El(w, "InfoAdicionalEmisor", e.InfoAdicional);
        w.WriteEndElement(); // Emisor
    }

    private static void EscribirReceptor(XmlWriter w, Receptor r, FamiliaCfe familia)
    {
        var conDocumento = !string.IsNullOrWhiteSpace(r.Documento);
        var pais = string.IsNullOrWhiteSpace(r.CodigoPais) ? "UY" : r.CodigoPais.ToUpperInvariant();

        w.WriteStartElement("Receptor", NsUri);

        if (conDocumento)
        {
            if (familia == FamiliaCfe.Factura)
            {
                // Receptor_Fact: documento fijo RUC (2) de Uruguay.
                El(w, "TipoDocRecep", "2");
                El(w, "CodPaisRecep", "UY");
                El(w, "DocRecep", r.Documento!);
            }
            else
            {
                El(w, "TipoDocRecep", TipoDocumentoDgi(r.TipoDocumento));
                El(w, "CodPaisRecep", pais);
                El(w, pais == "UY" ? "DocRecep" : "DocRecepExt", r.Documento!);
            }
        }

        if (!string.IsNullOrWhiteSpace(r.RazonSocial))
            El(w, "RznSocRecep", r.RazonSocial);
        if (!string.IsNullOrWhiteSpace(r.Direccion))
            El(w, "DirRecep", r.Direccion);
        if (!string.IsNullOrWhiteSpace(r.Ciudad))
            El(w, "CiudadRecep", r.Ciudad);
        if (!string.IsNullOrWhiteSpace(r.Departamento))
            El(w, "DeptoRecep", r.Departamento);
        if (!string.IsNullOrWhiteSpace(r.Pais))
            El(w, "PaisRecep", r.Pais);
        if (!string.IsNullOrWhiteSpace(r.CodigoPostal))
            El(w, "CP", r.CodigoPostal);
        if (!string.IsNullOrWhiteSpace(r.InfoAdicional))
            El(w, "InfoAdicional", r.InfoAdicional);
        if (familia != FamiliaCfe.Resguardo)
        {
            if (!string.IsNullOrWhiteSpace(r.LugarDestinoEntrega))
                El(w, "LugarDestEnt", r.LugarDestinoEntrega);
            if (!string.IsNullOrWhiteSpace(r.CompraId))
                El(w, "CompraID", r.CompraId);
        }

        w.WriteEndElement(); // Receptor
    }

    /// <summary>Código de tipo de documento de la DGI (<c>DocType</c>).</summary>
    private static string TipoDocumentoDgi(TipoDocumentoReceptor tipo) => tipo switch
    {
        TipoDocumentoReceptor.Rut => "2",
        TipoDocumentoReceptor.CedulaIdentidad => "3",
        TipoDocumentoReceptor.Pasaporte => "5",
        TipoDocumentoReceptor.Dni => "6",
        TipoDocumentoReceptor.Nife => "7",
        _ => "4",
    };

    private static void EscribirTotales(XmlWriter w, Datos d)
    {
        var (cfe, t) = (d.Cfe, d.Totales);
        w.WriteStartElement("Totales", NsUri);

        switch (d.Familia)
        {
            case FamiliaCfe.Remito:
                El(w, "CantLinDet", Cant(cfe));
                break;

            case FamiliaCfe.RemitoExportacion:
            case FamiliaCfe.FacturaExportacion:
                EscribirMoneda(w, cfe);
                El(w, "MntExpoyAsim", M2(t.MontoExportacion));
                El(w, "MntTotal", M2(t.MontoTotal));
                El(w, "CantLinDet", Cant(cfe));
                if (d.Familia == FamiliaCfe.FacturaExportacion)
                    El(w, "MntPagar", M2(t.MontoTotal));
                break;

            case FamiliaCfe.Resguardo:
                EscribirMoneda(w, cfe);
                El(w, "MntTotRetenido", M2(t.MontoRetenido));
                El(w, "CantLinDet", Cant(cfe));
                foreach (var grupo in cfe.Detalle.SelectMany(l => l.Retenciones).GroupBy(r => r.Codigo))
                {
                    w.WriteStartElement("RetencPercep", NsUri);
                    El(w, "CodRet", grupo.Key);
                    El(w, "ValRetPerc", M2(grupo.Sum(r => CfeFormat.Redondear(r.Valor))));
                    w.WriteEndElement();
                }
                break;

            default:
                EscribirMoneda(w, cfe);
                if (t.MontoNetoExento > 0)
                    El(w, "MntNoGrv", M2(t.MontoNetoExento));
                if (t.MontoNetoSuspendido > 0)
                    El(w, "MntIVaenSusp", M2(t.MontoNetoSuspendido));
                if (t.MontoNetoMinimo > 0)
                    El(w, "MntNetoIvaTasaMin", M2(t.MontoNetoMinimo));
                if (t.MontoNetoBasico > 0)
                    El(w, "MntNetoIVATasaBasica", M2(t.MontoNetoBasico));
                if (t.MontoNetoMinimo > 0)
                    El(w, "IVATasaMin", Tasa(Totales.TasaIvaMinima));
                if (t.MontoNetoBasico > 0)
                    El(w, "IVATasaBasica", Tasa(Totales.TasaIvaBasica));
                if (t.MontoNetoMinimo > 0)
                    El(w, "MntIVATasaMin", M2(t.IvaMinimo));
                if (t.MontoNetoBasico > 0)
                    El(w, "MntIVATasaBasica", M2(t.IvaBasico));
                El(w, "MntTotal", M2(t.MontoTotal));
                El(w, "CantLinDet", Cant(cfe));
                El(w, "MntPagar", M2(t.MontoTotal));
                break;
        }

        w.WriteEndElement(); // Totales
    }

    private static void EscribirMoneda(XmlWriter w, Cfe cfe)
    {
        El(w, "TpoMoneda", cfe.Moneda.CodigoIso());
        if (cfe.Moneda != Moneda.PesoUruguayo && cfe.TipoCambio.HasValue)
            El(w, "TpoCambio", CfeFormat.DecimalInvariant(cfe.TipoCambio.Value, "F3"));
    }

    private static void EscribirLinea(XmlWriter w, LineaDetalle linea, int nro, FamiliaCfe familia)
    {
        w.WriteStartElement("Item", NsUri);
        El(w, "NroLinDet", nro.ToString(CultureInfo.InvariantCulture));

        if (familia == FamiliaCfe.Resguardo)
        {
            foreach (var ret in linea.Retenciones)
            {
                w.WriteStartElement("RetencPercep", NsUri);
                El(w, "CodRet", ret.Codigo);
                if (ret.Tasa.HasValue)
                    El(w, "Tasa", CfeFormat.DecimalInvariant(ret.Tasa.Value, "F3"));
                El(w, "MntSujetoaRet", M2(ret.MontoSujeto));
                if (!string.IsNullOrWhiteSpace(ret.InfoAdicional))
                    El(w, "InfoAdicionalRet", ret.InfoAdicional);
                El(w, "ValRetPerc", M2(ret.Valor));
                w.WriteEndElement();
            }
            w.WriteEndElement();
            return;
        }

        foreach (var cod in linea.Codigos)
        {
            w.WriteStartElement("CodItem", NsUri);
            El(w, "TpoCod", cod.Tipo);
            El(w, "Cod", cod.Codigo);
            w.WriteEndElement();
        }

        // Item_Rem no lleva IndFact ni montos; Item_Rem_Exp no admite IndFact 10 ni descuentos/recargos.
        var esRemito = familia == FamiliaCfe.Remito;
        var conIndFact = familia is not (FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion);

        if (conIndFact)
            El(w, "IndFact", familia == FamiliaCfe.FacturaExportacion ? "10" : IndFact(linea.IndFactIva));
        El(w, "NomItem", linea.NombreItem);
        if (!string.IsNullOrWhiteSpace(linea.Descripcion))
            El(w, "DscItem", linea.Descripcion);
        El(w, "Cantidad", CfeFormat.DecimalInvariant(linea.Cantidad, "F3"));
        El(w, "UniMed", string.IsNullOrWhiteSpace(linea.UnidadMedida) ? "N/A" : linea.UnidadMedida);

        if (!esRemito)
        {
            El(w, "PrecioUnitario", CfeFormat.DecimalInvariant(linea.PrecioUnitario, "F6"));
            if (conIndFact)
            {
                if (linea.DescuentoPorcentaje is > 0)
                    El(w, "DescuentoPct", CfeFormat.DecimalInvariant(linea.DescuentoPorcentaje.Value, "F3"));
                if (linea.DescuentoEfectivo > 0)
                    El(w, "DescuentoMonto", M2(linea.DescuentoEfectivo));
                if (linea.RecargoMonto > 0)
                    El(w, "RecargoMnt", M2(linea.RecargoMonto));
            }
            El(w, "MontoItem", M2(linea.MontoTotal));
        }

        w.WriteEndElement(); // Item
    }

    /// <summary>Indicador de facturación (<c>IndFact</c>) de un tipo de IVA.</summary>
    private static string IndFact(TipoIva iva) => iva switch
    {
        TipoIva.Exento => "1",
        TipoIva.Minimo => "2",
        TipoIva.Basico => "3",
        TipoIva.Suspendido => "12", // IVA en suspenso
        _ => throw new ArgumentOutOfRangeException(nameof(iva), iva, "Indicador de IVA no soportado."),
    };

    private static void EscribirDescuentosRecargos(XmlWriter w, Cfe cfe, FamiliaCfe familia)
    {
        w.WriteStartElement("DscRcgGlobal", NsUri);
        for (int i = 0; i < cfe.DescuentosRecargos.Count; i++)
        {
            var d = cfe.DescuentosRecargos[i];
            w.WriteStartElement("DRG_Item", NsUri);
            El(w, "NroLinDR", (i + 1).ToString(CultureInfo.InvariantCulture));
            El(w, "TpoMovDR", d.Tipo == TipoMovimientoGlobal.Descuento ? "D" : "R");
            El(w, "TpoDR", "1"); // monto
            if (d.Codigo.HasValue)
                El(w, "CodDR", d.Codigo.Value.ToString(CultureInfo.InvariantCulture));
            El(w, "GlosaDR", d.Glosa);
            El(w, "ValorDR", M2(d.Monto));
            El(w, "IndFactDR", familia == FamiliaCfe.FacturaExportacion ? "10" : IndFact(d.IndFact));
            w.WriteEndElement();
        }
        w.WriteEndElement(); // DscRcgGlobal
    }

    private static void EscribirMediosPago(XmlWriter w, Cfe cfe)
    {
        w.WriteStartElement("MediosPago", NsUri);
        for (int i = 0; i < cfe.MediosPago.Count; i++)
        {
            var m = cfe.MediosPago[i];
            w.WriteStartElement("MedioPago", NsUri);
            El(w, "NroLinMP", (i + 1).ToString(CultureInfo.InvariantCulture));
            if (m.Codigo.HasValue)
                El(w, "CodMP", m.Codigo.Value.ToString(CultureInfo.InvariantCulture));
            El(w, "GlosaMP", m.Glosa);
            if (m.Orden.HasValue)
                El(w, "OrdenMP", m.Orden.Value.ToString(CultureInfo.InvariantCulture));
            El(w, "ValorPago", M2(m.Valor));
            w.WriteEndElement();
        }
        w.WriteEndElement(); // MediosPago
    }

    private static void EscribirComplementoFiscal(XmlWriter w, Mandante m, Emisor emisor)
    {
        w.WriteStartElement("Compl_Fiscal", NsUri);
        w.WriteStartElement("Compl_Fiscal_Data", NsUri);
        El(w, "RUCEmisor", emisor.Rut);
        El(w, "TipoDocMdte", TipoDocumentoDgi(m.TipoDocumento));
        El(w, "Pais", m.CodigoPais.ToUpperInvariant());
        El(w, "DocMdte", m.Documento);
        El(w, "NombreMdte", m.Nombre);
        w.WriteEndElement();
        w.WriteEndElement();
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
            El(w, "TpoMonedaRef", r.MonedaCfeRef.Value.CodigoIso());
        if (r.TipoCambioCfeRef.HasValue)
            El(w, "TpoCambioRef", CfeFormat.DecimalInvariant(r.TipoCambioCfeRef.Value, "F3"));
        w.WriteEndElement(); // Referencia
    }

    private static void EscribirCaeData(XmlWriter w, Cae cae)
    {
        w.WriteStartElement("CAEData", NsUri);
        El(w, "CAE_ID", cae.NumeroAutorizacion);
        El(w, "DNro", cae.RangoDesde.ToString(CultureInfo.InvariantCulture));
        El(w, "HNro", cae.RangoHasta.ToString(CultureInfo.InvariantCulture));
        El(w, "FecVenc", CfeFormat.DateIso(cae.FechaVencimiento));
        w.WriteEndElement(); // CAEData
    }

    // -------------------------------------------------------------------------

    private static string Int(Enum value) =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string Cant(Cfe cfe) => cfe.Detalle.Count.ToString(CultureInfo.InvariantCulture);
}
