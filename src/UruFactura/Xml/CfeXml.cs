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
        FamiliaCfe.Boleta => "eBoleta",
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
            EscribirLinea(w, cfe, cfe.Detalle[i], i + 1, d.Familia);
        w.WriteEndElement(); // Detalle

        if (cfe.SubtotalesInformativos.Count > 0)
            EscribirSubtotales(w, cfe);

        if (cfe.DescuentosRecargos.Count > 0)
            EscribirDescuentosRecargos(w, cfe);

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

    /// <summary>
    /// <c>IdDoc</c> en el orden de cada familia (<c>IdDoc_Tck</c>, <c>IdDoc_Fact</c>, <c>IdDoc_Fact_Exp</c>,
    /// <c>IdDoc_Rem</c>, <c>IdDoc_Rem_Exp</c>, <c>IdDoc_Resg</c> e <c>IdDoc_Boleta</c>).
    /// </summary>
    private static void EscribirIdDoc(XmlWriter w, Datos d)
    {
        var (cfe, familia) = (d.Cfe, d.Familia);
        w.WriteStartElement("IdDoc", NsUri);
        El(w, "TipoCFE", Int(cfe.Tipo));
        El(w, "Serie", d.Cae.Serie);
        El(w, "Nro", d.Numero.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(cfe.NroInterno))
            El(w, "NroInterno", cfe.NroInterno);
        El(w, "FchEmis", CfeFormat.DateIso(cfe.FechaEmision!.Value));

        switch (familia)
        {
            case FamiliaCfe.Remito:
            case FamiliaCfe.RemitoExportacion:
                El(w, "TipoTraslado", TipoTraslado(cfe));
                if (cfe.Propietario is { } propietario)
                    EscribirPropietario(w, propietario);
                if (familia == FamiliaCfe.RemitoExportacion)
                {
                    if (!string.IsNullOrWhiteSpace(cfe.ClausulaVenta))
                        El(w, "ClauVenta", cfe.ClausulaVenta);
                    El(w, "ModVenta", Numero(cfe.ModalidadVenta!.Value));
                    El(w, "ViaTransp", Numero(cfe.ViaTransporte!.Value));
                }
                break;

            case FamiliaCfe.Resguardo:
                if (cfe.FechaValor is { } fechaValor)
                    El(w, "FchValor", CfeFormat.DateIso(fechaValor));
                break;

            default:
                if (cfe.PeriodoDesde is { } desde)
                    El(w, "PeriodoDesde", CfeFormat.DateIso(desde));
                if (cfe.PeriodoHasta is { } hasta)
                    El(w, "PeriodoHasta", CfeFormat.DateIso(hasta));
                if (MontosBrutos(d) is { } brutos)
                    El(w, "MntBruto", brutos);
                El(w, "FmaPago", Int(cfe.FormaPago));
                if (cfe.FechaVencimiento is { } venc)
                    El(w, "FchVenc", CfeFormat.DateIso(venc));
                if (familia == FamiliaCfe.FacturaExportacion)
                {
                    El(w, "ClauVenta", cfe.ClausulaVenta!);
                    if (cfe.ModalidadVenta is { } modalidad)
                        El(w, "ModVenta", Numero(modalidad));
                    El(w, "ViaTransp", Numero(cfe.ViaTransporte!.Value));
                }
                else if (familia != FamiliaCfe.Boleta && cfe.ModalidadVenta is { } modalidad)
                    El(w, "ModVenta", Numero(modalidad));
                break;
        }

        if (!string.IsNullOrWhiteSpace(cfe.InfoAdicional))
            El(w, "InfoAdicionalDoc", cfe.InfoAdicional);

        if (familia is FamiliaCfe.Ticket or FamiliaCfe.Factura or FamiliaCfe.FacturaExportacion && IvaAlDia(cfe, d.Cae) is { } ivaAlDia)
            El(w, "IVAalDia", ivaAlDia);
        if (cfe.SecretoProfesionalInformado is { } secreto)
            El(w, "SecProf", Numero(secreto));
        if (cfe.PagoCuentaTerceros)
            El(w, "IndPagCta3ros", "1");
        if (cfe.CobranzaPropia)
            El(w, "IndCobPropia", "1");
        if (cfe.CompraMonedaExtranjera is { } compra)
            El(w, "IndCompraMEReventa", Int(compra));

        w.WriteEndElement(); // IdDoc
    }

    /// <summary>
    /// <c>MntBruto</c>: 3 en los CFE numerados con un CAE de IVA mínimo o Monotributo (una venta por cuenta ajena de
    /// un mandante IVA mínimo usa el indicador 16 pero no lo informa: la DGI rechaza el 3 por cuenta ajena); si no,
    /// el de <see cref="Cfe.MontosBrutos"/>, o se omite (montos netos).
    /// </summary>
    private static string? MontosBrutos(Datos d) =>
        d.Cae.EsIvaMinimoOMonotributo ? "3"
        : d.Cfe.MontosBrutos is { } brutos ? Int(brutos)
        : null;

    /// <summary>
    /// <c>IVAalDia</c> (A-C17): se informa solo si el emisor (1), el mandante (2) o ambos (3) no tienen la
    /// constancia de estar al día con el IVA. No corresponde a los contribuyentes Monotributo.
    /// </summary>
    internal static string? IvaAlDia(Cfe cfe, Cae cae)
    {
        if (cae.Especial is CaeEspecial.Monotributo or CaeEspecial.MonotributoSocialMides)
            return null;
        var emisor = cfe.Emisor is { IvaAlDia: false };
        var mandante = cfe.Mandante is { IvaAlDia: false };
        return (emisor, mandante) switch
        {
            (true, true) => "3",
            (false, true) => "2",
            (true, false) => "1",
            _ => null,
        };
    }

    /// <summary><c>TipoTraslado</c> del e-Remito: 1 = Venta, 2 = Traslados internos.</summary>
    private static string TipoTraslado(Cfe cfe) =>
        cfe.IndTraslado == IndTraslado.TrasladoPorVenta ? "1" : "2";

    /// <summary>Mercadería de terceros (A-C6.1 a A-C6.6): el indicador y el documento y nombre del propietario.</summary>
    private static void EscribirPropietario(XmlWriter w, Propietario p)
    {
        El(w, "IndPropiedad", "1");
        El(w, "TipoDocProp", Int(p.TipoDocumento));
        El(w, "CodPaisProp", Validacion.PaisDocumento(p.CodigoPais));
        El(w, Validacion.EsDocumentoUruguayo(p.TipoDocumento) ? "DocProp" : "DocPropExt", p.Documento);
        El(w, "RznSocProp", p.Nombre);
    }

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
        w.WriteStartElement("Receptor", NsUri);

        if (!string.IsNullOrWhiteSpace(r.Documento))
        {
            // Los documentos uruguayos (NIE, RUT y C.I.) van en DocRecep; el resto, en DocRecepExt (A-C62 y A-C62.1).
            // Receptor_Fact no tiene DocRecepExt: el de la e-Factura es siempre un RUT.
            El(w, "TipoDocRecep", Int(r.TipoDocumento));
            El(w, "CodPaisRecep", Validacion.PaisDocumento(r.CodigoPais));
            El(w, Validacion.EsDocumentoUruguayo(r.TipoDocumento) ? "DocRecep" : "DocRecepExt", r.Documento!);
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
                EscribirMoneda(w, cfe);
                El(w, "MntExpoyAsim", M2(t.MontoExportacion));
                El(w, "MntTotal", M2(t.MontoTotal));
                El(w, "CantLinDet", Cant(cfe));
                break;

            case FamiliaCfe.FacturaExportacion:
                EscribirMoneda(w, cfe);
                El(w, "MntExpoyAsim", M2(t.MontoExportacion));
                El(w, "MntTotal", M2(t.MontoTotal));
                El(w, "CantLinDet", Cant(cfe));
                if (t.MontoNoFacturable != 0)
                    El(w, "MontoNF", M2(t.MontoNoFacturable));
                El(w, "MntPagar", M2(t.MontoPagar));
                break;

            case FamiliaCfe.Resguardo:
                EscribirMoneda(w, cfe);
                El(w, "MntTotRetenido", M2(t.MontoRetenido));
                if (t.MontoCreditoFiscal != 0)
                    El(w, "MntTotCredFisc", M2(t.MontoCreditoFiscal));
                El(w, "CantLinDet", Cant(cfe));
                EscribirRetencionesTotales(w, cfe);
                break;

            case FamiliaCfe.Boleta:
                EscribirMoneda(w, cfe);
                if (t.MontoNoGravado > 0)
                    El(w, "MntNoGrv", M2(t.MontoNoGravado));
                if (t.MontoNetoSuspendido > 0)
                    El(w, "MntIVaenSusp", M2(t.MontoNetoSuspendido));
                El(w, "MntTotal", M2(t.MontoTotal));
                if (t.MontoRetenido != 0)
                    El(w, "MntTotRetenido", M2(t.MontoRetenido));
                El(w, "CantLinDet", Cant(cfe));
                EscribirRetencionesTotales(w, cfe);
                if (t.MontoNoFacturable != 0)
                    El(w, "MontoNF", M2(t.MontoNoFacturable));
                El(w, "MntPagar", M2(t.MontoPagar));
                break;

            default:
                EscribirMoneda(w, cfe);
                if (t.MontoNoGravado > 0)
                    El(w, "MntNoGrv", M2(t.MontoNoGravado));
                if (t.MontoExportacion > 0)
                    El(w, "MntExpoyAsim", M2(t.MontoExportacion));
                if (t.MontoImpuestoPercibido > 0)
                    El(w, "MntImpuestoPerc", M2(t.MontoImpuestoPercibido));
                if (t.MontoNetoSuspendido > 0)
                    El(w, "MntIVaenSusp", M2(t.MontoNetoSuspendido));
                if (t.MontoNetoMinimo > 0)
                    El(w, "MntNetoIvaTasaMin", M2(t.MontoNetoMinimo));
                if (t.MontoNetoBasico > 0)
                    El(w, "MntNetoIVATasaBasica", M2(t.MontoNetoBasico));
                if (t.MontoNetoOtraTasa > 0)
                    El(w, "MntNetoIVAOtra", M2(t.MontoNetoOtraTasa));
                if (t.MontoNetoMinimo > 0)
                    El(w, "IVATasaMin", Tasa(Totales.TasaIvaMinima));
                if (t.MontoNetoBasico > 0)
                    El(w, "IVATasaBasica", Tasa(Totales.TasaIvaBasica));
                if (t.MontoNetoMinimo > 0)
                    El(w, "MntIVATasaMin", M2(t.IvaMinimo));
                if (t.MontoNetoBasico > 0)
                    El(w, "MntIVATasaBasica", M2(t.IvaBasico));
                if (t.IvaOtraTasa > 0)
                    El(w, "MntIVAOtra", M2(t.IvaOtraTasa));
                El(w, "MntTotal", M2(t.MontoTotal));
                if (t.MontoRetenido != 0)
                    El(w, "MntTotRetenido", M2(t.MontoRetenido));
                if (t.MontoCreditoFiscal != 0)
                    El(w, "MntTotCredFisc", M2(t.MontoCreditoFiscal));
                El(w, "CantLinDet", Cant(cfe));
                EscribirRetencionesTotales(w, cfe);
                // Con cobranza propia deben existir MontoNF y MntPagar (A-C129 y A-C130).
                if (t.MontoNoFacturable != 0 || cfe.CobranzaPropia)
                    El(w, "MontoNF", M2(t.MontoNoFacturable));
                El(w, "MntPagar", M2(t.MontoPagar));
                break;
        }

        w.WriteEndElement(); // Totales
    }

    /// <summary>Retenciones, percepciones y créditos fiscales acumulados por código (A-C127 y A-C128).</summary>
    private static void EscribirRetencionesTotales(XmlWriter w, Cfe cfe)
    {
        foreach (var (codigo, valor) in cfe.RetencionesPorCodigo())
        {
            w.WriteStartElement("RetencPercep", NsUri);
            El(w, "CodRet", codigo);
            El(w, "ValRetPerc", M2(valor));
            w.WriteEndElement();
        }
    }

    private static void EscribirMoneda(XmlWriter w, Cfe cfe)
    {
        El(w, "TpoMoneda", cfe.Moneda.Codigo);
        if (cfe.Moneda != Moneda.PesoUruguayo && cfe.TipoCambio.HasValue)
            El(w, "TpoCambio", CfeFormat.DecimalInvariant(cfe.TipoCambio.Value, "F3"));
    }

    /// <summary>
    /// <c>Item</c> en el orden de cada familia (<c>Item_Det_Fact</c>, <c>Item_Det_Fact_Exp</c>, <c>Item_Rem</c>,
    /// <c>Item_Rem_Exp</c>, <c>Item_Resg</c> e <c>Item_Det_Boleta</c>).
    /// </summary>
    private static void EscribirLinea(XmlWriter w, Cfe cfe, LineaDetalle linea, int nro, FamiliaCfe familia)
    {
        w.WriteStartElement("Item", NsUri);
        El(w, "NroLinDet", nro.ToString(CultureInfo.InvariantCulture));

        if (familia == FamiliaCfe.Resguardo)
        {
            if (cfe.IndicadorEscrito(linea) is { } anulacion)
                El(w, "IndFact", Int(anulacion));
            foreach (var ret in linea.Retenciones)
                EscribirRetencion(w, ret, conMontoSujeto: true);
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

        if (cfe.IndicadorEscrito(linea) is { } indicador)
            El(w, "IndFact", Int(indicador));
        if (linea.AgenteResponsable is { } agente)
            El(w, "IndAgenteResp", agente == AgenteResponsable.Producto ? "R" : "A");
        El(w, "NomItem", linea.NombreItem);
        if (!string.IsNullOrWhiteSpace(linea.Ncm))
            El(w, "NCM", linea.Ncm);
        if (!string.IsNullOrWhiteSpace(linea.Descripcion))
            El(w, "DscItem", linea.Descripcion);
        El(w, "Cantidad", CfeFormat.DecimalInvariant(linea.Cantidad, "F3"));
        El(w, "UniMed", string.IsNullOrWhiteSpace(linea.UnidadMedida) ? "N/A" : linea.UnidadMedida);

        if (familia != FamiliaCfe.Remito)
        {
            El(w, "PrecioUnitario", CfeFormat.DecimalInvariant(linea.PrecioUnitario, "F6"));
            if (familia != FamiliaCfe.RemitoExportacion)
            {
                // Si se informa el porcentaje, debe ir el monto (B-C12 y B-C13, B-C16 y B-C17).
                if (linea.DescuentoPorcentaje is > 0)
                    El(w, "DescuentoPct", CfeFormat.DecimalInvariant(linea.DescuentoPorcentaje.Value, "F3"));
                if (linea.DescuentoPorcentaje is > 0 || linea.DescuentoEfectivo > 0)
                    El(w, "DescuentoMonto", M2(linea.DescuentoEfectivo));
                EscribirDistribucion(w, "SubDescuento", "DescTipo", "DescVal", linea.Subdescuentos);
                if (linea.RecargoPorcentaje is > 0)
                    El(w, "RecargoPct", CfeFormat.DecimalInvariant(linea.RecargoPorcentaje.Value, "F3"));
                if (linea.RecargoPorcentaje is > 0 || linea.RecargoEfectivo > 0)
                    El(w, "RecargoMnt", M2(linea.RecargoEfectivo));
                EscribirDistribucion(w, "SubRecargo", "RecargoTipo", "RecargoVal", linea.Subrecargos);
                foreach (var ret in linea.Retenciones)
                    EscribirRetencion(w, ret, conMontoSujeto: false);
            }
            El(w, "MontoItem", M2(linea.MontoTotal));
        }

        w.WriteEndElement(); // Item
    }

    /// <summary>Una retención o percepción del ítem (<c>RetPerc</c> o <c>RetPerc_Resg</c>, donde el monto sujeto es obligatorio).</summary>
    private static void EscribirRetencion(XmlWriter w, RetencionPercepcion ret, bool conMontoSujeto)
    {
        w.WriteStartElement("RetencPercep", NsUri);
        El(w, "CodRet", ret.Codigo);
        if (ret.Tasa.HasValue)
            El(w, "Tasa", CfeFormat.DecimalInvariant(ret.Tasa.Value, "F3"));
        if (conMontoSujeto || ret.MontoSujeto != 0)
            El(w, "MntSujetoaRet", M2(ret.MontoSujeto));
        if (!string.IsNullOrWhiteSpace(ret.InfoAdicional))
            El(w, "InfoAdicionalRet", ret.InfoAdicional);
        El(w, "ValRetPerc", M2(ret.Valor));
        w.WriteEndElement();
    }

    private static void EscribirDistribucion(XmlWriter w, string elemento, string tipo, string valor, IReadOnlyList<DistribucionAjuste> partes)
    {
        foreach (var parte in partes)
        {
            w.WriteStartElement(elemento, NsUri);
            El(w, tipo, Int(parte.Tipo));
            El(w, valor, M2(parte.Valor));
            w.WriteEndElement();
        }
    }

    private static void EscribirSubtotales(XmlWriter w, Cfe cfe)
    {
        w.WriteStartElement("SubTotInfo", NsUri);
        for (int i = 0; i < cfe.SubtotalesInformativos.Count; i++)
        {
            var s = cfe.SubtotalesInformativos[i];
            w.WriteStartElement("STI_Item", NsUri);
            El(w, "NroSTI", (i + 1).ToString(CultureInfo.InvariantCulture));
            El(w, "GlosaSTI", s.Glosa);
            if (s.Orden.HasValue)
                El(w, "OrdenSTI", Numero(s.Orden.Value));
            El(w, "ValSubtotSTI", M2(s.Valor));
            w.WriteEndElement();
        }
        w.WriteEndElement(); // SubTotInfo
    }

    private static void EscribirDescuentosRecargos(XmlWriter w, Cfe cfe)
    {
        w.WriteStartElement("DscRcgGlobal", NsUri);
        for (int i = 0; i < cfe.DescuentosRecargos.Count; i++)
        {
            var d = cfe.DescuentosRecargos[i];
            w.WriteStartElement("DRG_Item", NsUri);
            El(w, "NroLinDR", (i + 1).ToString(CultureInfo.InvariantCulture));
            El(w, "TpoMovDR", d.Tipo == TipoMovimientoGlobal.Descuento ? "D" : "R");
            El(w, "TpoDR", "1"); // monto (el porcentaje se informa ya aplicado)
            if (d.Codigo.HasValue)
                El(w, "CodDR", d.Codigo.Value.ToString(CultureInfo.InvariantCulture));
            El(w, "GlosaDR", d.Glosa);
            El(w, "ValorDR", M2(cfe.MontoInformado(d)));
            El(w, "IndFactDR", Int(cfe.IndicadorInformado(d.IndFact)));
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
        El(w, "TipoDocMdte", Int(m.TipoDocumento));
        El(w, "Pais", Validacion.PaisDocumento(m.CodigoPais));
        El(w, "DocMdte", m.Documento);
        El(w, "NombreMdte", m.Nombre);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void EscribirReferencia(XmlWriter w, RefCfe r, int nro)
    {
        w.WriteStartElement("Referencia", NsUri);
        El(w, "NroLinRef", nro.ToString(CultureInfo.InvariantCulture));
        if (r.Global)
            El(w, "IndGlobal", "1");
        if (r.TipoCfe is { } tipo)
            El(w, "TpoDocRef", Int(tipo));
        if (!string.IsNullOrEmpty(r.Serie))
            El(w, "Serie", r.Serie);
        if (r.NroCfe is { } nroCfe)
            El(w, "NroCFERef", nroCfe.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(r.Razon))
            El(w, "RazonRef", r.Razon);
        if (r.FechaCfe is { } fecha)
            El(w, "FechaCFEref", CfeFormat.DateIso(fecha));
        if (r.MontoCfeRef.HasValue)
            El(w, "MntCFEref", M2(r.MontoCfeRef.Value));
        if (r.MonedaCfeRef is { } moneda)
            El(w, "TpoMonedaRef", moneda.Codigo);
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
        if (cae.Especial is { } especial)
            El(w, "CAEEspecial", Int(especial));
        if (cae.CausalEspecial is { } causal)
            El(w, "CausalCAEEsp", causal.ToString(CultureInfo.InvariantCulture));
        w.WriteEndElement(); // CAEData
    }

    // -------------------------------------------------------------------------

    private static string Int(Enum value) =>
        Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    private static string Numero(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    private static string Cant(Cfe cfe) => cfe.Detalle.Count.ToString(CultureInfo.InvariantCulture);
}
