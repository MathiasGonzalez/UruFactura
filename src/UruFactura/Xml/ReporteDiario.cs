using System.Globalization;
using System.Xml;
using UruFactura.Formatting;
using static UruFactura.Xml.XmlCfe;

namespace UruFactura.Xml;

/// <summary>
/// Arma el XML del Reporte Diario (<c>Reporte</c>, esquema <c>ReporteDiarioCFE.xsd</c>):
/// un resumen por tipo de comprobante con montos totales, cantidad y rangos de números
/// utilizados. No contiene los CFE; el XML resultante se firma antes de enviarse.
/// </summary>
internal static class ReporteDiario
{
    // Orden y nombre de los resúmenes tal como los exige el XSD.
    private static readonly (TipoCfe Tipo, string Elemento, Resumen Clase)[] Resumenes =
    [
        (TipoCfe.ETicket,                                   "Rsmn_Tck",                           Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoETicket,                        "Rsmn_Tck_Nota_Credito",              Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoETicket,                         "Rsmn_Tck_Nota_Debito",               Resumen.FacturaYTicket),
        (TipoCfe.ETicketContingencia,                       "Rsmn_Tck_Cont",                      Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoETicketContingencia,            "Rsmn_Tck_Cont_Nota_Credito",         Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoETicketContingencia,             "Rsmn_Tck_Cont_Nota_Debito",          Resumen.FacturaYTicket),
        (TipoCfe.ETicketCuentaAjena,                        "Rsmn_Tck_CtaAj",                     Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoETicketCuentaAjena,             "Rsmn_Tck_Nota_Credito_CtaAj",        Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoETicketCuentaAjena,              "Rsmn_Tck_Nota_Debito_CtaAj",         Resumen.FacturaYTicket),
        (TipoCfe.ETicketCuentaAjenaContingencia,            "Rsmn_Tck_CtaAj_Cont",                Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoETicketCuentaAjenaContingencia, "Rsmn_Tck_CtaAj_Cont_Nota_Credito",   Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoETicketCuentaAjenaContingencia,  "Rsmn_Tck_CtaAj_Cont_Nota_Debito",    Resumen.FacturaYTicket),
        (TipoCfe.EFactura,                                  "Rsmn_Fac",                           Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoEFactura,                       "Rsmn_Fac_Nota_Credito",              Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoEFactura,                        "Rsmn_Fac_Nota_Debito",               Resumen.FacturaYTicket),
        (TipoCfe.EFacturaContingencia,                      "Rsmn_Fac_Cont",                      Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoEFacturaContingencia,           "Rsmn_Fac_Cont_Nota_Credito",         Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoEFacturaContingencia,            "Rsmn_Fac_Cont_Nota_Debito",          Resumen.FacturaYTicket),
        (TipoCfe.EFacturaCuentaAjena,                       "Rsmn_Fac_CtaAj",                     Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoEFacturaCuentaAjena,            "Rsmn_Fac_Nota_Credito_CtaAj",        Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoEFacturaCuentaAjena,             "Rsmn_Fac_Nota_Debito_CtaAj",         Resumen.FacturaYTicket),
        (TipoCfe.EFacturaCuentaAjenaContingencia,           "Rsmn_Fac_CtaAj_Cont",                Resumen.FacturaYTicket),
        (TipoCfe.NotaCreditoEFacturaCuentaAjenaContingencia,"Rsmn_Fac_CtaAj_Cont_Nota_Credito",   Resumen.FacturaYTicket),
        (TipoCfe.NotaDebitoEFacturaCuentaAjenaContingencia, "Rsmn_Fac_CtaAj_Cont_Nota_Debito",    Resumen.FacturaYTicket),
        (TipoCfe.EFacturaExportacion,                       "Rsmn_Fac_Exp",                       Resumen.Exportacion),
        (TipoCfe.NotaCreditoEFacturaExportacion,            "Rsmn_Fac_Exp_Nota_Credito",          Resumen.Exportacion),
        (TipoCfe.NotaDebitoEFacturaExportacion,             "Rsmn_Fac_Exp_Nota_Debito",           Resumen.Exportacion),
        (TipoCfe.EFacturaExportacionContingencia,           "Rsmn_Fac_Exp_Cont",                  Resumen.Exportacion),
        (TipoCfe.NotaCreditoEFacturaExportacionContingencia,"Rsmn_Fac_Exp_Cont_Nota_Credito",     Resumen.Exportacion),
        (TipoCfe.NotaDebitoEFacturaExportacionContingencia, "Rsmn_Fac_Exp_Cont_Nota_Debito",      Resumen.Exportacion),
        (TipoCfe.ERemito,                                   "Rsmn_Rem",                           Resumen.Remito),
        (TipoCfe.ERemitoContingencia,                       "Rsmn_Rem_Cont",                      Resumen.Remito),
        (TipoCfe.ERemitoExportacion,                        "Rsmn_Rem_Exp",                       Resumen.RemitoExportacion),
        (TipoCfe.ERemitoExportacionContingencia,            "Rsmn_Rem_Exp_Cont",                  Resumen.RemitoExportacion),
        (TipoCfe.EResguardo,                                "Rsmn_Resg",                          Resumen.Resguardo),
        (TipoCfe.EResguardoContingencia,                    "Rsmn_Resg_Cont",                     Resumen.Resguardo),
    ];

