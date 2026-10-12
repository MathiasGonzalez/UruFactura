using System.Xml;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Models;
using UruFactura.Signature;
using UruFactura.Xml;
using Xunit;

namespace UruFactura.Tests;

/// <summary>Tests del sobre EnvioCFE y del reporte diario, validados contra los XSD oficiales de la DGI.</summary>
public class SobreYReporteTests : IDisposable
{
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2 _cert = TestData.CertificadoAutofirmado();

    public void Dispose() => _cert.Dispose();

    private string Firmado(Cfe cfe)
    {
        // CfeFirmante es dueño del certificado y lo libera al disponerse: se comparte el de la clase.
        var firmante = new CfeFirmante(_cert);
        return firmante.Firmar(new CfeXmlBuilder().Generar(cfe));
    }

    // --------------------------------------------------------------- sobre

    [Fact]
    public void Sobre_Caratula_TieneLosCamposYElOrdenDelEsquema()
    {
        var xml = CfeSobreBuilder.Generar(
            "219999820013", 123, new DateTimeOffset(2026, 6, 15, 12, 30, 0, TimeSpan.Zero), _cert.RawData,
            [Firmado(TestData.CfeValido())]);

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
        Assert.Equal(Convert.ToBase64String(_cert.RawData), caratula.SelectSingleNode("c:X509Certificate", nsm)!.InnerText);
    }

    [Fact]
    public void Sobre_ConservaLaFirmaDeCadaCfe_EmbebidoIntacto()
    {
        var cfe1 = Firmado(TestData.CfeValido(numero: 1));
        var cfe2 = Firmado(TestData.CfeValido(numero: 2));

        var sobre = CfeSobreBuilder.Generar("219999820013", 1, DateTimeOffset.UtcNow, _cert.RawData, [cfe1, cfe2]);

        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(sobre);
        var cfes = doc.DocumentElement!.ChildNodes.Cast<XmlNode>().Where(n => n.LocalName == "CFE").ToList();
        Assert.Equal(2, cfes.Count);

        // Cada CFE extraído del sobre sigue siendo verificable por sí solo.
        foreach (var nodo in cfes)
        {
            var solo = new XmlDocument { PreserveWhitespace = true };
            solo.AppendChild(solo.ImportNode(nodo, true));
            Assert.True(CfeFirmante.VerificarFirma(solo.OuterXml));
        }
    }

    [Fact]
    public void Sobre_SinCfes_LanzaUruFacturaException() =>
        Assert.Throws<UruFacturaException>(() =>
            CfeSobreBuilder.Generar("219999820013", 1, DateTimeOffset.UtcNow, _cert.RawData, []));

    [Fact]
    public void Sobre_MasDe250Cfes_LanzaUruFacturaException()
    {
        var cfe = Firmado(TestData.CfeValido());
        Assert.Throws<UruFacturaException>(() =>
            CfeSobreBuilder.Generar("219999820013", 1, DateTimeOffset.UtcNow, _cert.RawData, Enumerable.Repeat(cfe, 251).ToList()));
    }

    [Fact]
    public void Sobre_250Cfes_ValidaContraXsd()
    {
        var cfe = Firmado(TestData.CfeValido());
        var sobre = CfeSobreBuilder.Generar("219999820013", 1, DateTimeOffset.UtcNow, _cert.RawData, Enumerable.Repeat(cfe, 250).ToList());

        Assert.Empty(TestData.ValidarContraXsd(sobre));
    }

    [Fact]
    public void Sobre_IdEmisorFueraDeRango_LanzaUruFacturaException() =>
        Assert.Throws<UruFacturaException>(() =>
            CfeSobreBuilder.Generar("219999820013", 10_000_000_000, DateTimeOffset.UtcNow, _cert.RawData, [Firmado(TestData.CfeValido())]));

    // --------------------------------------------------------------- reporte

    private string ReporteFirmado(IReadOnlyCollection<Cfe> cfes, DateOnly fecha, int sec = 0)
    {
        var firmante = new CfeFirmante(_cert);
        foreach (var c in cfes) c.CalcularTotales();
        return firmante.Firmar(ReporteDiarioBuilder.Generar(
            "219999820013", fecha, sec, 77, new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero), cfes));
    }

    private static readonly DateOnly Dia = new(2026, 6, 15);

    [Fact]
    public void Reporte_ConVariosTiposYSeries_ValidaContraXsd()
    {
        var cfes = new List<Cfe>
        {
            TestData.CfeValido(TipoCfe.ETicket, 1),
            TestData.CfeValido(TipoCfe.ETicket, 2),
            TestData.CfeValido(TipoCfe.ETicket, 5),
            TestData.CfeValido(TipoCfe.NotaCreditoETicket, 3),
            TestData.CfeValido(TipoCfe.EFactura, 4),
            TestData.CfeValido(TipoCfe.EFacturaExportacion, 6),
            TestData.CfeValido(TipoCfe.ERemito, 7),
            TestData.CfeValido(TipoCfe.ERemitoExportacion, 8),
        };

        var errores = TestData.ValidarContraXsd(ReporteFirmado(cfes, Dia));

        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Reporte_Vacio_ValidaContraXsd()
    {
        var errores = TestData.ValidarContraXsd(ReporteFirmado([], Dia));
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Reporte_AgrupaMontosYCompactaRangos()
    {
        var cfes = new List<Cfe>
        {
            TestData.CfeValido(TipoCfe.ETicket, 1),
            TestData.CfeValido(TipoCfe.ETicket, 2),
            TestData.CfeValido(TipoCfe.ETicket, 3),
            TestData.CfeValido(TipoCfe.ETicket, 7),
        };
        foreach (var c in cfes) c.CalcularTotales();

        var doc = new XmlDocument();
        doc.LoadXml(ReporteDiarioBuilder.Generar("219999820013", Dia, 0, null, DateTimeOffset.UtcNow, cfes));
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
    public void Reporte_IgnoraCfesDeOtraFecha()
    {
        var otroDia = TestData.CfeValido(TipoCfe.ETicket, 9);
        otroDia.FechaEmision = Dia.AddDays(-1);

        var xml = ReporteDiarioBuilder.Generar("219999820013", Dia, 0, null, DateTimeOffset.UtcNow, [otroDia]);

        Assert.DoesNotContain("Rsmn_Tck", xml);
        Assert.Contains("<CantComprobantes>0</CantComprobantes>", xml);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void Reporte_SecEnvioFueraDeRango_LanzaUruFacturaException(int sec) =>
        Assert.Throws<UruFacturaException>(() =>
            ReporteDiarioBuilder.Generar("219999820013", Dia, sec, null, DateTimeOffset.UtcNow, []));

    [Fact]
    public void Reporte_ConTipoNoSoportado_LanzaUruFacturaException()
    {
        var resguardo = TestData.CfeValido();
        resguardo.Tipo = TipoCfe.EResguardo;

        Assert.Throws<UruFacturaException>(() =>
            ReporteDiarioBuilder.Generar("219999820013", Dia, 0, null, DateTimeOffset.UtcNow, [resguardo]));
    }
}
