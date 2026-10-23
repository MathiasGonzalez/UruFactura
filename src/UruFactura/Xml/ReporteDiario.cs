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

    /// <summary>Genera el reporte (sin firmar) con los CFE de <paramref name="cfes"/> emitidos en <paramref name="fecha"/>.</summary>
    /// <param name="rutEmisor">RUT del emisor.</param>
    /// <param name="fecha">Fecha que resume el reporte.</param>
    /// <param name="secEnvio">Secuencia del envío del día (1 el primero; para corregir, la anterior + 1).</param>
    /// <param name="idEmisor">Número de envío asignado por el emisor (opcional).</param>
    /// <param name="tmstFirmaEnv">Fecha y hora de generación del reporte.</param>
    /// <param name="cfes">CFE emitidos (si no hay de la fecha, se informa un reporte vacío).</param>
    internal static string Generar(
        string rutEmisor, DateOnly fecha, int secEnvio, long? idEmisor, DateTimeOffset tmstFirmaEnv, IEnumerable<CfeEmitido> cfes)
    {
        var acumulador = new Acumulador(fecha);
        foreach (var cfe in cfes)
            acumulador.Agregar(cfe);
        return acumulador.Generar(rutEmisor, secEnvio, idEmisor, tmstFirmaEnv);
    }

    /// <summary>
    /// Resume los CFE de una fecha a medida que se agregan: guarda sumas por tipo y sucursal y los
    /// números usados por serie, no los CFE, así que el reporte no carga en memoria los XML del día.
    /// </summary>
    internal sealed class Acumulador(DateOnly fecha)
    {
        private readonly Dictionary<TipoCfe, PorTipo> _porTipo = [];

        /// <summary>Cantidad de CFE de la fecha agregados.</summary>
        internal int Cantidad { get; private set; }

        /// <summary>Agrega un CFE (los de otra fecha se ignoran).</summary>
        /// <exception cref="UruFacturaException">Si el formato del reporte no contempla su tipo.</exception>
        internal void Agregar(CfeEmitido cfe)
        {
            if (cfe.FechaEmision != fecha)
                return;
            if (Resumenes.All(r => r.Tipo != cfe.Tipo))
                throw new UruFacturaException($"El reporte diario no soporta el tipo {cfe.Tipo}.");

            if (!_porTipo.TryGetValue(cfe.Tipo, out var tipo))
                _porTipo[cfe.Tipo] = tipo = new PorTipo();

            tipo.Cantidad++;
            var sucursal = cfe.Emisor.CodigoSucursal;
            if (!tipo.MontosPorSucursal.TryGetValue(sucursal, out var montos))
                tipo.MontosPorSucursal[sucursal] = montos = new Montos();
            montos.Sumar(cfe.Totales);
            if (!tipo.NumerosPorSerie.TryGetValue(cfe.Serie, out var numeros))
                tipo.NumerosPorSerie[cfe.Serie] = numeros = [];
            numeros.Add(cfe.Numero);
            Cantidad++;
        }

        /// <summary>Genera el reporte (sin firmar).</summary>
        internal string Generar(string rutEmisor, int secEnvio, long? idEmisor, DateTimeOffset tmstFirmaEnv)
        {
            if (secEnvio is < 1 or > 99)
                throw new UruFacturaException("SecEnvio debe estar entre 1 (el primer envío del día) y 99.");

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
                El(w, "CantComprobantes", Cantidad.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();

                foreach (var (tipo, elemento, clase) in Resumenes)
                {
                    if (!_porTipo.TryGetValue(tipo, out var resumen))
                        continue;

                    w.WriteStartElement(elemento, CfeXml.NsUri);
                    El(w, "TipoComp", ((int)tipo).ToString(CultureInfo.InvariantCulture));
                    w.WriteStartElement("RsmnData", CfeXml.NsUri);
                    EscribirMontos(w, resumen, clase);
                    if (tipo.EsContingencia())
                    {
                        // Los CFC (RsmnData_*_Cont) no informan rangos: solo la cantidad emitida.
                        El(w, "CantCFCEmi", resumen.Cantidad.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        El(w, "CantDocsUtil", resumen.Cantidad.ToString(CultureInfo.InvariantCulture));
                        EscribirRangos(w, resumen);
                    }
                    w.WriteEndElement(); // RsmnData
                    w.WriteEndElement(); // Rsmn_*
                }

                w.WriteEndElement(); // Reporte
                w.WriteEndDocument();
            });
        }

        private void EscribirMontos(XmlWriter w, PorTipo resumen, Resumen clase)
        {
            var item = clase switch
            {
                Resumen.FacturaYTicket => "Mnts_FyT_Item",
                Resumen.Exportacion => "Mnts_FExp_Item",
                Resumen.Remito => "Mnts_Rem_Item",
                Resumen.Resguardo => "Mnts_Res_Item",
                _ => "Mnts_Rem_Exp_Item",
            };

            w.WriteStartElement("Montos", CfeXml.NsUri);
            foreach (var (sucursal, m) in resumen.MontosPorSucursal)
            {
                w.WriteStartElement(item, CfeXml.NsUri);
                El(w, "Fecha", CfeFormat.DateIso(fecha));
                El(w, "CodSuc", sucursal.ToString(CultureInfo.InvariantCulture));

                switch (clase)
                {
                    case Resumen.FacturaYTicket:
                        if (m.Exento > 0) El(w, "TotMntNoGrv", M2(m.Exento));
                        if (m.Suspendido > 0) El(w, "TotMntIVAenSusp", M2(m.Suspendido));
                        if (m.NetoMinimo > 0) El(w, "TotMntIVATasaMin", M2(m.NetoMinimo));
                        if (m.NetoBasico > 0) El(w, "TotMntIVATasaBas", M2(m.NetoBasico));
                        if (m.NetoMinimo > 0) El(w, "MntIVATasaMin", M2(m.IvaMinimo));
                        if (m.NetoBasico > 0) El(w, "MntIVATasaBas", M2(m.IvaBasico));
                        if (m.NetoMinimo > 0) El(w, "IVATasaMin", Tasa(Totales.TasaIvaMinima));
                        if (m.NetoBasico > 0) El(w, "IVATasaBas", Tasa(Totales.TasaIvaBasica));
                        El(w, "TotMntTotal", M2(m.Total));
                        break;

                    case Resumen.Resguardo:
                        El(w, "TotMntRetenido", M2(m.Retenido));
                        break;

                    case Resumen.Exportacion:
                    case Resumen.RemitoExportacion:
                        El(w, "TotMntExpyAsim", M2(m.Exportacion));
                        El(w, "TotMntTotal", M2(m.Total));
                        break;
                }

                w.WriteEndElement();
            }
            w.WriteEndElement(); // Montos
        }

        /// <summary>Rangos contiguos de números usados, por serie (<c>RDU_Item</c>).</summary>
        private static void EscribirRangos(XmlWriter w, PorTipo resumen)
        {
            w.WriteStartElement("RngDocsUtil", CfeXml.NsUri);
            foreach (var (serie, numeros) in resumen.NumerosPorSerie)
            {
                // En una lista ordenada y sin repetidos, los números consecutivos comparten (número − posición).
                var rangos = numeros.Distinct().Order()
                    .Select((numero, i) => (numero, tramo: numero - i))
                    .GroupBy(x => x.tramo, x => x.numero);

                foreach (var rango in rangos)
                {
                    w.WriteStartElement("RDU_Item", CfeXml.NsUri);
                    El(w, "Serie", serie);
                    El(w, "NroDesde", rango.First().ToString(CultureInfo.InvariantCulture));
                    El(w, "NroHasta", rango.Last().ToString(CultureInfo.InvariantCulture));
                    w.WriteEndElement();
                }
            }
            w.WriteEndElement();
        }
    }

    /// <summary>Lo que el reporte necesita de los CFE de un tipo.</summary>
    private sealed class PorTipo
    {
        internal int Cantidad;
        internal readonly SortedDictionary<int, Montos> MontosPorSucursal = [];
        internal readonly SortedDictionary<string, List<long>> NumerosPorSerie = new(StringComparer.Ordinal);
    }

    /// <summary>Sumas de los totales de un grupo de CFE.</summary>
    private sealed class Montos
    {
        internal decimal Exento, Suspendido, NetoMinimo, NetoBasico, IvaMinimo, IvaBasico, Exportacion, Total, Retenido;

        internal void Sumar(Totales t)
        {
            Exento += t.MontoNetoExento;
            Suspendido += t.MontoNetoSuspendido;
            NetoMinimo += t.MontoNetoMinimo;
            NetoBasico += t.MontoNetoBasico;
            IvaMinimo += t.IvaMinimo;
            IvaBasico += t.IvaBasico;
            Exportacion += t.MontoExportacion;
            Total += t.MontoTotal;
            Retenido += t.MontoRetenido;
        }
    }
}
