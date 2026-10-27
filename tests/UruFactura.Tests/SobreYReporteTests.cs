using System.Xml;
using UruFactura.Xml;

namespace UruFactura.Tests;

/// <summary>Tests del sobre EnvioCFE y del reporte diario, validados contra los XSD oficiales de la DGI.</summary>
public class SobreYReporteTests
{
    private static readonly byte[] Cert = TestData.Certificado.RawData;

    // --------------------------------------------------------------- sobre

    [Fact]
    public void Sobre_Caratula_TieneLosCamposYElOrdenDelEsquema()
    {
        var xml = CfeSobre.Generar(
            "219999820013", 123, new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero), Cert, [TestData.Emitido().Xml]);

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", "http://cfe.dgi.gub.uy");

        Assert.Equal("EnvioCFE", doc.DocumentElement!.LocalName);
        Assert.Equal("1.0", doc.DocumentElement.GetAttribute("version"));
        var caratula = doc.SelectSingleNode("/c:EnvioCFE/c:Caratula", nsm)!;
        Assert.Equal("1.0", caratula.Attributes!["version"]!.Value);
        Assert.Equal(
            ["RutReceptor", "RUCEmisor", "Idemisor", "CantCFE", "Fecha", "X509Certificate"],
            caratula.ChildNodes.Cast<XmlNode>().Select(n => n.LocalName));
        Assert.Equal("214844360018", caratula.SelectSingleNode("c:RutReceptor", nsm)!.InnerText);
        Assert.Equal("123", caratula.SelectSingleNode("c:Idemisor", nsm)!.InnerText);
        Assert.Equal("2026-06-15T09:30:00-03:00", caratula.SelectSingleNode("c:Fecha", nsm)!.InnerText);
        Assert.Equal(Convert.ToBase64String(Cert), caratula.SelectSingleNode("c:X509Certificate", nsm)!.InnerText);
    }

    [Fact]
    public void Sobre_ConservaLaFirmaDeCadaCfe_EmbebidoIntacto()
    {
        var sobre = TestData.Sobre(TestData.Emitido(numero: 1), TestData.Emitido(numero: 2));

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(sobre);
        var cfes = doc.DocumentElement!.ChildNodes.Cast<XmlNode>().Where(n => n.LocalName == "CFE").ToList();
        Assert.Equal(2, cfes.Count);

        // Cada CFE extraído del sobre sigue siendo verificable por sí solo.
        foreach (var nodo in cfes)
        {
            var solo = new XmlDocument { PreserveWhitespace = true };
            solo.AppendChild(solo.ImportNode(nodo, true));
            Assert.NotNull(FirmaDigital.VerificarFirma(solo.OuterXml));
        }
    }

    [Fact]
    public void Sobre_SinCfes_LanzaUruFacturaException() =>
        Assert.Throws<UruFacturaException>(() => CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, Cert, []));

    [Fact]
    public void Sobre_MasDe250Cfes_LanzaUruFacturaException()
    {
        var xml = TestData.Emitido().Xml;
        Assert.Throws<UruFacturaException>(() =>
            CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, Cert, Enumerable.Repeat(xml, 251).ToList()));
    }

    [Fact]
    public void Sobre_250Cfes_ValidaContraXsd()
    {
        var xml = TestData.Emitido().Xml;
        TestData.AssertValidoContraXsd(CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, Cert, Enumerable.Repeat(xml, 250).ToList()));
    }

    [Fact]
    public void Sobre_DeMasDe2MB_LanzaUruFacturaException()
    {
        // Formato del sobre v05: tamaño máximo 2 MB (la DGI lo rechazaría con S07).
        var linea = TestData.Linea() with { Descripcion = new string('x', 1000) };
        var grande = TestData.Emitido(TestData.CfeValido() with { Detalle = [.. Enumerable.Repeat(linea, 700)] }).Xml;

        var ex = Assert.Throws<UruFacturaException>(() => CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, Cert, [grande, grande, grande]));
        Assert.Contains("2 MB", ex.Message);
    }

    [Fact]
    public void Sobre_LaCaratulaLlevaElCertificadoConQueSeFirmaronLosCfe()
    {
        // Por ejemplo, CFE firmados antes de renovar el certificado: la carátula lleva el de sus firmas (S06).
        using var otro = TestData.CertificadoAutofirmado();
        var xml = CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, otro.RawData, [TestData.Emitido().Xml]);

        Assert.Contains($"<X509Certificate>{Convert.ToBase64String(Cert)}</X509Certificate>", xml);
        Assert.DoesNotContain(Convert.ToBase64String(otro.RawData), xml);
    }

    [Fact]
    public void Sobre_CfeFirmadosConCertificadosDistintos_LanzaUruFacturaException()
    {
        using var otro = TestData.CertificadoAutofirmado();
        var cfe = TestData.CfeValido() with { Emisor = TestData.Emisor(), FechaEmision = TestData.Dia };
        var cae = TestData.Cae() with { UltimoNroUsado = 2 };
        var (firmadoConOtro, _) = FirmaDigital.Firmar(CfeXml.Generar(cfe, cae, 2, cfe.CalcularTotales(), DateTimeOffset.UtcNow), otro, AlgoritmoFirma.Sha256);

        Assert.Throws<UruFacturaException>(() => CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, Cert, [TestData.Emitido().Xml, firmadoConOtro]));
    }

    [Fact]
    public void Sobre_IdEmisorFueraDeRango_LanzaUruFacturaException() =>
        Assert.Throws<UruFacturaException>(() =>
            CfeSobre.Generar("219999820013", 10_000_000_000, DateTimeOffset.UtcNow, Cert, [TestData.Emitido().Xml]));

    // --------------------------------------------------------------- reporte

    private static string Reporte(IReadOnlyCollection<CfeEmitido> cfes, int sec = 1, decimal? tope = null) =>
        ReporteDiario.Generar("219999820013", TestData.Dia, sec, 77, new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero), cfes, tope);

    private static XmlDocument Documento(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static string? Valor(XmlDocument doc, string xpath)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", "http://cfe.dgi.gub.uy");
        return doc.SelectSingleNode(xpath, nsm)?.InnerText;
    }

    /// <summary>Un CFE con el estado ante la DGI y los motivos indicados.</summary>
    private static CfeEmitido ConEstado(CfeEmitido cfe, EstadoCfe estado, string? motivos = null) =>
        new(cfe.Documento, cfe.Cae, cfe.Numero, cfe.Totales, cfe.FechaFirma, cfe.CodigoSeguridad, cfe.Xml) { EstadoDgi = estado, MensajeDgi = motivos };

    private static string ReporteFirmado(IReadOnlyCollection<CfeEmitido> cfes) =>
        FirmaDigital.Firmar(Reporte(cfes), TestData.Certificado, AlgoritmoFirma.Sha256).Xml;

    private static CfeEmitido Emitido(TipoCfe tipo, long numero, string serie = "A") =>
        TestData.Emitido(TestData.CfeValido(tipo), numero, TestData.Cae(tipo, serie));

    [Fact]
    public void Reporte_ConVariosTiposYSeries_ValidaContraXsd() =>
        TestData.AssertValidoContraXsd(ReporteFirmado(
        [
            Emitido(TipoCfe.ETicket, 1),
            Emitido(TipoCfe.ETicket, 2),
            Emitido(TipoCfe.ETicket, 5, "B"),
            Emitido(TipoCfe.NotaCreditoETicket, 3),
            Emitido(TipoCfe.EFactura, 4),
            Emitido(TipoCfe.EFacturaExportacion, 6),
            Emitido(TipoCfe.ERemito, 7),
            Emitido(TipoCfe.ERemitoExportacion, 8),
        ]));

    public static IEnumerable<object[]> TodosLosTipos() => Enum.GetValues<TipoCfe>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void Reporte_CadaTipoDeCfe_ValidaContraXsd(TipoCfe tipo) =>
        TestData.AssertValidoContraXsd(ReporteFirmado([Emitido(tipo, 1)]));

    [Fact]
    public void Reporte_Vacio_ValidaContraXsd() => TestData.AssertValidoContraXsd(ReporteFirmado([]));

    [Fact]
    public void Reporte_Contingencia_InformaCantCfcEmiSinRangos()
    {
        var reporte = Reporte([Emitido(TipoCfe.ETicketContingencia, 1), Emitido(TipoCfe.ETicketContingencia, 2)]);

        Assert.Contains("<CantCFCEmi>2</CantCFCEmi>", reporte);
        Assert.DoesNotContain("CantDocsUtil", reporte);
        Assert.DoesNotContain("RngDocsUtil", reporte);
    }

    [Fact]
    public void Reporte_AgrupaMontosYCompactaRangos()
    {
        var doc = new XmlDocument();
        doc.LoadXml(Reporte([Emitido(TipoCfe.ETicket, 1), Emitido(TipoCfe.ETicket, 2), Emitido(TipoCfe.ETicket, 3), Emitido(TipoCfe.ETicket, 7)]));
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", "http://cfe.dgi.gub.uy");

        Assert.Equal("4", doc.SelectSingleNode("//c:Caratula/c:CantComprobantes", nsm)!.InnerText);
        Assert.Equal("101", doc.SelectSingleNode("//c:Rsmn_Tck/c:TipoComp", nsm)!.InnerText);
        Assert.Equal("4", doc.SelectSingleNode("//c:Rsmn_Tck/c:RsmnData/c:CantDocsUtil", nsm)!.InnerText);
        Assert.Equal("976.00", doc.SelectSingleNode("//c:Mnts_FyT_Item/c:TotMntTotal", nsm)!.InnerText); // 4 x 244
        Assert.Equal("800.00", doc.SelectSingleNode("//c:Mnts_FyT_Item/c:TotMntIVATasaBas", nsm)!.InnerText);

        var rangos = doc.SelectNodes("//c:RDU_Item", nsm)!.Cast<XmlNode>()
            .Select(n => (n["NroDesde", "http://cfe.dgi.gub.uy"]!.InnerText, n["NroHasta", "http://cfe.dgi.gub.uy"]!.InnerText))
            .ToList();
        Assert.Equal([("1", "3"), ("7", "7")], rangos);
    }

    [Fact]
    public void Reporte_AgrupaMontosPorSucursal()
    {
        var central = Emitido(TipoCfe.ETicket, 1);
        var otra = TestData.Emitido(TestData.CfeValido() with { Emisor = TestData.Emisor() with { CodigoSucursal = 2 } }, 2);

        var reporte = Reporte([central, otra]);

        Assert.Contains("<CodSuc>1</CodSuc>", reporte);
        Assert.Contains("<CodSuc>2</CodSuc>", reporte);
    }

    [Fact]
    public void Reporte_IgnoraCfesFirmadosEnOtraFecha()
    {
        var ayer = new DateTimeOffset(2026, 6, 14, 23, 59, 0, HoraUruguay.Offset);
        var otroDia = TestData.Emitido(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(-1) }, 9, firma: ayer);

        var xml = Reporte([otroDia]);

        Assert.DoesNotContain("Rsmn_Tck", xml);
        Assert.Contains("<CantComprobantes>0</CantComprobantes>", xml);
    }

    [Fact]
    public void Reporte_LaFechaEsLaDeLaFirmaEnUruguay_NoLaDelServidor()
    {
        // 01:30 UTC del 16 es el 15 a las 22:30 en Uruguay: va en el reporte del 15.
        var cfe = TestData.Emitido(firma: new DateTimeOffset(2026, 6, 16, 1, 30, 0, TimeSpan.Zero));

        Assert.Contains("<CantComprobantes>1</CantComprobantes>", Reporte([cfe]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Reporte_SecEnvioFueraDeRango_LanzaUruFacturaException(int sec) =>
        Assert.Throws<UruFacturaException>(() => Reporte([], sec));

    [Fact]
    public void Reporte_ConTipoNoSoportado_LanzaUruFacturaException()
    {
        // Un almacenamiento propio podría devolver un tipo que el formato del reporte no contempla.
        var desconocido = new CfeEmitido(
            new Cfe((TipoCfe)999) { FechaEmision = TestData.Dia, Emisor = TestData.Emisor() },
            TestData.Cae(), 1, new Totales(), new DateTimeOffset(2026, 6, 15, 12, 0, 0, HoraUruguay.Offset), "x", "<CFE/>");

        Assert.Throws<UruFacturaException>(() => Reporte([desconocido]));
    }

    // --- Formato del reporte v13.2 ---

    [Fact]
    public void Reporte_MontosEnMonedaExtranjera_SeInformanEnPesos()
    {
        // USD 244 (neto 200 + IVA 44) a 40,5: el reporte se expresa en pesos (formato de reporte v13.2).
        var dolares = TestData.Emitido(TestData.CfeValido() with { Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m });

        var doc = Documento(Reporte([dolares]));

        Assert.Equal("8100.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntIVATasaBas"));
        Assert.Equal("1782.00", Valor(doc, "//c:Mnts_FyT_Item/c:MntIVATasaBas"));
        Assert.Equal("9882.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntTotal"));
    }

    [Fact]
    public void Reporte_TotalEsLaSumaDeLosMontosInformados_AunqueRedondeeCadaConversion()
    {
        var a = TestData.Emitido(TestData.CfeValido() with { Moneda = Moneda.DolarAmericano, TipoCambio = 39.333m }, 1);
        var b = TestData.Emitido(TestData.CfeValido() with { Moneda = Moneda.DolarAmericano, TipoCambio = 40.777m }, 2);

        var doc = Documento(Reporte([a, b]));

        Assert.Equal(
            decimal.Parse(Valor(doc, "//c:TotMntIVATasaBas")!, System.Globalization.CultureInfo.InvariantCulture)
            + decimal.Parse(Valor(doc, "//c:MntIVATasaBas")!, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(Valor(doc, "//c:TotMntTotal")!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Reporte_MontosPorFechaDeComprobante_YRangosGlobales()
    {
        var hoy = TestData.Emitido(numero: 1);
        var conFechaDeAyer = TestData.Emitido(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(-1) }, 2);

        var doc = Documento(Reporte([hoy, conFechaDeAyer]));

        Assert.Equal("2", Valor(doc, "//c:Caratula/c:CantComprobantes"));
        Assert.Equal("244.00", Valor(doc, "//c:Mnts_FyT_Item[c:Fecha='2026-06-14']/c:TotMntTotal"));
        Assert.Equal("244.00", Valor(doc, "//c:Mnts_FyT_Item[c:Fecha='2026-06-15']/c:TotMntTotal"));
        Assert.Equal("2", Valor(doc, "//c:RDU_Item/c:NroHasta"));
    }

    [Fact]
    public void Reporte_CfeRechazadoPorLaDgi_SeInformaComoAnuladoSinMontos()
    {
        var aceptado = TestData.Emitido(numero: 1, estado: EstadoCfe.Aceptado);
        var rechazado = ConEstado(TestData.Emitido(numero: 2), EstadoCfe.Rechazado, "E05 No cumple validaciones");

        var xml = Reporte([aceptado, rechazado]);
        var doc = Documento(xml);

        TestData.AssertValidoContraXsd(FirmaDigital.Firmar(xml, TestData.Certificado, AlgoritmoFirma.Sha256).Xml);
        Assert.Equal("2", Valor(doc, "//c:RsmnData/c:CantDocsUtil"));
        Assert.Equal("1", Valor(doc, "//c:RsmnData/c:CantDocsAnulados"));
        Assert.Equal("1", Valor(doc, "//c:RsmnData/c:CantDocsEmi"));
        Assert.Equal("2", Valor(doc, "//c:RngDocsAnulados/c:RDA_Item/c:NroDesde"));
        Assert.Equal("244.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntTotal")); // solo el aceptado
        Assert.Equal("2", Valor(doc, "//c:Caratula/c:CantComprobantes"));
    }

    [Fact]
    public void Reporte_RechazoPorDuplicado_NoAnulaElNumero()
    {
        // E02: el CFE ya existe en los registros de la DGI (un reenvío de uno recibido): sigue emitido.
        var duplicado = ConEstado(TestData.Emitido(), EstadoCfe.Rechazado, "E02 Tipo y Nº de CFE ya existe en los registros");

        var doc = Documento(Reporte([duplicado]));

        Assert.Equal("0", Valor(doc, "//c:RsmnData/c:CantDocsAnulados"));
        Assert.Equal("1", Valor(doc, "//c:RsmnData/c:CantDocsEmi"));
    }

    [Fact]
    public void Reporte_CantidadDeETicketsSobreElTope()
    {
        var sobreElTope = TestData.Emitido(TestData.CfeValido() with
        {
            Receptor = new Receptor { Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
        }, 1);
        var bajoElTope = TestData.Emitido(TestData.CfeValido() with { Detalle = [TestData.Linea(1, 10m)] }, 2);

        var xml = Reporte([sobreElTope, bajoElTope], tope: 100m);

        Assert.Contains("<CantDocsMay_topeUI>1</CantDocsMay_topeUI>", xml);
        TestData.AssertValidoContraXsd(FirmaDigital.Firmar(xml, TestData.Certificado, AlgoritmoFirma.Sha256).Xml);
        // Sin el valor de la UI no se puede calcular: no se informa.
        Assert.DoesNotContain("CantDocsMay_topeUI", Reporte([sobreElTope]));
        // No corresponde a los e-Tickets de venta por cuenta ajena.
        Assert.DoesNotContain("CantDocsMay_topeUI", Reporte([Emitido(TipoCfe.ETicketCuentaAjena, 1)], tope: 100m));
    }

    [Fact]
    public void Reporte_CfcObservado_NoSeCuentaComoEmitido()
    {
        var observado = ConEstado(Emitido(TipoCfe.ETicketContingencia, 1), EstadoCfe.Observado, "C05 No cumple validaciones");

        var xml = Reporte([observado, Emitido(TipoCfe.ETicketContingencia, 2)]);

        Assert.Contains("<CantCFCEmi>1</CantCFCEmi>", xml);
        Assert.Contains("<CantComprobantes>1</CantComprobantes>", xml);
    }

    [Fact]
    public void Reporte_ConTodosLosIndicadoresYCreditosFiscales_ValidaContraXsd()
    {
        var factura = TestData.Emitido(TestData.CfeValido(TipoCfe.EFactura) with
        {
            Detalle =
            [
                TestData.Linea(), TestData.Linea(1, 10m, IndicadorFacturacion.ExportacionYAsimiladas),
                TestData.Linea(1, 5m, IndicadorFacturacion.ImpuestoPercibido), TestData.Linea(1, 100m, IndicadorFacturacion.OtraTasa),
            ],
            IvaOtraTasa = 4m,
        }, cae: TestData.Cae(TipoCfe.EFactura));
        var resguardo = TestData.Emitido(TestData.CfeValido(TipoCfe.EResguardo) with
        {
            Detalle = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "2181103", MontoSujeto = 100m, Valor = 3m }] }],
        }, cae: TestData.Cae(TipoCfe.EResguardo));

        var xml = Reporte([factura, resguardo]);
        var doc = Documento(xml);

        TestData.AssertValidoContraXsd(FirmaDigital.Firmar(xml, TestData.Certificado, AlgoritmoFirma.Sha256).Xml);
        Assert.Equal("10.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntExpyAsim"));
        Assert.Equal("5.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntImpPerc"));
        Assert.Equal("100.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntIVAOtra"));
        Assert.Equal("4.00", Valor(doc, "//c:Mnts_FyT_Item/c:MntIVAOtra"));
        Assert.Equal("363.00", Valor(doc, "//c:Mnts_FyT_Item/c:TotMntTotal")); // 200 + 44 + 10 + 5 + 100 + 4
        Assert.Equal("0.00", Valor(doc, "//c:Mnts_Res_Item/c:TotMntRetenido"));
        Assert.Equal("3.00", Valor(doc, "//c:Mnts_Res_Item/c:TotMntCredFisc"));
    }
}
