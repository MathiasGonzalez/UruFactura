using System.Globalization;
using System.Text;
using System.Xml;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Formatting;
using UruFactura.Models;

namespace UruFactura.Xml;

/// <summary>
/// Arma el XML del Reporte Diario (<c>Reporte</c>, esquema <c>ReporteDiarioCFE.xsd</c>):
/// un resumen por tipo de comprobante con montos totales, cantidad y rangos de números
/// utilizados. No contiene los CFE; el XML resultante debe firmarse con
/// <see cref="Signature.ICfeFirmante"/> antes de enviarse.
/// </summary>
public static class ReporteDiarioBuilder
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
    public static string Generar(
        string rutEmisor,
        DateOnly fecha,
        int secEnvio,
        long? idEmisor,
        DateTimeOffset tmstFirmaEnv,
        IReadOnlyCollection<Cfe> cfes)
    {
        if (secEnvio is < 0 or > 99)
            throw new UruFacturaException("SecEnvio debe estar entre 0 y 99.");

        var delDia = cfes.Where(c => c.FechaEmision == fecha).ToList();
        var noSoportados = delDia.Where(c => Resumenes.All(r => r.Tipo != c.Tipo)).Select(c => c.Tipo).Distinct().ToList();
        if (noSoportados.Count > 0)
            throw new UruFacturaException(
                $"El reporte diario no soporta los tipos: {string.Join(", ", noSoportados)}.");

        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("Reporte", CfeXmlBuilder.NsUri);

            w.WriteStartElement("Caratula", CfeXmlBuilder.NsUri);
            w.WriteAttributeString("version", "1.0");
            El(w, "RUCEmisor", rutEmisor);
            El(w, "FechaResumen", CfeFormat.DateIso(fecha));
            if (idEmisor.HasValue)
                El(w, "IDEmisor", idEmisor.Value.ToString(CultureInfo.InvariantCulture));
            El(w, "SecEnvio", secEnvio.ToString(CultureInfo.InvariantCulture));
            El(w, "TmstFirmaEnv", CfeXmlBuilder.TimestampUruguay(tmstFirmaEnv));
            El(w, "CantComprobantes", delDia.Count.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();

            foreach (var (tipo, elemento, clase) in Resumenes)
            {
                var grupo = delDia.Where(c => c.Tipo == tipo).ToList();
                if (grupo.Count == 0)
                    continue;

                w.WriteStartElement(elemento, CfeXmlBuilder.NsUri);
                El(w, "TipoComp", ((int)tipo).ToString(CultureInfo.InvariantCulture));
                w.WriteStartElement("RsmnData", CfeXmlBuilder.NsUri);
                EscribirMontos(w, grupo, clase);
                El(w, "CantDocsUtil", grupo.Count.ToString(CultureInfo.InvariantCulture));
                EscribirRangos(w, grupo);
                w.WriteEndElement(); // RsmnData
                w.WriteEndElement(); // Rsmn_*
            }

            w.WriteEndElement(); // Reporte
            w.WriteEndDocument();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void EscribirMontos(XmlWriter w, List<Cfe> grupo, Resumen clase)
    {
        var porDiaYSucursal = grupo
            .GroupBy(c => (c.FechaEmision, c.CodigoSucursal))
            .OrderBy(g => g.Key.FechaEmision).ThenBy(g => g.Key.CodigoSucursal);

        w.WriteStartElement("Montos", CfeXmlBuilder.NsUri);
        foreach (var g in porDiaYSucursal)
        {
            var nombre = clase switch
            {
                Resumen.FacturaYTicket => "Mnts_FyT_Item",
                Resumen.Exportacion => "Mnts_FExp_Item",
                Resumen.Remito => "Mnts_Rem_Item",
                Resumen.Resguardo => "Mnts_Res_Item",
                _ => "Mnts_Rem_Exp_Item",
            };

            w.WriteStartElement(nombre, CfeXmlBuilder.NsUri);
            El(w, "Fecha", CfeFormat.DateIso(g.Key.FechaEmision));
            El(w, "CodSuc", g.Key.CodigoSucursal.ToString(CultureInfo.InvariantCulture));

            switch (clase)
            {
                case Resumen.FacturaYTicket:
                    var exento = g.Sum(c => c.MontoNetoExento);
                    var susp = g.Sum(c => c.MontoNetoSuspendido);
                    var netoMin = g.Sum(c => c.MontoNetoMinimo);
                    var netoBas = g.Sum(c => c.MontoNetoBasico);
                    if (exento > 0) El(w, "TotMntNoGrv", M2(exento));
                    if (susp > 0) El(w, "TotMntIVAenSusp", M2(susp));
                    if (netoMin > 0) El(w, "TotMntIVATasaMin", M2(netoMin));
                    if (netoBas > 0) El(w, "TotMntIVATasaBas", M2(netoBas));
                    if (netoMin > 0) El(w, "MntIVATasaMin", M2(g.Sum(c => c.IvaMinimo)));
                    if (netoBas > 0) El(w, "MntIVATasaBas", M2(g.Sum(c => c.IvaBasico)));
                    if (netoMin > 0) El(w, "IVATasaMin", "10.000");
                    if (netoBas > 0) El(w, "IVATasaBas", "22.000");
                    El(w, "TotMntTotal", M2(g.Sum(c => c.MontoTotal)));
                    break;

                case Resumen.Resguardo:
                    El(w, "TotMntRetenido", M2(g.Sum(c => c.MontoRetenido)));
                    break;

                case Resumen.Exportacion:
                case Resumen.RemitoExportacion:
                    El(w, "TotMntExpyAsim", M2(g.Sum(c => c.MontoExportacion)));
                    El(w, "TotMntTotal", M2(g.Sum(c => c.MontoTotal)));
                    break;
            }

            w.WriteEndElement();
        }
        w.WriteEndElement(); // Montos
    }

    /// <summary>Rangos contiguos de números usados, por serie (<c>RDU_Item</c>).</summary>
    private static void EscribirRangos(XmlWriter w, List<Cfe> grupo)
    {
        w.WriteStartElement("RngDocsUtil", CfeXmlBuilder.NsUri);
        foreach (var serie in grupo.GroupBy(c => c.Serie).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var numeros = serie.Select(c => c.Numero).Distinct().OrderBy(n => n).ToList();
            var desde = numeros[0];
            var hasta = desde;
            for (int i = 1; i <= numeros.Count; i++)
            {
                if (i < numeros.Count && numeros[i] == hasta + 1)
                {
                    hasta = numeros[i];
                    continue;
                }

                w.WriteStartElement("RDU_Item", CfeXmlBuilder.NsUri);
                El(w, "Serie", serie.Key ?? string.Empty);
                El(w, "NroDesde", desde.ToString(CultureInfo.InvariantCulture));
                El(w, "NroHasta", hasta.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();

                if (i < numeros.Count)
                    desde = hasta = numeros[i];
            }
        }
        w.WriteEndElement();
    }

    private static void El(XmlWriter w, string name, string value)
    {
        w.WriteStartElement(name, CfeXmlBuilder.NsUri);
        w.WriteString(value);
        w.WriteEndElement();
    }

    private static string M2(decimal v) => CfeFormat.DecimalInvariant(v, "F2");
}
