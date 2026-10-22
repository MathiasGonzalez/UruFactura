namespace UruFactura.Tests;

/// <summary>Totales y validaciones del contenido del CFE.</summary>
public class CfeModelTests
{
    private static Cfe Cfe(params LineaDetalle[] lineas) => TestData.CfeValido() with { Detalle = lineas };

    // --- Totales ---

    [Fact]
    public void MontoItem_CantidadConMasDe3Decimales_SeCalculaConLaCantidadDelXml()
    {
        // En el XML la cantidad se escribe con 3 decimales (0.333): MontoItem debe ser 0.333 × 100.
        var linea = new LineaDetalle { NombreItem = "x", Cantidad = 0.3333m, PrecioUnitario = 100m };
        Assert.Equal(33.30m, linea.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_MitadDeCentesimo_RedondeaHaciaArriba()
    {
        // IVA mínimo de 1.25 = 0.125: el redondeo al par daría 0.12; el comercial (y el F2 del XML), 0.13.
        var t = Cfe(TestData.Linea(1, 1.25m, TipoIva.Minimo)).CalcularTotales();

        Assert.Equal(0.13m, t.IvaMinimo);
        Assert.Equal(1.38m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_DescuentoGlobalConMasDe2Decimales_UsaElValorEscritoEnElXml()
    {
        var cfe = TestData.CfeValido() with { DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "d", Monto = 0.125m }] };

        Assert.Equal(199.87m, cfe.CalcularTotales().MontoNetoBasico); // 200 − 0.13 (ValorDR escrito)
    }

    [Fact]
    public void CalcularTotales_IvaBasico()
    {
        var t = TestData.CfeValido().CalcularTotales(); // 2 × 100

        Assert.Equal(200m, t.MontoNetoBasico);
        Assert.Equal(44m, t.IvaBasico);
        Assert.Equal(244m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_IvaMinimo()
    {
        var t = Cfe(TestData.Linea(1, 1000m, TipoIva.Minimo)).CalcularTotales();

        Assert.Equal(1000m, t.MontoNetoMinimo);
        Assert.Equal(100m, t.IvaMinimo);
        Assert.Equal(1100m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_Exento_NoAplicaIva()
    {
        var t = Cfe(TestData.Linea(iva: TipoIva.Exento)).CalcularTotales();

        Assert.Equal(200m, t.MontoNetoExento);
        Assert.Equal(0m, t.IvaBasico);
        Assert.Equal(200m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_ExentoYSuspendido_SonCamposSeparados()
    {
        var t = Cfe(TestData.Linea(1, 300m, TipoIva.Exento), TestData.Linea(1, 200m, TipoIva.Suspendido)).CalcularTotales();

        Assert.Equal(300m, t.MontoNetoExento);
        Assert.Equal(200m, t.MontoNetoSuspendido);
        Assert.Equal(500m, t.MontoTotal);
        Assert.Equal(500m, t.MontoNeto);
    }

    [Fact]
    public void CalcularTotales_MixtoIvas()
    {
        var t = Cfe(TestData.Linea(), TestData.Linea(1, 50m, TipoIva.Exento)).CalcularTotales();

        Assert.Equal(200m, t.MontoNetoBasico);
        Assert.Equal(50m, t.MontoNetoExento);
        Assert.Equal(294m, t.MontoTotal);
    }

    [Fact]
    public void LineaDetalle_MontoTotal_ConDescuento()
    {
        var linea = new LineaDetalle { Cantidad = 3, PrecioUnitario = 150m, DescuentoMonto = 10m };
        Assert.Equal(440m, linea.MontoTotal);
    }

    [Fact]
    public void LineaDetalle_DescuentoMonto_TienePrioridadSobreElPorcentaje()
    {
        var linea = new LineaDetalle { Cantidad = 1, PrecioUnitario = 100m, DescuentoMonto = 5m, DescuentoPorcentaje = 50m };
        Assert.Equal(95m, linea.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_EResguardo_SumaLasRetenciones()
    {
        var t = TestData.CfeValido(TipoCfe.EResguardo).CalcularTotales();

        Assert.Equal(145m, t.MontoRetenido);
        Assert.Equal(0m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_Exportacion_TodoEsMontoDeExportacion()
    {
        var t = TestData.CfeValido(TipoCfe.EFacturaExportacion).CalcularTotales();

        Assert.Equal(200m, t.MontoExportacion);
        Assert.Equal(200m, t.MontoTotal);
        Assert.Equal(0m, t.IvaBasico);
    }

    // --- Validación ---

    public static IEnumerable<object[]> TodosLosTipos() => Enum.GetValues<TipoCfe>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void Validar_CfeValidoDeCadaTipo_NoTieneErrores(TipoCfe tipo) =>
        Assert.Empty(TestData.Validar(TestData.CfeValido(tipo)));

    [Fact]
    public void Validar_SinDetalle() =>
        Assert.Contains(TestData.Validar(Cfe()), e => e.Contains("línea de detalle"));

    [Fact]
    public void Validar_LineaSinNombre() =>
        Assert.Contains(TestData.Validar(Cfe(TestData.Linea() with { NombreItem = "" })), e => e.Contains("NombreItem"));

    [Fact]
    public void Validar_LineaCantidadCero() =>
        Assert.Contains(TestData.Validar(Cfe(TestData.Linea(cantidad: 0))), e => e.Contains("Cantidad"));

    [Fact]
    public void Validar_TipoNoSoportado() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido() with { Tipo = (TipoCfe)999 }), e => e.Contains("no está soportado"));

    [Fact]
    public void Validar_MonedaFueraDelEnum() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido() with { Moneda = (Moneda)1 }), e => e.Contains("Moneda"));

    [Fact]
    public void Validar_MonedaExtranjeraSinTipoDeCambio() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido() with { Moneda = Moneda.DolarAmericano }), e => e.Contains("TipoCambio"));

    [Fact]
    public void Validar_FechaDeVencimientoAnteriorALaEmision() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido() with { FechaVencimiento = TestData.Dia.AddDays(-1) }),
            e => e.Contains("FechaVencimiento"));

    [Fact]
    public void Validar_NotaSinReferencias() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with { Referencias = [] }),
            e => e.Contains("referenci"));

    [Fact]
    public void Validar_NotaConReferenciaSinMontoNiMoneda()
    {
        var cfe = TestData.CfeValido(TipoCfe.NotaCreditoETicket) with
        {
            Referencias = [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, FechaCfe = TestData.Dia }],
        };

        var errores = TestData.Validar(cfe);

        Assert.Contains(errores, e => e.Contains("MontoCfeRef"));
        Assert.Contains(errores, e => e.Contains("MonedaCfeRef"));
    }

    [Theory]
    [InlineData(TipoCfe.EFactura)]
    [InlineData(TipoCfe.EFacturaExportacion)]
    [InlineData(TipoCfe.EResguardo)]
    public void Validar_TiposQueRequierenReceptor_SinReceptor(TipoCfe tipo) =>
        Assert.Contains(TestData.Validar(TestData.CfeValido(tipo) with { Receptor = null }), e => e.Contains("receptor"));

    [Fact]
    public void Validar_RutReceptorDeEFacturaInvalido() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido(TipoCfe.EFactura) with { Receptor = new Receptor { Documento = "210000000012" } }),
            e => e.Contains("RUT del receptor"));

    [Fact]
    public void Validar_EFacturaConReceptorNoRut() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido(TipoCfe.EFactura) with
            {
                Receptor = new Receptor { Documento = "12345678", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
            }), e => e.Contains("RUT uruguayo"));

    [Theory]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.ERemitoExportacion)]
    public void Validar_RemitoSinIndTraslado(TipoCfe tipo) =>
        Assert.Contains(TestData.Validar(TestData.CfeValido(tipo) with { IndTraslado = null }), e => e.Contains("IndTraslado"));

    [Fact]
    public void Validar_RemitoConIndTrasladoFueraDelEnum() =>
        Assert.Contains(TestData.Validar(TestData.CfeValido(TipoCfe.ERemito) with { IndTraslado = (IndTraslado)99 }),
            e => e.Contains("IndTraslado"));

    [Fact]
    public void Validar_RemitoConReceptorSinDireccionNiCiudad()
    {
        var errores = TestData.Validar(TestData.CfeValido(TipoCfe.ERemito) with
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A." },
        });

        Assert.Contains(errores, e => e.Contains("e-Remito requiere dirección"));
        Assert.Contains(errores, e => e.Contains("e-Remito requiere ciudad"));
    }

    [Fact]
    public void Validar_ExportacionSinClausulaNiViaDeTransporte()
    {
        var errores = TestData.Validar(TestData.CfeValido(TipoCfe.EFacturaExportacion) with { ClausulaVenta = null, ViaTransporte = null });

        Assert.Contains(errores, e => e.Contains("ClausulaVenta"));
        Assert.Contains(errores, e => e.Contains("ViaTransporte"));
    }

    [Fact]
    public void Validar_ETicketSobreElTope_RequiereDocumentoDelReceptor()
    {
        var cfe = TestData.CfeValido(); // neto 200

        Assert.Contains(TestData.Validar(cfe, tope: 150m), e => e.Contains("tope"));
        Assert.Empty(TestData.Validar(cfe, tope: 1000m));
        Assert.Empty(TestData.Validar(cfe with
        {
            Receptor = new Receptor { Documento = "12345678", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
        }, tope: 150m));
    }

    [Fact]
    public void Validar_ETicketSobreElTopeEnDolares_UsaElTipoDeCambio()
    {
        var cfe = TestData.CfeValido() with { Moneda = Moneda.DolarAmericano, TipoCambio = 40m }; // 200 USD = 8000 pesos

        Assert.Contains(TestData.Validar(cfe, tope: 5000m), e => e.Contains("tope"));
    }
}
