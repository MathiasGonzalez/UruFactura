using System.Globalization;
using System.Xml;
using UruFactura.Formatting;
using static UruFactura.Xml.XmlCfe;

namespace UruFactura.Xml;

/// <summary>
/// Arma el XML del Reporte Diario (<c>Reporte</c>, esquema <c>ReporteDiarioCFE.xsd</c>, formato de reporte
/// v13.2): por tipo de comprobante, los montos en pesos por fecha de comprobante y sucursal, y las cantidades
/// y rangos de números utilizados, anulados y emitidos. Resume los CFE firmados en la fecha del reporte (la
/// fecha de Uruguay de su firma, no la de emisión). No contiene los CFE; el XML resultante se firma antes de
/// enviarse.
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
        (TipoCfe.EBoleta,                                   "Rsmn_Bol",                           Resumen.Boleta),
        (TipoCfe.NotaCreditoEBoleta,                        "Rsmn_Bol_Nota_Credito",              Resumen.Boleta),
        (TipoCfe.NotaDebitoEBoleta,                         "Rsmn_Bol_Nota_Debito",               Resumen.Boleta),
        (TipoCfe.EBoletaContingencia,                       "Rsmn_Bol_Cont",                      Resumen.Boleta),
        (TipoCfe.NotaCreditoEBoletaContingencia,            "Rsmn_Bol_Cont_Nota_Credito",         Resumen.Boleta),
        (TipoCfe.NotaDebitoEBoletaContingencia,             "Rsmn_Bol_Cont_Nota_Debito",          Resumen.Boleta),
    ];

    private enum Resumen { FacturaYTicket, Exportacion, Remito, RemitoExportacion, Resguardo, Boleta }

    /// <summary>Genera el reporte (sin firmar) con los CFE de <paramref name="cfes"/> firmados en <paramref name="fecha"/>.</summary>
    /// <param name="rutEmisor">RUT del emisor.</param>
    /// <param name="fecha">Fecha que resume el reporte (la de la firma de los CFE, en Uruguay).</param>
    /// <param name="secEnvio">Secuencia del envío del día (1 el primero; para corregir, la anterior + 1).</param>
    /// <param name="idEmisor">Número de envío asignado por el emisor (opcional).</param>
    /// <param name="tmstFirmaEnv">Fecha y hora de generación del reporte.</param>
    /// <param name="cfes">CFE emitidos (si no hay de la fecha, se informa un reporte vacío).</param>
    /// <param name="topeMontoNeto">Tope en pesos (5.000 UI) que obliga a enviar los e-Tickets uno a uno; si se omite no se informa <c>CantDocsMay_topeUI</c>.</param>
    internal static string Generar(
        string rutEmisor, DateOnly fecha, int secEnvio, long? idEmisor, DateTimeOffset tmstFirmaEnv, IEnumerable<CfeEmitido> cfes,
        decimal? topeMontoNeto = null)
    {
        var acumulador = new Acumulador(fecha, topeMontoNeto is { } tope ? _ => tope : null);
        foreach (var cfe in cfes)
            acumulador.Agregar(cfe);
        return acumulador.Generar(rutEmisor, secEnvio, idEmisor, tmstFirmaEnv);
    }

    /// <summary>
    /// Resume los CFE de una fecha a medida que se agregan: guarda sumas por tipo, fecha de comprobante y
    /// sucursal y los números usados por serie, no los CFE, así que el reporte no carga en memoria los XML
    /// del día.
    /// </summary>
    /// <param name="fecha">Fecha del reporte: la de Uruguay de la firma de los CFE (<see cref="CfeEmitido.FechaReporte"/>).</param>
    /// <param name="topeMontoNeto">
    /// Tope en pesos (5.000 UI) de los e-Tickets que se envían uno a uno, según la fecha del comprobante (la UI al
    /// cierre del año anterior); si se omite no se informa <c>CantDocsMay_topeUI</c>.
    /// </param>
    internal sealed class Acumulador(DateOnly fecha, Func<DateOnly, decimal?>? topeMontoNeto)
    {
        private readonly Dictionary<TipoCfe, PorTipo> _porTipo = [];

        /// <summary>
        /// Cantidad de comprobantes del reporte (<c>CantComprobantes</c>): los CFE utilizados (emitidos más
        /// anulados por rechazo de la DGI) y los CFC emitidos y no observados.
        /// </summary>
        internal int Cantidad => _porTipo.Sum(p => p.Key.EsContingencia() ? p.Value.Emitidos : p.Value.CantidadUtilizados);

        /// <summary>Agrega un CFE (los de otra fecha se ignoran).</summary>
        /// <exception cref="UruFacturaException">Si el formato del reporte no contempla su tipo.</exception>
        internal void Agregar(CfeEmitido cfe)
        {
            if (cfe.FechaReporte != fecha)
                return;
            if (Resumenes.All(r => r.Tipo != cfe.Tipo))
                throw new UruFacturaException($"El reporte diario no soporta el tipo {cfe.Tipo}.");

            var situacion = cfe.SituacionEnReporte;
            if (situacion == SituacionReporte.Excluido)
                return;

            if (!_porTipo.TryGetValue(cfe.Tipo, out var tipo))
                _porTipo[cfe.Tipo] = tipo = new PorTipo();

            // Los CFC no informan rangos: su número es el del comprobante en papel.
            if (!cfe.Tipo.EsContingencia())
            {
                Agregar(tipo.Utilizados, cfe);
                if (situacion == SituacionReporte.Anulado)
                {
                    Agregar(tipo.Anulados, cfe);
                    return;
                }
            }
            else if (situacion != SituacionReporte.Emitido)
                return;

            // Emitido y no rechazado (o CFC no observado): suma montos, en pesos, aparte los pagos por cuenta de terceros.
            tipo.Emitidos++;
            var montosClave = (cfe.FechaEmision, cfe.Emisor.CodigoSucursal, cfe.Documento.PagoCuentaTerceros);
            if (!tipo.Montos.TryGetValue(montosClave, out var montos))
                tipo.Montos[montosClave] = montos = new Montos();
            var tipoCambio = cfe.Documento.Moneda == Moneda.PesoUruguayo ? 1m : cfe.Documento.TipoCambio ?? 1m;
            montos.Sumar(cfe.Totales, tipoCambio);

            if (cfe.Tipo.Familia() == FamiliaCfe.Ticket && !cfe.Tipo.EsCuentaAjena() && topeMontoNeto?.Invoke(cfe.FechaEmision) is { } tope
                && CfeFormat.Redondear(cfe.Totales.MontoNeto * tipoCambio) > tope)
                tipo.SobreTope++;
        }

        private static void Agregar(SortedDictionary<string, List<long>> porSerie, CfeEmitido cfe)
        {
            if (!porSerie.TryGetValue(cfe.Serie, out var numeros))
                porSerie[cfe.Serie] = numeros = [];
            numeros.Add(cfe.Numero);
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
                    if (resumen.Montos.Count > 0)
                        EscribirMontos(w, resumen, clase);

                    // CantDocsMay_topeUI: solo los e-Tickets propios (no los de venta por cuenta ajena) y sus CFC.
                    var conTope = topeMontoNeto is not null && tipo.Familia() == FamiliaCfe.Ticket && !tipo.EsCuentaAjena();
                    if (tipo.EsContingencia())
                    {
                        if (conTope)
                            El(w, "CantDocsMay_topeUI", resumen.SobreTope.ToString(CultureInfo.InvariantCulture));
                        El(w, "CantCFCEmi", resumen.Emitidos.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        El(w, "CantDocsUtil", resumen.CantidadUtilizados.ToString(CultureInfo.InvariantCulture));
                        if (conTope)
                            El(w, "CantDocsMay_topeUI", resumen.SobreTope.ToString(CultureInfo.InvariantCulture));
                        El(w, "CantDocsAnulados", resumen.CantidadAnulados.ToString(CultureInfo.InvariantCulture));
                        El(w, "CantDocsEmi", resumen.Emitidos.ToString(CultureInfo.InvariantCulture));
                        EscribirRangos(w, "RngDocsUtil", "RDU_Item", resumen.Utilizados);
                        if (resumen.Anulados.Count > 0)
                            EscribirRangos(w, "RngDocsAnulados", "RDA_Item", resumen.Anulados);
                    }
                    w.WriteEndElement(); // RsmnData
                    w.WriteEndElement(); // Rsmn_*
                }

                w.WriteEndElement(); // Reporte
                w.WriteEndDocument();
            });
        }

        private static void EscribirMontos(XmlWriter w, PorTipo resumen, Resumen clase)
        {
            var item = clase switch
            {
                Resumen.FacturaYTicket => "Mnts_FyT_Item",
                Resumen.Exportacion => "Mnts_FExp_Item",
                Resumen.Remito => "Mnts_Rem_Item",
                Resumen.Resguardo => "Mnts_Res_Item",
                Resumen.Boleta => "Mnts_Bol_Item",
                _ => "Mnts_Rem_Exp_Item",
            };

            w.WriteStartElement("Montos", CfeXml.NsUri);
            foreach (var ((fechaComprobante, sucursal, pagoCuentaTerceros), m) in resumen.Montos)
            {
                w.WriteStartElement(item, CfeXml.NsUri);
                El(w, "Fecha", CfeFormat.DateIso(fechaComprobante));
                El(w, "CodSuc", sucursal.ToString(CultureInfo.InvariantCulture));
                // B-C11.1: los montos de pagos por cuenta de terceros (reintegros de gastos) van en un grupo propio.
                if (pagoCuentaTerceros)
                    El(w, "IndPagCta3ros", "1");

                switch (clase)
                {
                    case Resumen.FacturaYTicket:
                        if (m.NoGravado > 0) El(w, "TotMntNoGrv", M2(m.NoGravado));
                        if (m.Exportacion > 0) El(w, "TotMntExpyAsim", M2(m.Exportacion));
                        if (m.ImpuestoPercibido > 0) El(w, "TotMntImpPerc", M2(m.ImpuestoPercibido));
                        if (m.Suspendido > 0) El(w, "TotMntIVAenSusp", M2(m.Suspendido));
                        if (m.NetoMinimo > 0) El(w, "TotMntIVATasaMin", M2(m.NetoMinimo));
                        if (m.NetoBasico > 0) El(w, "TotMntIVATasaBas", M2(m.NetoBasico));
                        if (m.NetoOtraTasa > 0) El(w, "TotMntIVAOtra", M2(m.NetoOtraTasa));
                        if (m.NetoMinimo > 0) El(w, "MntIVATasaMin", M2(m.IvaMinimo));
                        if (m.NetoBasico > 0) El(w, "MntIVATasaBas", M2(m.IvaBasico));
                        if (m.IvaOtraTasa > 0) El(w, "MntIVAOtra", M2(m.IvaOtraTasa));
                        if (m.NetoMinimo > 0) El(w, "IVATasaMin", Tasa(Totales.TasaIvaMinima));
                        if (m.NetoBasico > 0) El(w, "IVATasaBas", Tasa(Totales.TasaIvaBasica));
                        El(w, "TotMntTotal", M2(m.Total));
                        if (m.Retenido != 0) El(w, "TotMntRetenido", M2(m.Retenido));
                        if (m.CreditoFiscal != 0) El(w, "TotMntCredFisc", M2(m.CreditoFiscal));
                        break;

                    case Resumen.Resguardo:
                        El(w, "TotMntRetenido", M2(m.Retenido));
                        if (m.CreditoFiscal != 0) El(w, "TotMntCredFisc", M2(m.CreditoFiscal));
                        break;

                    case Resumen.Boleta:
                        if (m.NoGravado > 0) El(w, "TotMntNoGrv", M2(m.NoGravado));
                        if (m.Suspendido > 0) El(w, "TotMntIVAenSusp", M2(m.Suspendido));
                        El(w, "TotMntTotal", M2(m.Total));
                        if (m.Retenido != 0) El(w, "TotMntRetenido", M2(m.Retenido));
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

        /// <summary>Rangos contiguos de números, por serie (<c>RDU_Item</c> o <c>RDA_Item</c>).</summary>
        private static void EscribirRangos(XmlWriter w, string elemento, string item, SortedDictionary<string, List<long>> porSerie)
        {
            w.WriteStartElement(elemento, CfeXml.NsUri);
            foreach (var (serie, numeros) in porSerie)
            {
                // En una lista ordenada y sin repetidos, los números consecutivos comparten (número − posición).
                var rangos = numeros.Distinct().Order()
                    .Select((numero, i) => (numero, tramo: numero - i))
                    .GroupBy(x => x.tramo, x => x.numero);

                foreach (var rango in rangos)
                {
                    w.WriteStartElement(item, CfeXml.NsUri);
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
        /// <summary>Emitidos y no rechazados (CFE) o emitidos y no observados (CFC).</summary>
        internal int Emitidos;

        /// <summary>E-Tickets emitidos con monto neto (en pesos) mayor al tope, que se envían uno a uno.</summary>
        internal int SobreTope;

        /// <summary>Montos por fecha de comprobante, sucursal e indicador de pagos por cuenta de terceros.</summary>
        internal readonly SortedDictionary<(DateOnly Fecha, int Sucursal, bool PagoCuentaTerceros), Montos> Montos = [];

        /// <summary>Números utilizados (emitidos y anulados) por serie.</summary>
        internal readonly SortedDictionary<string, List<long>> Utilizados = new(StringComparer.Ordinal);

        /// <summary>Números anulados por rechazo de la DGI, por serie.</summary>
        internal readonly SortedDictionary<string, List<long>> Anulados = new(StringComparer.Ordinal);

        internal int CantidadUtilizados => Utilizados.Values.Sum(n => n.Distinct().Count());

        internal int CantidadAnulados => Anulados.Values.Sum(n => n.Distinct().Count());
    }

    /// <summary>
    /// Sumas de los totales de un grupo de CFE, en pesos: cada monto se convierte con el tipo de cambio de su
    /// CFE y se redondea a 2 decimales.
    /// </summary>
    private sealed class Montos
    {
        internal decimal NoGravado, Exportacion, ImpuestoPercibido, Suspendido, NetoMinimo, NetoBasico, NetoOtraTasa;
        internal decimal IvaMinimo, IvaBasico, IvaOtraTasa, Retenido, CreditoFiscal;

        /// <summary>Total (<c>TotMntTotal</c>): la suma de los montos informados (C12 a C21 del formato).</summary>
        internal decimal Total =>
            NoGravado + Exportacion + ImpuestoPercibido + Suspendido + NetoMinimo + NetoBasico + NetoOtraTasa
            + IvaMinimo + IvaBasico + IvaOtraTasa;

        internal void Sumar(Totales t, decimal tipoCambio)
        {
            decimal Pesos(decimal monto) => tipoCambio == 1m ? monto : CfeFormat.Redondear(monto * tipoCambio);

            NoGravado += Pesos(t.MontoNoGravado);
            Exportacion += Pesos(t.MontoExportacion);
            ImpuestoPercibido += Pesos(t.MontoImpuestoPercibido);
            Suspendido += Pesos(t.MontoNetoSuspendido);
            NetoMinimo += Pesos(t.MontoNetoMinimo);
            NetoBasico += Pesos(t.MontoNetoBasico);
            NetoOtraTasa += Pesos(t.MontoNetoOtraTasa);
            IvaMinimo += Pesos(t.IvaMinimo);
            IvaBasico += Pesos(t.IvaBasico);
            IvaOtraTasa += Pesos(t.IvaOtraTasa);
            Retenido += Pesos(t.MontoRetenido);
            CreditoFiscal += Pesos(t.MontoCreditoFiscal);
        }
    }
}
