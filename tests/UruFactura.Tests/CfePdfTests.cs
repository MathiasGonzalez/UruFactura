using System.Text;

namespace UruFactura.Tests;

/// <summary>Representación impresa (FluentReport + SkiaSharp + ZXing), sin stubs.</summary>
public class CfePdfTests
{
    private static bool EsPdf(byte[] bytes) => Encoding.ASCII.GetString(bytes, 0, 4) == "%PDF";

    public static IEnumerable<object[]> TodosLosTipos() => Enum.GetValues<TipoCfe>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void Pdf_SeGeneraParaCadaTipo(TipoCfe tipo)
    {
        var cfe = TestData.CfeValido(tipo) with
        {
            Emisor = TestData.Emisor() with { FechaIncorporacion = "01/01/2020", UrlVerificacion = "www.empresa.com.uy" },
        };
        if (!tipo.EsRemito() && tipo.Familia() != FamiliaCfe.Resguardo)
            cfe = cfe with
            {
                MediosPago = [new MedioPago { Glosa = "Efectivo", Valor = 1m }],
                DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "d", Monto = 1m }],
            };
        var emitido = TestData.Emitido(cfe);

        Assert.True(EsPdf(CfePdf.GenerarA4(emitido)));
        Assert.True(EsPdf(CfePdf.GenerarTermico(emitido)));
    }

    [Fact]
    public void A4_ConReceptor_EsMayorQueSinReceptor()
    {
        var sin = CfePdf.GenerarA4(TestData.Emitido());
        var con = CfePdf.GenerarA4(TestData.Emitido(TestData.CfeValido() with
        {
            Receptor = new Receptor { RazonSocial = "Cliente S.A.", Documento = "214844360018", Direccion = "Calle Falsa 123" },
        }));

        Assert.True(con.Length > sin.Length, $"Con receptor ({con.Length} bytes) debería ser mayor que sin ({sin.Length} bytes).");
    }

    [Fact]
    public void Termico_ConReceptor_EsMayorQueSinReceptor()
    {
        var sin = CfePdf.GenerarTermico(TestData.Emitido());
        var con = CfePdf.GenerarTermico(TestData.Emitido(TestData.CfeValido() with
        {
            Receptor = new Receptor { RazonSocial = "Cliente S.A.", Documento = "214844360018" },
        }));

        Assert.True(con.Length > sin.Length);
    }

    [Fact]
    public void A4_AceptadoPorDgi_AgregaLaMarca()
    {
        var sinAceptar = CfePdf.GenerarA4(TestData.Emitido());
        var aceptado = TestData.Emitido();
        aceptado.EstadoDgi = "AE";

        Assert.True(CfePdf.GenerarA4(aceptado).Length > sinAceptar.Length);
    }

    [Fact]
    public void Qr_EsPng()
    {
        var bytes = CfePdf.GenerarQr(TestData.Emitido());

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], bytes.Take(4));
    }

    [Fact]
    public void Nulo_LanzaArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CfePdf.GenerarA4(null!));
        Assert.Throws<ArgumentNullException>(() => CfePdf.GenerarTermico(null!));
        Assert.Throws<ArgumentNullException>(() => CfePdf.GenerarQr(null!));
    }

    [Theory]
    [InlineData(TipoCfe.ETicket, "e-Ticket")]
    [InlineData(TipoCfe.NotaCreditoEFacturaExportacion, "Nota de Crédito e-Factura de Exportación")]
    [InlineData(TipoCfe.NotaDebitoETicketCuentaAjenaContingencia, "Nota de Débito e-Ticket Cuenta Ajena (Contingencia)")]
    [InlineData(TipoCfe.EResguardo, "e-Resguardo")]
    public void NombreCompleto_DelTipo(TipoCfe tipo, string esperado) => Assert.Equal(esperado, CfePdf.NombreCompleto(tipo));
}
