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
        var aceptado = TestData.Emitido(estado: EstadoCfe.Aceptado);

        Assert.True(CfePdf.GenerarA4(aceptado).Length > sinAceptar.Length);
    }

    // --- Contenido según el tipo de comprobante ---

    [Theory]
    [InlineData(TipoCfe.ETicket, new[] { "Cant.", "Descripción", "P.Unit.", "IVA", "Total" })]
    [InlineData(TipoCfe.EFacturaExportacion, new[] { "Cant.", "Descripción", "P.Unit.", "Total" })]
    [InlineData(TipoCfe.ERemitoExportacion, new[] { "Cant.", "Descripción", "P.Unit.", "Total" })]
    [InlineData(TipoCfe.ERemito, new[] { "Cant.", "Descripción" })]
    [InlineData(TipoCfe.EResguardo, new[] { "Código", "Tasa", "Monto sujeto", "Valor" })]
    public void Columnas_SegunElTipo_SinIvaEnExportacionNiPreciosEnRemitos(TipoCfe tipo, string[] titulos) =>
        Assert.Equal(titulos, CfePdf.Columnas(tipo).Select(c => c.Titulo));

    [Fact]
    public void Filas_ETicket_IndicanElIvaDeCadaLinea()
    {
        var cfe = TestData.CfeValido() with { Detalle = [TestData.Linea(), TestData.Linea(1, 0.125m, TipoIva.Minimo)] };

        Assert.Equal(
            [["2", "Servicio de consultoría", "100,00", "22%", "200,00"], ["1", "Servicio de consultoría", "0,125", "10%", "0,13"]],
            CfePdf.Filas(cfe));
    }

    [Fact]
    public void Filas_EResguardo_SonLasRetenciones() =>
        Assert.Equal(
            [["R1", "12%", "1.000,00", "120,00"], ["R2", "", "500,00", "25,00"]],
            CfePdf.Filas(TestData.CfeValido(TipoCfe.EResguardo)));

    [Fact]
    public void Totales_ETicket_IncluyenAjustesNetosEIva()
    {
        var emitido = TestData.Emitido(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "Promo", Monto = 50m }],
        });

        Assert.Equal([("Descuento Promo", 50m), ("Neto IVA 22%", 150m), ("IVA 22%", 33m)], CfePdf.LineasTotales(emitido));
        Assert.Equal(("TOTAL", 183m), CfePdf.Total(emitido));
    }

    [Theory]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.EResguardo)]
    [InlineData(TipoCfe.EFacturaExportacion)]
    public void Totales_SinIvaEnRemitosResguardosYExportacion(TipoCfe tipo) =>
        Assert.DoesNotContain(CfePdf.LineasTotales(TestData.Emitido(TestData.CfeValido(tipo))), l => l.Etiqueta.Contains("IVA"));

    [Fact]
    public void Total_ERemito_NoTiene_YEResguardoEsElRetenido()
    {
        Assert.Null(CfePdf.Total(TestData.Emitido(TestData.CfeValido(TipoCfe.ERemito))));
        Assert.Equal(("TOTAL RETENIDO", 145m), CfePdf.Total(TestData.Emitido(TestData.CfeValido(TipoCfe.EResguardo))));
    }

    [Fact]
    public void Moneda_ExtranjeraConCodigoIsoYTipoDeCambio()
    {
        var emitido = TestData.Emitido(TestData.CfeValido(TipoCfe.EFacturaExportacion)); // USD a 40,5

        Assert.Equal("Moneda: USD - Tipo de cambio: 40,500", CfePdf.LineaMoneda(emitido.Documento));
        Assert.Equal("TOTAL (USD)", CfePdf.Total(emitido)!.Value.Etiqueta);
        Assert.Null(CfePdf.LineaMoneda(TestData.CfeValido()));
    }

    [Fact]
    public void Referencias_DeLasNotasIndicanElComprobanteQueCorrigen() =>
        Assert.Equal(
            ["e-Ticket A-10 del 01/06/2026 por UYU 500,00 - Corrección"],
            CfePdf.LineasReferencias(TestData.CfeValido(TipoCfe.NotaCreditoETicket)));

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