    private enum Resumen { FacturaYTicket, Exportacion, Remito, RemitoExportacion, Resguardo }

    /// <summary>
    /// Genera el reporte (sin firmar) con los CFE emitidos en <paramref name="fecha"/>.
    /// </summary>
    /// <param name="rutEmisor">RUT del emisor.</param>
    /// <param name="fecha">Fecha que resume el reporte.</param>
    /// <param name="secEnvio">Secuencia del envío del día (0 a 99; 0 si es el primero).</param>
    /// <param name="idEmisor">Número de envío asignado por el emisor (opcional).</param>
    /// <param name="tmstFirmaEnv">Fecha y hora de generación del reporte.</param>
    /// <param name="cfes">CFE emitidos en la fecha (si no hay, se informa un reporte vacío).</param>
    internal static string Generar(
        string rutEmisor,
        DateOnly fecha,
        int secEnvio,
        long? idEmisor,
        DateTimeOffset tmstFirmaEnv,
        IReadOnlyCollection<CfeEmitido> cfes)
    {
        if (secEnvio is < 0 or > 99)
            throw new UruFacturaException("SecEnvio debe estar entre 0 y 99.");

        var delDia = cfes.Where(c => c.FechaEmision == fecha).ToList();
        var noSoportados = delDia.Where(c => Resumenes.All(r => r.Tipo != c.Tipo)).Select(c => c.Tipo).Distinct().ToList();
        if (noSoportados.Count > 0)
            throw new UruFacturaException(
                $"El reporte diario no soporta los tipos: {string.Join(", ", noSoportados)}.");

        return Escribir(w =>
        {
            w.WriteStartDocument();
            w.WriteStartElement("Reporte", CfeXml.NsUri);

            w.WriteStartElement("Caratula", CfeXml.NsUri);
            w.WriteAttributeString("version", "1.0");
            El(w, "RUCEmisor", rutEmisor);
            El(w, "FechaResumen", CfeFormat.DateIso(fecha));
            if (idEmisor.HasValue)
                El(w, "IDEmisor", idEmisor.Value.ToString(CultureInfo.InvariantCulture));
            El(w, "SecEnvio", secEnvio.ToString(CultureInfo.InvariantCulture));
            El(w, "TmstFirmaEnv", CfeXml.TimestampUruguay(tmstFirmaEnv));
            El(w, "CantComprobantes", delDia.Count.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();

            foreach (var (tipo, elemento, clase) in Resumenes)
            {
                var grupo = delDia.Where(c => c.Tipo == tipo).ToList();
                if (grupo.Count == 0)
                    continue;

                w.WriteStartElement(elemento, CfeXml.NsUri);
                El(w, "TipoComp", ((int)tipo).ToString(CultureInfo.InvariantCulture));
                w.WriteStartElement("RsmnData", CfeXml.NsUri);
                EscribirMontos(w, grupo, clase);
                if (tipo.EsContingencia())
                {
                    // Los CFC (RsmnData_*_Cont) no informan rangos: solo la cantidad emitida.
                    El(w, "CantCFCEmi", grupo.Count.ToString(CultureInfo.InvariantCulture));
                }
                else
                {
                    El(w, "CantDocsUtil", grupo.Count.ToString(CultureInfo.InvariantCulture));
                    EscribirRangos(w, grupo);
                }
                w.WriteEndElement(); // RsmnData
                w.WriteEndElement(); // Rsmn_*
            }

            w.WriteEndElement(); // Reporte
            w.WriteEndDocument();
        });
    }

    private static void EscribirMontos(XmlWriter w, List<CfeEmitido> grupo, Resumen clase)
    {
        var porDiaYSucursal = grupo
            .GroupBy(c => (c.FechaEmision, CodigoSucursal: c.Emisor.CodigoSucursal))
            .OrderBy(g => g.Key.FechaEmision).ThenBy(g => g.Key.CodigoSucursal);

        var item = clase switch
        {
            Resumen.FacturaYTicket => "Mnts_FyT_Item",
            Resumen.Exportacion => "Mnts_FExp_Item",
            Resumen.Remito => "Mnts_Rem_Item",
            Resumen.Resguardo => "Mnts_Res_Item",
            _ => "Mnts_Rem_Exp_Item",
        };

        w.WriteStartElement("Montos", CfeXml.NsUri);
        foreach (var g in porDiaYSucursal)
        {
            w.WriteStartElement(item, CfeXml.NsUri);
            El(w, "Fecha", CfeFormat.DateIso(g.Key.FechaEmision));
            El(w, "CodSuc", g.Key.CodigoSucursal.ToString(CultureInfo.InvariantCulture));

            switch (clase)
            {
                case Resumen.FacturaYTicket:
                    var exento = g.Sum(c => c.Totales.MontoNetoExento);
                    var susp = g.Sum(c => c.Totales.MontoNetoSuspendido);
                    var netoMin = g.Sum(c => c.Totales.MontoNetoMinimo);
                    var netoBas = g.Sum(c => c.Totales.MontoNetoBasico);
                    if (exento > 0) El(w, "TotMntNoGrv", M2(exento));
                    if (susp > 0) El(w, "TotMntIVAenSusp", M2(susp));
                    if (netoMin > 0) El(w, "TotMntIVATasaMin", M2(netoMin));
                    if (netoBas > 0) El(w, "TotMntIVATasaBas", M2(netoBas));
                    if (netoMin > 0) El(w, "MntIVATasaMin", M2(g.Sum(c => c.Totales.IvaMinimo)));
                    if (netoBas > 0) El(w, "MntIVATasaBas", M2(g.Sum(c => c.Totales.IvaBasico)));
                    if (netoMin > 0) El(w, "IVATasaMin", "10.000");
                    if (netoBas > 0) El(w, "IVATasaBas", "22.000");
                    El(w, "TotMntTotal", M2(g.Sum(c => c.Totales.MontoTotal)));
                    break;

                case Resumen.Resguardo:
                    El(w, "TotMntRetenido", M2(g.Sum(c => c.Totales.MontoRetenido)));
                    break;

                case Resumen.Exportacion:
                case Resumen.RemitoExportacion:
                    El(w, "TotMntExpyAsim", M2(g.Sum(c => c.Totales.MontoExportacion)));
                    El(w, "TotMntTotal", M2(g.Sum(c => c.Totales.MontoTotal)));
                    break;
            }

            w.WriteEndElement();
        }
        w.WriteEndElement(); // Montos
    }

    /// <summary>Rangos contiguos de números usados, por serie (<c>RDU_Item</c>).</summary>
    private static void EscribirRangos(XmlWriter w, List<CfeEmitido> grupo)
    {
        w.WriteStartElement("RngDocsUtil", CfeXml.NsUri);
        foreach (var serie in grupo.GroupBy(c => c.Serie).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            // En una lista ordenada y sin repetidos, los números consecutivos comparten (número − posición).
            var rangos = serie.Select(c => c.Numero).Distinct().Order()
                .Select((numero, i) => (numero, tramo: numero - i))
                .GroupBy(x => x.tramo, x => x.numero);

            foreach (var rango in rangos)
            {
                w.WriteStartElement("RDU_Item", CfeXml.NsUri);
                El(w, "Serie", serie.Key);
                El(w, "NroDesde", rango.First().ToString(CultureInfo.InvariantCulture));
                El(w, "NroHasta", rango.Last().ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
        }
        w.WriteEndElement();
    }
}
