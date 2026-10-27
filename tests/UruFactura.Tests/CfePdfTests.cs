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
                DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "d", Monto = 1m, IndFact = cfe.Detalle[0].IndFact }],
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
        Assert.Equal(titulos, CfePdf.Columnas(TestData.Emitido(TestData.CfeValido(tipo))).Select(c => c.Titulo));

    [Fact]
    public void Filas_ETicket_IndicanElIndicadorDeCadaLinea()
    {
        var emitido = TestData.Emitido(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(), TestData.Linea(1, 0.125m, IndicadorFacturacion.TasaMinima), TestData.Linea(1, 5m, IndicadorFacturacion.NoFacturableNegativo)],
        });

        Assert.Equal(
            [
                ["2", "Servicio de consultoría", "100,00", "22%", "200,00"],
                ["1", "Servicio de consultoría", "0,125", "10%", "0,13"],
                ["1", "Servicio de consultoría", "5,00", "No fact.", "-5,00"], // el no facturable negativo, con el signo
            ],
            CfePdf.Filas(emitido));
    }

    [Fact]
    public void Filas_EResguardo_SonLasRetenciones() =>
        Assert.Equal(
            [["1146171", "12%", "1.000,00", "120,00"], ["2183212", "", "500,00", "25,00"]],
            CfePdf.Filas(TestData.Emitido(TestData.CfeValido(TipoCfe.EResguardo))));

    [Fact]
    public void Totales_ETicket_IncluyenAjustesNetosEIvaYElTotal()
    {
        var emitido = TestData.Emitido(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "Promo", Porcentaje = 25m }],
        });

        Assert.Equal(
            [
                new CfePdf.LineaTotal("Descuento Promo", 50m),
                new CfePdf.LineaTotal("Neto IVA 22%", 150m),
                new CfePdf.LineaTotal("IVA 22%", 33m),
                new CfePdf.LineaTotal("TOTAL", 183m, Destacada: true),
            ],
            CfePdf.LineasTotales(emitido));
    }

    [Fact]
    public void Totales_ConNoFacturable_ImprimenElTotalAPagar()
    {
        var emitido = TestData.Emitido(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(), TestData.Linea(1, 30m, IndicadorFacturacion.NoFacturable)],
        });

        var lineas = CfePdf.LineasTotales(emitido);

        Assert.Equal(new CfePdf.LineaTotal("TOTAL", 244m), lineas[^3]);
        Assert.Equal(new CfePdf.LineaTotal("No facturable", 30m), lineas[^2]);
        Assert.Equal(new CfePdf.LineaTotal("TOTAL A PAGAR", 274m, Destacada: true), lineas[^1]);
    }

    [Theory]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.EResguardo)]
    [InlineData(TipoCfe.EFacturaExportacion)]
    public void Totales_SinIvaEnRemitosResguardosYExportacion(TipoCfe tipo) =>
        Assert.DoesNotContain(CfePdf.LineasTotales(TestData.Emitido(TestData.CfeValido(tipo))), l => l.Etiqueta.Contains("IVA"));

    [Fact]
    public void Totales_IvaMinimo_NoMencionanElIva()
    {
        var cae = TestData.Cae() with { Especial = CaeEspecial.IvaMinimo };
        var emitido = TestData.Emitido(TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo)] }, cae: cae);

        Assert.DoesNotContain(CfePdf.LineasTotales(emitido), l => l.Etiqueta.Contains("IVA"));
        Assert.DoesNotContain("IVA", CfePdf.Columnas(emitido).Select(c => c.Titulo));
        Assert.Equal("Contribuyente IVA MINIMO", CfePdf.LeyendaEmisor(emitido));
    }

    [Fact]
    public void Totales_ERemito_NoTiene_YEResguardoEsElRetenido()
    {
        Assert.Empty(CfePdf.LineasTotales(TestData.Emitido(TestData.CfeValido(TipoCfe.ERemito))));
        Assert.Equal([new CfePdf.LineaTotal("TOTAL RETENIDO", 145m, Destacada: true)], CfePdf.LineasTotales(TestData.Emitido(TestData.CfeValido(TipoCfe.EResguardo))));
    }

    [Fact]
    public void Moneda_ExtranjeraConCodigoIsoYTipoDeCambio()
    {
        var emitido = TestData.Emitido(TestData.CfeValido(TipoCfe.EFacturaExportacion)); // USD a 40,5

        Assert.Equal("Moneda: USD - Tipo de cambio: 40,500", CfePdf.LineaMoneda(emitido.Documento));
        Assert.Equal("TOTAL (USD)", CfePdf.LineasTotales(emitido)[^1].Etiqueta);
        Assert.Null(CfePdf.LineaMoneda(TestData.CfeValido()));
    }

    [Fact]
    public void Referencias_DeLasNotasIndicanElComprobanteQueCorrigen() =>
        Assert.Equal(
            ["e-Ticket A-10 del 01/06/2026 por UYU 500,00 - Corrección"],
            CfePdf.LineasReferencias(TestData.CfeValido(TipoCfe.NotaCreditoETicket)));

    [Fact]
    public void Referencias_Global_ImprimeElMotivo() =>
        Assert.Equal(
            ["Referencia global: Liquidación de mayo"],
            CfePdf.LineasReferencias(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with
            {
                Referencias = [new RefCfe { Global = true, Razon = "Liquidación de mayo" }],
            }));

    // --- Datos obligatorios de la representación impresa ---

    [Theory]
    [InlineData(TipoCfe.ETicket, FormaPago.Contado, "Contado")]
    [InlineData(TipoCfe.EFactura, FormaPago.Credito, "Crédito")]
    [InlineData(TipoCfe.EFacturaExportacion, FormaPago.Contado, "Contado")]
    [InlineData(TipoCfe.ERemito, FormaPago.Contado, null)]
    [InlineData(TipoCfe.EResguardo, FormaPago.Contado, null)]
    public void FormaDePago_EnLaIdentificacionDelComprobante(TipoCfe tipo, FormaPago forma, string? esperada) =>
        Assert.Equal(esperada, CfePdf.FormaDePago(TestData.CfeValido(tipo) with { FormaPago = forma }));

    [Fact]
    public void Receptor_ETicket_RecuadroConsumoFinal_ConElDocumentoSiSeInforma()
    {
        Assert.Equal("CONSUMO FINAL", CfePdf.Receptor(TestData.CfeValido()).Recuadro);

        var identificado = CfePdf.Receptor(TestData.CfeValido() with
        {
            Receptor = new Receptor { Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad, RazonSocial = "Juan Pérez" },
        });
        Assert.Equal("CONSUMO FINAL", identificado.Recuadro);
        Assert.Equal(["Juan Pérez", "C.I. 12345672 (UY)"], identificado.Lineas);
    }

    [Theory]
    [InlineData(TipoCfe.EFactura)]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.EResguardo)]
    public void Receptor_EntreContribuyentes_RecuadroRucComprador(TipoCfe tipo)
    {
        var receptor = CfePdf.Receptor(TestData.CfeValido(tipo) with
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A.", Direccion = "Calle 1", Ciudad = "Montevideo" },
        });

        Assert.Equal("RUC COMPRADOR", receptor.Recuadro);
        Assert.Equal("214844360018", receptor.ValorRecuadro);
        Assert.Equal(["Cliente S.A.", "Calle 1, Montevideo"], receptor.Lineas);
    }

    [Fact]
    public void Receptor_Exportacion_NombreYDomicilioSinRecuadro()
    {
        var receptor = CfePdf.Receptor(TestData.CfeValido(TipoCfe.EFacturaExportacion));

        Assert.Null(receptor.Recuadro);
        Assert.Equal(["Importer Inc.", "Pasaporte X1234567 (US)", "5th Avenue 100, New York, NY, Estados Unidos"], receptor.Lineas);
    }

    [Theory]
    [InlineData(CaeEspecial.Exonerado, "Contribuyente Exonerado - Ley 16.906")]
    [InlineData(CaeEspecial.IvaMinimo, "Contribuyente IVA MINIMO")]
    [InlineData(CaeEspecial.Monotributo, "MONOTRIBUTO")]
    [InlineData(CaeEspecial.MonotributoSocialMides, "MONOTRIBUTO SOCIAL MIDES")]
    public void LeyendaEmisor_SegunElCaeEspecial(CaeEspecial especial, string leyenda)
    {
        var cae = TestData.Cae() with { Especial = especial, CausalEspecial = especial == CaeEspecial.Exonerado ? 1 : null };
        var cfe = TestData.CfeValido() with
        {
            Emisor = TestData.Emisor() with { NormaExoneracion = "Ley 16.906" },
            Detalle = [TestData.Linea(iva: especial == CaeEspecial.Exonerado ? IndicadorFacturacion.Exento : IndicadorFacturacion.IvaMinimoMonotributo)],
        };

        Assert.Equal(leyenda, CfePdf.LeyendaEmisor(TestData.Emitido(cfe, cae: cae)));
    }

    [Fact]
    public void LeyendaEmisor_CaeComun_NoTiene() => Assert.Null(CfePdf.LeyendaEmisor(TestData.Emitido()));

    [Fact]
    public void Sello_ConstanciaDeIvaAlDia_SalvoMonotributo()
    {
        Assert.Contains("IVA al día", CfePdf.LineasSello(TestData.Emitido()));
        Assert.Contains("Emisor sin constancia de IVA al día",
            CfePdf.LineasSello(TestData.Emitido(TestData.CfeValido() with { Emisor = TestData.Emisor() with { IvaAlDia = false } })));
        var monotributo = TestData.Emitido(
            TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo)] },
            cae: TestData.Cae() with { Especial = CaeEspecial.Monotributo });
        Assert.DoesNotContain(CfePdf.LineasSello(monotributo), l => l.Contains("IVA"));
        Assert.DoesNotContain(CfePdf.LineasSello(TestData.Emitido(TestData.CfeValido(TipoCfe.ERemito))), l => l.Contains("IVA"));
    }

    [Fact]
    public void A4_ConAdenda_EsMayorQueSinAdenda()
    {
        var sin = CfePdf.GenerarA4(TestData.Emitido());
        var con = CfePdf.GenerarA4(TestData.Emitido(TestData.CfeValido() with { Adenda = "Pedido: 1234\nEntrega: lunes" }));

        Assert.True(con.Length > sin.Length);
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
    [InlineData(TipoCfe.NotaCreditoEFacturaExportacion, "Nota de Crédito de e-Factura Exportación")]
    [InlineData(TipoCfe.NotaDebitoETicketCuentaAjenaContingencia, "Nota de Débito de e-Ticket Venta por Cuenta Ajena Contingencia")]
    [InlineData(TipoCfe.ERemitoExportacion, "e-Remito de Exportación")]
    [InlineData(TipoCfe.NotaCreditoEBoleta, "Nota de Crédito de e-Boleta de entrada")]
    [InlineData(TipoCfe.EResguardo, "e-Resguardo")]
    public void NombreCompleto_DelTipo(TipoCfe tipo, string esperado) => Assert.Equal(esperado, CfePdf.NombreCompleto(tipo));
}
