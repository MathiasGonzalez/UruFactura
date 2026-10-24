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
    public void Sobre_IdEmisorFueraDeRango_LanzaUruFacturaException() =>
        Assert.Throws<UruFacturaException>(() =>
            CfeSobre.Generar("219999820013", 10_000_000_000, DateTimeOffset.UtcNow, Cert, [TestData.Emitido().Xml]));

    // --------------------------------------------------------------- reporte

    private static string Reporte(IReadOnlyCollection<CfeEmitido> cfes, int sec = 1) =>
        ReporteDiario.Generar("219999820013", TestData.Dia, sec, 77, new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero), cfes);

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
    public void Reporte_IgnoraCfesDeOtraFecha()
    {
        var otroDia = TestData.Emitido(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(-1) }, 9);

        var xml = Reporte([otroDia]);

        Assert.DoesNotContain("Rsmn_Tck", xml);
        Assert.Contains("<CantComprobantes>0</CantComprobantes>", xml);
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
            TestData.Cae(), 1, new Totales(), DateTimeOffset.UtcNow, "x", "<CFE/>");

        Assert.Throws<UruFacturaException>(() => Reporte([desconocido]));
    }
}
