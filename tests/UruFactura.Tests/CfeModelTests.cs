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
        var t = Cfe(TestData.Linea(1, 1.25m, IndicadorFacturacion.TasaMinima)).CalcularTotales();

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
        var t = Cfe(TestData.Linea(1, 1000m, IndicadorFacturacion.TasaMinima)).CalcularTotales();

        Assert.Equal(1000m, t.MontoNetoMinimo);
        Assert.Equal(100m, t.IvaMinimo);
        Assert.Equal(1100m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_Exento_NoAplicaIva()
    {
        var t = Cfe(TestData.Linea(iva: IndicadorFacturacion.Exento)).CalcularTotales();

        Assert.Equal(200m, t.MontoNoGravado);
        Assert.Equal(0m, t.IvaBasico);
        Assert.Equal(200m, t.MontoTotal);
    }

    [Fact]
    public void CalcularTotales_ExentoYSuspendido_SonCamposSeparados()
    {
        var t = Cfe(TestData.Linea(1, 300m, IndicadorFacturacion.Exento), TestData.Linea(1, 200m, IndicadorFacturacion.IvaEnSuspenso)).CalcularTotales();

        Assert.Equal(300m, t.MontoNoGravado);
        Assert.Equal(200m, t.MontoNetoSuspendido);
        Assert.Equal(500m, t.MontoTotal);
        Assert.Equal(500m, t.MontoNeto);
    }

    [Fact]
    public void CalcularTotales_MixtoIvas()
    {
        var t = Cfe(TestData.Linea(), TestData.Linea(1, 50m, IndicadorFacturacion.Exento)).CalcularTotales();

        Assert.Equal(200m, t.MontoNetoBasico);
        Assert.Equal(50m, t.MontoNoGravado);
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
    public void Validar_SinDetalle() => Assert.Contains("Detalle", TestData.Campos(Cfe()));

    [Fact]
    public void Validar_LineaSinNombre() =>
        Assert.Contains("Detalle[0].NombreItem", TestData.Campos(Cfe(TestData.Linea() with { NombreItem = "" })));

    [Fact]
    public void Validar_LineaCantidadCero() =>
        Assert.Contains("Detalle[0].Cantidad", TestData.Campos(Cfe(TestData.Linea(cantidad: 0))));

    [Fact]
    public void Validar_CantidadQueRedondeaACero() =>
        Assert.Contains("Detalle[0].Cantidad", TestData.Campos(Cfe(TestData.Linea(cantidad: 0.0004m))));

    [Fact]
    public void Validar_TipoNoSoportado() =>
        Assert.Equal(["Tipo"], TestData.Campos(TestData.CfeValido() with { Tipo = (TipoCfe)999 }));

    [Fact]
    public void Validar_MonedaFueraDeLaTabla() =>
        Assert.Contains("Moneda", TestData.Campos(TestData.CfeValido() with { Moneda = new Moneda("XYZ") }));

    [Fact]
    public void Validar_MonedaExtranjeraSinTipoDeCambio() =>
        Assert.Contains("TipoCambio", TestData.Campos(TestData.CfeValido() with { Moneda = Moneda.DolarAmericano }));

    [Fact]
    public void Validar_TipoDeCambioQueRedondeaACero() =>
        Assert.Contains("TipoCambio", TestData.Campos(TestData.CfeValido() with { Moneda = Moneda.DolarAmericano, TipoCambio = 0.0004m }));

    [Fact]
    public void Validar_FechaDeVencimientoAnteriorALaEmision() =>
        Assert.Contains("FechaVencimiento", TestData.Campos(TestData.CfeValido() with { FechaVencimiento = TestData.Dia.AddDays(-1) }));

    [Fact]
    public void Validar_FechaDeEmisionMasDe60DiasPosterior() =>
        Assert.Contains("FechaEmision", TestData.Campos(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(61) }));

    [Fact]
    public void Validar_FechaDeEmisionHasta60DiasPosterior_EsValida() =>
        Assert.Empty(TestData.Validar(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(60) }));

    [Fact]
    public void Validar_FechaDeEmisionAnteriorAlFormatoCfe() =>
        Assert.Contains("FechaEmision", TestData.Campos(TestData.CfeValido() with { FechaEmision = new DateOnly(2010, 1, 1) }));

    [Fact]
    public void Validar_NotaSinReferencias() =>
        Assert.Contains("Referencias", TestData.Campos(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with { Referencias = [] }));

    [Fact]
    public void Validar_NotaConReferenciaSinMontoNiMoneda()
    {
        var cfe = TestData.CfeValido(TipoCfe.NotaCreditoETicket) with
        {
            Referencias = [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, FechaCfe = TestData.Dia }],
        };

        var campos = TestData.Campos(cfe);

        Assert.Contains("Referencias[0].MontoCfeRef", campos);
        Assert.Contains("Referencias[0].MonedaCfeRef", campos);
    }

    public static IEnumerable<object[]> ReferenciasInvalidas() =>
    [
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "", NroCfe = 1, FechaCfe = TestData.Dia }, "Referencias[0].Serie"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "a", NroCfe = 1, FechaCfe = TestData.Dia }, "Referencias[0].Serie"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 0, FechaCfe = TestData.Dia }, "Referencias[0].NroCfe"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 10_000_000, FechaCfe = TestData.Dia }, "Referencias[0].NroCfe"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, FechaCfe = new DateOnly(2010, 1, 1) }, "Referencias[0].FechaCfe"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A" }, "Referencias[0].NroCfe"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, FechaCfe = TestData.Dia.AddDays(61) }, "Referencias[0].FechaCfe"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, FechaCfe = TestData.Dia, Razon = new string('x', 91) }, "Referencias[0].Razon"],
        [new RefCfe { Serie = "A", NroCfe = 1, FechaCfe = TestData.Dia }, "Referencias[0].TipoCfe"],
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, FechaCfe = TestData.Dia, MontoCfeRef = -1m }, "Referencias[0].MontoCfeRef"],
        // F-C10: el tipo de cambio es obligatorio si el comprobante referenciado no es en pesos.
        [new RefCfe { TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 1, MontoCfeRef = 10m, MonedaCfeRef = Moneda.DolarAmericano }, "Referencias[0].TipoCambioCfeRef"],
        // Referencia global: exige el motivo y no admite los datos del comprobante.
        [new RefCfe { Global = true }, "Referencias[0].Razon"],
        [new RefCfe { Global = true, Razon = "Liquidación", TipoCfe = TipoCfe.ETicket }, "Referencias[0]"],
        [new RefCfe { Global = true, Razon = "Liquidación", MontoCfeRef = 1m }, "Referencias[0]"],
    ];

    [Fact]
    public void Validar_ReferenciasRepetidas_AlMismoComprobante()
    {
        var referencia = TestData.CfeValido(TipoCfe.NotaCreditoETicket).Referencias[0];

        var campos = TestData.Campos(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with { Referencias = [referencia, referencia with { Razon = "Otra" }] });

        Assert.Equal(["Referencias[1]"], campos);
    }

    [Fact]
    public void Validar_NotaConReferenciaGlobal_NoExigeMontoNiMoneda() =>
        Assert.Empty(TestData.Validar(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with
        {
            Referencias = [new RefCfe { Global = true, Razon = "Descuento por volumen de mayo" }],
        }));

    [Fact]
    public void Validar_NotaSinFechaDelComprobanteReferenciado_EsValida() =>
        Assert.Empty(TestData.Validar(TestData.CfeValido(TipoCfe.NotaCreditoEFactura) with
        {
            Referencias = [TestData.CfeValido(TipoCfe.NotaCreditoEFactura).Referencias[0] with { FechaCfe = null }],
        }));

    [Fact]
    public void Validar_ERemitoConMontoDelComprobanteReferenciado_TieneError() =>
        Assert.Equal(["Referencias[0]"], TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with
        {
            Referencias = [new RefCfe { TipoCfe = TipoCfe.ERemito, Serie = "A", NroCfe = 3, MontoCfeRef = 10m, MonedaCfeRef = Moneda.PesoUruguayo }],
        }));

    [Theory]
    [MemberData(nameof(ReferenciasInvalidas))]
    public void Validar_ReferenciaQueNoCumpleElEsquema(RefCfe referencia, string campo) =>
        Assert.Contains(campo, TestData.Campos(TestData.CfeValido() with { Referencias = [referencia] }));

    [Theory]
    [InlineData(TipoCfe.EFactura)]
    [InlineData(TipoCfe.EFacturaExportacion)]
    [InlineData(TipoCfe.EResguardo)]
    public void Validar_TiposQueRequierenReceptor_SinReceptor(TipoCfe tipo) =>
        Assert.Contains("Receptor", TestData.Campos(TestData.CfeValido(tipo) with { Receptor = null }));

    [Fact]
    public void Validar_EFacturaSinDocumentoDelReceptor() =>
        Assert.Contains("Receptor.Documento", TestData.Campos(TestData.CfeValido(TipoCfe.EFactura) with
        {
            Receptor = new Receptor { RazonSocial = "Cliente S.A." },
        }));

    [Fact]
    public void Validar_RutReceptorDeEFacturaInvalido() =>
        Assert.Contains("Receptor.Documento", TestData.Campos(TestData.CfeValido(TipoCfe.EFactura) with
        {
            Receptor = new Receptor { Documento = "210000000012" },
        }));

    [Fact]
    public void Validar_EFacturaConReceptorNoRut() =>
        Assert.Contains("Receptor.TipoDocumento", TestData.Campos(TestData.CfeValido(TipoCfe.EFactura) with
        {
            Receptor = new Receptor { Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
        }));

    [Fact]
    public void Validar_CodigoDePaisQueNoAdmiteLaDgi() =>
        Assert.Contains("Receptor.CodigoPais", TestData.Campos(TestData.CfeValido() with
        {
            Receptor = new Receptor { Documento = "X1", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "XX" },
        }));

    [Theory]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.ERemitoExportacion)]
    public void Validar_RemitoSinIndTraslado(TipoCfe tipo) =>
        Assert.Contains("IndTraslado", TestData.Campos(TestData.CfeValido(tipo) with { IndTraslado = null }));

    [Fact]
    public void Validar_RemitoConIndTrasladoFueraDelEnum() =>
        Assert.Contains("IndTraslado", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with { IndTraslado = (IndTraslado)99 }));

    [Fact]
    public void Validar_RemitoConReceptorSinDireccionNiCiudad()
    {
        var campos = TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A." },
        });

        Assert.Contains("Receptor.Direccion", campos);
        Assert.Contains("Receptor.Ciudad", campos);
    }

    [Theory]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.ERemitoExportacion)]
    public void Validar_RemitoConDescuentoEnLaLinea(TipoCfe tipo) =>
        Assert.Contains("Detalle[0]", TestData.Campos(TestData.CfeValido(tipo) with
        {
            Detalle = [TestData.Linea() with { DescuentoMonto = 10m }],
        }));

    [Fact]
    public void Validar_ExportacionSinClausulaNiViaDeTransporte()
    {
        var campos = TestData.Campos(TestData.CfeValido(TipoCfe.EFacturaExportacion) with { ClausulaVenta = null, ViaTransporte = null });

        Assert.Contains("ClausulaVenta", campos);
        Assert.Contains("ViaTransporte", campos);
    }

    [Fact]
    public void Validar_RemitoDeExportacionConClausulaInvalida() =>
        Assert.Contains("ClausulaVenta", TestData.Campos(TestData.CfeValido(TipoCfe.ERemitoExportacion) with { ClausulaVenta = "FOB1" }));

    [Fact]
    public void Validar_ETicketSobreElTope_RequiereDocumentoDelReceptor()
    {
        var cfe = TestData.CfeValido(); // neto 200

        Assert.Contains("Receptor.Documento", TestData.Campos(cfe, tope: 150m));
        Assert.Empty(TestData.Validar(cfe, tope: 1000m));
        Assert.Empty(TestData.Validar(cfe with
        {
            Receptor = new Receptor { Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
        }, tope: 150m));
    }

    [Fact]
    public void Validar_ETicketSobreElTopeEnDolares_UsaElTipoDeCambio()
    {
        var cfe = TestData.CfeValido() with { Moneda = Moneda.DolarAmericano, TipoCambio = 40m }; // 200 USD = 8000 pesos

        Assert.Contains(TestData.Validar(cfe, tope: 5000m), e => e.Mensaje.Contains("tope"));
    }

    // --- Montos y textos fuera del esquema (pasaban la validación y la DGI los rechazaba) ---

    [Theory]
    [InlineData(-1)]
    [InlineData(100_000_000_000)]
    public void Validar_PrecioUnitarioFueraDeRango(double precio) =>
        Assert.Contains("Detalle[0].PrecioUnitario", TestData.Campos(Cfe(TestData.Linea(1, (decimal)precio))));

    [Fact]
    public void Validar_MontoDeLaLineaSobreElMaximo() =>
        Assert.Contains("Detalle[0]", TestData.Campos(Cfe(TestData.Linea(10_000_000m, 99_999_999_999m))));

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(0, -50)]
    public void Validar_DescuentoORecargoNegativo(double descuento, double recargo)
    {
        // Antes daba un MontoItem de 150 para 1 × 100 sin informar el ajuste.
        var linea = TestData.Linea(1, 100m) with { DescuentoMonto = (decimal)descuento, RecargoMonto = (decimal)recargo };

        Assert.Contains(TestData.Validar(Cfe(linea)), e => e.Campo is "Detalle[0].DescuentoMonto" or "Detalle[0].RecargoMonto");
    }

    [Fact]
    public void Validar_DescuentoDeLaLineaMayorQueSuMonto() =>
        Assert.Contains("Detalle[0].DescuentoMonto", TestData.Campos(Cfe(TestData.Linea(1, 100m) with { DescuentoMonto = 150m })));

    [Fact]
    public void Validar_DescuentoGlobalMayorQueLaBase()
    {
        // Antes dejaba MntTotal negativo, que el esquema no admite.
        var cfe = TestData.CfeValido() with { DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "Promo", Monto = 500m }] };

        Assert.Contains("DescuentosRecargos", TestData.Campos(cfe));
    }

    [Theory]
    [InlineData(@"Café\u0002")]
    [InlineData(@"Línea\u001Fx")]
    [InlineData(@"\uFFFE")]
    public void Validar_CaracteresDeControl(string nombre) =>
        Assert.Contains("Detalle[0].NombreItem", TestData.Campos(Cfe(TestData.Linea() with { NombreItem = System.Text.RegularExpressions.Regex.Unescape(nombre) })));

    [Fact]
    public void Validar_TextoConEmoji_EsValido() =>
        Assert.Empty(TestData.Validar(Cfe(TestData.Linea() with { NombreItem = "Torta 🎂" })));

    [Fact]
    public void Validar_CaracterDeControlEnElReceptor() =>
        Assert.Contains("Receptor.RazonSocial", TestData.Campos(TestData.CfeValido(TipoCfe.EFactura) with
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente\u0007 S.A." },
        }));

    [Fact]
    public void Validar_InformaTodosLosErroresConSuCampo()
    {
        var cfe = Cfe(TestData.Linea() with { NombreItem = "" }, TestData.Linea(cantidad: 0)) with { NroInterno = new string('x', 51) };

        Assert.Equal(["NroInterno", "Detalle[0].NombreItem", "Detalle[1].Cantidad"], TestData.Campos(cfe));
    }

    // --- Indicadores de facturación, descuentos en porcentaje y CAE especial ---

    [Fact]
    public void Totales_IndicadoresNoFacturables_VanAlMontoNoFacturableYAlTotalAPagar()
    {
        var t = (TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(), TestData.Linea(1, 30m, IndicadorFacturacion.NoFacturable), TestData.Linea(1, 10m, IndicadorFacturacion.NoFacturableNegativo)],
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "Bonificación", Monto = 5m, IndFact = IndicadorFacturacion.NoFacturable }],
        }).CalcularTotales();

        Assert.Equal(244m, t.MontoTotal);
        Assert.Equal(15m, t.MontoNoFacturable); // 30 − 5 − 10
        Assert.Equal(259m, t.MontoPagar);
    }

    [Fact]
    public void Totales_IvaMinimoMonotributo_EsNoGravado()
    {
        var t = (TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo)] }).CalcularTotales();

        Assert.Equal((200m, 0m, 200m), (t.MontoNoGravado, t.IvaBasico, t.MontoTotal));
    }

    [Fact]
    public void Validar_EntregaGratuitaConPrecio_TieneError() =>
        Assert.Contains("Detalle[0].PrecioUnitario", TestData.Campos(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(1, 10m, IndicadorFacturacion.EntregaGratuita)],
        }));

    [Fact]
    public void Validar_OtraTasa_ExigeElIvaInformado_YElIvaExigeLasLineas()
    {
        Assert.Contains("IvaOtraTasa", TestData.Campos(TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.OtraTasa)] }));
        Assert.Contains("IvaOtraTasa", TestData.Campos(TestData.CfeValido() with { IvaOtraTasa = 10m }));
        Assert.Empty(TestData.Validar(TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.OtraTasa)], IvaOtraTasa = 10m }));
        Assert.Contains("IvaOtraTasa", TestData.Campos(TestData.CfeValido(TipoCfe.EFacturaExportacion) with { IvaOtraTasa = 10m }));
    }

    [Fact]
    public void Validar_IvaMinimoMonotributoJuntoConLineasGravadas_TieneError() =>
        Assert.Equal(["Detalle[1].IndFact"], TestData.Campos(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo), TestData.Linea()],
        }));

    [Theory]
    [InlineData(0)]
    [InlineData(100.5)]
    public void Validar_DescuentoGlobalConPorcentajeFueraDeRango_TieneError(decimal porcentaje) =>
        Assert.Contains("DescuentosRecargos[0].Porcentaje", TestData.Campos(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "x", Porcentaje = porcentaje }],
        }));

    [Fact]
    public void Validar_DescuentoGlobalSinMontoNiPorcentaje_TieneError() =>
        Assert.Contains("DescuentosRecargos[0].Monto", TestData.Campos(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "x" }],
        }));

    [Fact]
    public void Validar_PorcentajeSobreUnIndicadorSinLineas_TieneError() =>
        Assert.Contains("DescuentosRecargos[0].Porcentaje", TestData.Campos(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "x", Porcentaje = 10m, IndFact = IndicadorFacturacion.Exento }],
        }));

    [Fact]
    public void Validar_DescuentoGlobalSobreEntregaGratuita_TieneError() =>
        Assert.Contains("DescuentosRecargos[0].IndFact", TestData.Campos(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "x", Monto = 1m, IndFact = IndicadorFacturacion.EntregaGratuita }],
        }));

    [Fact]
    public void Validar_MandanteQueEsElPropioEmisor_TieneError() =>
        Assert.Contains("Mandante.Documento", TestData.Campos(TestData.CfeValido(TipoCfe.ETicketCuentaAjena) with
        {
            Emisor = TestData.Emisor(),
            Mandante = new Mandante { Documento = "219999820013", Nombre = "Yo mismo" },
        }));

    [Fact]
    public void Validar_ETicketCuentaAjenaSobreElTope_ExigeElReceptor() =>
        Assert.Contains("Receptor.Documento", TestData.Campos(TestData.CfeValido(TipoCfe.ETicketCuentaAjena), tope: 100m));

    [Fact]
    public void ValidarConCae_Exonerado_NoAdmiteLineasGravadas()
    {
        var cae = TestData.Cae() with { Especial = CaeEspecial.Exonerado, CausalEspecial = 1 };

        Assert.Equal(["Detalle[0].IndFact"], TestData.CfeValido().ValidarConCae(cae).Select(e => e.Campo));
        Assert.Empty((TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.Exento)] }).ValidarConCae(cae));
    }

    [Fact]
    public void ValidarConCae_IvaMinimo_SoloAdmiteSusIndicadores()
    {
        var cae = TestData.Cae() with { Especial = CaeEspecial.IvaMinimo };

        Assert.Equal(["Detalle[0].IndFact"], TestData.CfeValido().ValidarConCae(cae).Select(e => e.Campo));
        Assert.Empty((TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo)] }).ValidarConCae(cae));
    }

    [Fact]
    public void ValidarConCae_IndicadorIvaMinimoConCaeComun_SoloEnCuentaAjena()
    {
        var lineas = new[] { TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo) };

        Assert.Equal(["Detalle[0].IndFact"], (TestData.CfeValido() with { Detalle = lineas }).ValidarConCae(TestData.Cae()).Select(e => e.Campo));
        Assert.Empty((TestData.CfeValido(TipoCfe.ETicketCuentaAjena) with { Detalle = lineas }).ValidarConCae(TestData.Cae(TipoCfe.ETicketCuentaAjena)));
    }

    [Fact]
    public void ValidarConCae_MonotributoConEmisorSinIvaAlDia_TieneError() =>
        Assert.Contains("Emisor.IvaAlDia", (TestData.CfeValido() with
        {
            Emisor = TestData.Emisor() with { IvaAlDia = false },
            Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo)],
        }).ValidarConCae(TestData.Cae() with { Especial = CaeEspecial.Monotributo }).Select(e => e.Campo));
}
