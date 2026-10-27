using System.Text.Json;
using System.Xml;
using UruFactura.Xml;

namespace UruFactura.Tests;

/// <summary>
/// Reglas del formato de los CFE v25.2 que el XSD no controla: cálculos de los totales, obligatoriedad según el
/// tipo y las zonas que dependen de otros datos (e-Boleta de entrada, rebajas y anulaciones, retenciones,
/// secreto profesional, cobranza propia, bienes de terceros, montos brutos).
/// </summary>
public class FormatoDgiTests
{
    private static XmlDocument Xml(Cfe cfe, Cae? cae = null)
    {
        var doc = new XmlDocument();
        doc.LoadXml(TestData.Emitido(cfe, cae: cae).Xml);
        return doc;
    }

    private static XmlNamespaceManager Ns(XmlDocument doc)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", CfeXml.NsUri);
        return nsm;
    }

    private static string Texto(XmlDocument doc, string xpath) =>
        doc.SelectSingleNode(xpath, Ns(doc))?.InnerText ?? throw new InvalidOperationException($"No existe {xpath}");

    private static bool Existe(XmlDocument doc, string xpath) => doc.SelectSingleNode(xpath, Ns(doc)) is not null;

    private static void ValidoContraXsd(Cfe cfe, Cae? cae = null) => TestData.AssertValidoContraXsd(TestData.Sobre(TestData.Emitido(cfe, cae: cae)));

    private static RetencionPercepcion Retencion(string codigo, decimal valor) => new() { Codigo = codigo, MontoSujeto = valor * 10, Valor = valor };

    // ------------------------------------------------------------- montos brutos

    [Fact]
    public void IvaIncluido_DivideLasBasesPorUnoMasLaTasa()
    {
        var cfe = TestData.CfeValido() with
        {
            MontosBrutos = MontosBrutos.IvaIncluido,
            Detalle = [TestData.Linea(1, 122m), TestData.Linea(1, 110m, IndicadorFacturacion.TasaMinima), TestData.Linea(1, 50m, IndicadorFacturacion.Exento)],
        };

        var t = cfe.CalcularTotales();

        Assert.Equal((100m, 22m, 100m, 10m, 50m, 282m), (t.MontoNetoBasico, t.IvaBasico, t.MontoNetoMinimo, t.IvaMinimo, t.MontoNoGravado, t.MontoTotal));
        Assert.Equal("1", Texto(Xml(cfe), "//c:IdDoc/c:MntBruto"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void IvaIncluido_ConOtraTasaOEnOtroTipo_NoSeAdmite()
    {
        Assert.Contains("MontosBrutos", TestData.Campos(TestData.CfeValido() with
        {
            MontosBrutos = MontosBrutos.IvaIncluido, Detalle = [TestData.Linea(iva: IndicadorFacturacion.OtraTasa)], IvaOtraTasa = 10m,
        }));
        Assert.Contains("MontosBrutos", TestData.Campos(TestData.CfeValido(TipoCfe.EFacturaExportacion) with { MontosBrutos = MontosBrutos.IvaIncluido }));
        Assert.Contains("MontosBrutos", TestData.Campos(TestData.CfeValido() with { MontosBrutos = MontosBrutos.ImebaYAdicionalesIncluidos }));
    }

    [Fact]
    public void IvaIncluido_ConCaeDeIvaMinimo_NoSeAdmite() =>
        Assert.Contains((TestData.CfeValido() with { MontosBrutos = MontosBrutos.IvaIncluido, Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaMinimoMonotributo)] })
                .ValidarConCae(TestData.Cae() with { Especial = CaeEspecial.IvaMinimo }),
            e => e.Campo == "MontosBrutos");

    // ------------------------------------------------- retenciones fuera del e-Resguardo

    [Fact]
    public void Retenciones_EnEFactura_SumanAlTotalAPagarSalvoLosCreditosFiscales()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura) with
        {
            Detalle =
            [
                TestData.Linea(1, 1000m) with
                {
                    AgenteResponsable = AgenteResponsable.Producto,
                    Retenciones = [Retencion("2183211", 50m), Retencion("2181410", 10m)],
                },
            ],
        };

        var t = cfe.CalcularTotales();
        var doc = Xml(cfe);

        Assert.Equal((1220m, 50m, 10m, 1270m), (t.MontoTotal, t.MontoRetenido, t.MontoCreditoFiscal, t.MontoPagar));
        Assert.Equal("R", Texto(doc, "//c:Item/c:IndAgenteResp"));
        Assert.Equal("50.00", Texto(doc, "//c:Totales/c:MntTotRetenido"));
        Assert.Equal("10.00", Texto(doc, "//c:Totales/c:MntTotCredFisc"));
        Assert.Equal("50.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='2183211']/c:ValRetPerc"));
        Assert.Equal("1270.00", Texto(doc, "//c:Totales/c:MntPagar"));
        Assert.Contains("1270.00", TestData.Emitido(cfe).UrlQr);
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void Retenciones_SinAgenteResponsable_FallaYElAgenteSinRetencionesTambien()
    {
        Assert.Contains("Detalle[0].AgenteResponsable", TestData.Campos(TestData.CfeValido() with
        {
            Receptor = new Receptor { Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
            Detalle = [TestData.Linea() with { Retenciones = [Retencion("2183211", 1m)] }],
        }));
        Assert.Contains("Detalle[0].AgenteResponsable", TestData.Campos(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea() with { AgenteResponsable = AgenteResponsable.ConceptoAsociado }],
        }));
    }

    [Fact]
    public void ETicketConRetenciones_IdentificaAlReceptor()
    {
        var linea = TestData.Linea() with { AgenteResponsable = AgenteResponsable.Producto, Retenciones = [Retencion("2183211", 1m)] };

        Assert.Contains("Receptor", TestData.Campos(TestData.CfeValido() with { Detalle = [linea] }));
        Assert.Contains("Receptor.Documento", TestData.Campos(TestData.CfeValido() with { Detalle = [linea], Receptor = new Receptor { RazonSocial = "x" } }));
        Assert.Empty(TestData.Validar(TestData.CfeValido() with
        {
            Detalle = [linea], Receptor = new Receptor { Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad },
        }));
    }

    [Theory]
    [InlineData("R1")]
    [InlineData("0")]
    [InlineData("123456789")]
    public void CodigoDeRetencion_EsNumericoDeHasta8Digitos(string codigo) =>
        Assert.Contains("Detalle[0].Retenciones[0].Codigo", TestData.Campos(TestData.CfeValido(TipoCfe.EResguardo) with
        {
            Detalle = [new LineaDetalle { Retenciones = [Retencion(codigo, 1m)] }],
        }));

    [Fact]
    public void Resguardo_RetencionSinMontoSujeto_Falla() =>
        Assert.Contains("Detalle[0].Retenciones[0].MontoSujeto", TestData.Campos(TestData.CfeValido(TipoCfe.EResguardo) with
        {
            Detalle = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "2183211", Valor = 1m }] }],
        }));

    // ------------------------------------------------------ rebajas y anulaciones

    [Fact]
    public void RemitoExportacion_ItemARebajar_RestaDelTotalEInformaElIndicador8()
    {
        var cfe = TestData.CfeValido(TipoCfe.ERemitoExportacion) with
        {
            Detalle = [TestData.Linea(1, 100m), TestData.Linea(1, 30m, IndicadorFacturacion.RebajaRemito), TestData.Linea(2, 0m, IndicadorFacturacion.EntregaGratuita)],
            Referencias = [new RefCfe { TipoCfe = TipoCfe.ERemitoExportacion, Serie = "A", NroCfe = 5 }],
        };

        var doc = Xml(cfe);

        Assert.Equal(70m, cfe.CalcularTotales().MontoTotal);
        Assert.False(Existe(doc, "//c:Item[1]/c:IndFact"));
        Assert.Equal("8", Texto(doc, "//c:Item[2]/c:IndFact"));
        Assert.Equal("5", Texto(doc, "//c:Item[3]/c:IndFact"));
        Assert.Equal("70.00", Texto(doc, "//c:Totales/c:MntExpoyAsim"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void RemitoExportacion_SoloRebajas_TotalNegativo()
    {
        var cfe = TestData.CfeValido(TipoCfe.ERemitoExportacion) with
        {
            Detalle = [TestData.Linea(1, 30m, IndicadorFacturacion.RebajaRemito)],
            Referencias = [new RefCfe { TipoCfe = TipoCfe.ERemitoExportacion, Serie = "A", NroCfe = 5 }],
        };

        Assert.Equal("-30.00", Texto(Xml(cfe), "//c:Totales/c:MntTotal"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void Rebaja_SinReferenciaAlRemito_Falla() =>
        Assert.Contains("Referencias", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with
        {
            Detalle = [TestData.Linea(1, 0m, IndicadorFacturacion.RebajaRemito)],
        }));

    [Fact]
    public void ERemito_InformaElIndicadorSoloEnLasRebajas()
    {
        var cfe = TestData.CfeValido(TipoCfe.ERemito) with
        {
            Detalle = [TestData.Linea(3, 0m), TestData.Linea(1, 0m, IndicadorFacturacion.RebajaRemito)],
            Referencias = [new RefCfe { TipoCfe = TipoCfe.ERemito, Serie = "A", NroCfe = 5 }],
        };

        var doc = Xml(cfe);

        Assert.False(Existe(doc, "//c:Item[1]/c:IndFact"));
        Assert.Equal("8", Texto(doc, "//c:Item[2]/c:IndFact"));
        Assert.Equal(["3", "-1"], CfePdf.Filas(TestData.Emitido(cfe)).Select(f => f[0]));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void Resguardo_ItemAAnular_RestaSusRetenciones()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo) with
        {
            Detalle =
            [
                new LineaDetalle { Retenciones = [Retencion("2183211", 120m)] },
                new LineaDetalle { IndFact = IndicadorFacturacion.AnulacionResguardo, Retenciones = [Retencion("2183211", 20m), Retencion("1146171", 5m)] },
            ],
            Referencias = [new RefCfe { TipoCfe = TipoCfe.EResguardo, Serie = "A", NroCfe = 3 }],
        };

        var doc = Xml(cfe);

        Assert.Equal(95m, cfe.CalcularTotales().MontoRetenido);
        Assert.Equal("9", Texto(doc, "//c:Item[2]/c:IndFact"));
        Assert.Equal("100.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='2183211']/c:ValRetPerc"));
        Assert.Equal("-5.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='1146171']/c:ValRetPerc"));
        ValidoContraXsd(cfe);
    }

    // ------------------------------------------------------------- e-Boleta de entrada

    [Fact]
    public void EBoleta_ConImebaIncluido_LaRetencionRestaDelTotalAPagar()
    {
        var cfe = TestData.CfeValido(TipoCfe.EBoleta) with
        {
            MontosBrutos = MontosBrutos.ImebaYAdicionalesIncluidos,
            Detalle =
            [
                TestData.Linea(1, 1000m, IndicadorFacturacion.VendedorImeba) with
                {
                    AgenteResponsable = AgenteResponsable.Producto, Retenciones = [Retencion("2183211", 50m)],
                },
                TestData.Linea(1, 200m, IndicadorFacturacion.IvaEnSuspenso),
            ],
        };

        var t = cfe.CalcularTotales();
        var doc = Xml(cfe);

        Assert.Equal((1000m, 200m, 1200m, 50m, 1150m), (t.MontoNoGravado, t.MontoNetoSuspendido, t.MontoTotal, t.MontoRetenido, t.MontoPagar));
        Assert.Equal("eBoleta", doc.DocumentElement!.FirstChild!.LocalName);
        Assert.Equal("2", Texto(doc, "//c:IdDoc/c:MntBruto"));
        Assert.Equal("1150.00", Texto(doc, "//c:Totales/c:MntPagar"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void EBoleta_ConIvaEnSuspensoSinImeba_Falla() =>
        Assert.Contains("MontosBrutos", TestData.Campos(TestData.CfeValido(TipoCfe.EBoleta) with
        {
            Detalle = [TestData.Linea(iva: IndicadorFacturacion.IvaEnSuspenso)],
        }));

    [Fact]
    public void EBoleta_ElIndicadorDiceSiElVendedorTieneRut()
    {
        Assert.Contains("Detalle[0].IndFact", TestData.Campos(TestData.CfeValido(TipoCfe.EBoleta) with
        {
            Detalle = [TestData.Linea(iva: IndicadorFacturacion.VendedorNoContribuyente)],
        }));
        var sinRut = new Receptor
        {
            Documento = "12345672", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad, RazonSocial = "Juan", Direccion = "Ruta 5 km 30", Ciudad = "Canelones",
        };
        Assert.Contains("Detalle[0].IndFact", TestData.Campos(TestData.CfeValido(TipoCfe.EBoleta) with
        {
            Receptor = sinRut, Detalle = [TestData.Linea(iva: IndicadorFacturacion.VendedorIvaMinimo)],
        }));
        Assert.Empty(TestData.Validar(TestData.CfeValido(TipoCfe.EBoleta) with
        {
            Receptor = sinRut, Detalle = [TestData.Linea(iva: IndicadorFacturacion.VendedorNoContribuyente)],
        }));
    }

    [Fact]
    public void EBoleta_SinVendedor_Falla() =>
        Assert.Contains("Receptor", TestData.Campos(TestData.CfeValido(TipoCfe.EBoleta) with { Receptor = null }));

    [Fact]
    public void EBoleta_IndicadoresDeTicketOFactura_NoSeAdmiten() =>
        Assert.Contains("Detalle[0].IndFact", TestData.Campos(TestData.CfeValido(TipoCfe.EBoleta) with { Detalle = [TestData.Linea()] }));

    [Fact]
    public void IndicadoresDeLaEBoleta_SoloEnLaEBoleta() =>
        Assert.Contains("Detalle[0].IndFact", TestData.Campos(TestData.CfeValido() with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.VendedorImeba)] }));

    [Fact]
    public void CompraMonedaExtranjera_PorCuentaAjena_InformaElIndicadorYElMandante()
    {
        var cfe = new Cfe(TipoCfe.EBoleta)
        {
            FechaEmision = TestData.Dia,
            CompraMonedaExtranjera = CompraMonedaExtranjera.ReventaPorCuentaAjena,
            Detalle = [TestData.Linea(100, 40m, IndicadorFacturacion.CompraMonedaExtranjera)],
            Mandante = new Mandante { Documento = "214844360018", Nombre = "Casa de Cambio S.A." },
        };

        var doc = Xml(cfe);

        Assert.Equal("2", Texto(doc, "//c:IdDoc/c:IndCompraMEReventa"));
        Assert.Equal("214844360018", Texto(doc, "//c:Compl_Fiscal_Data/c:DocMdte"));
        Assert.False(Existe(doc, "//c:Receptor"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void CompraMonedaExtranjera_Reglas()
    {
        var compra = new Cfe(TipoCfe.EBoleta)
        {
            FechaEmision = TestData.Dia,
            CompraMonedaExtranjera = CompraMonedaExtranjera.Reventa,
            Detalle = [TestData.Linea(100, 40m, IndicadorFacturacion.CompraMonedaExtranjera)],
        };

        Assert.Empty(TestData.Validar(compra, tope: 32_000m));
        // Por encima del tope identifica al vendedor.
        Assert.Contains("Receptor", TestData.Campos(compra with { Detalle = [TestData.Linea(1000, 40m, IndicadorFacturacion.CompraMonedaExtranjera)] }, tope: 32_000m));
        Assert.Contains("Moneda", TestData.Campos(compra with { Moneda = Moneda.DolarAmericano, TipoCambio = 40m }));
        Assert.Contains("Detalle[0].IndFact", TestData.Campos(compra with { Detalle = [TestData.Linea(iva: IndicadorFacturacion.VendedorNoContribuyente)] }));
        Assert.Contains("Detalle[0].IndFact", TestData.Campos(compra with { CompraMonedaExtranjera = null }));
        Assert.Contains("Mandante", TestData.Campos(compra with { CompraMonedaExtranjera = CompraMonedaExtranjera.ReventaPorCuentaAjena }));
        Assert.Contains("Mandante.TipoDocumento", TestData.Campos(compra with
        {
            CompraMonedaExtranjera = CompraMonedaExtranjera.ReventaPorCuentaAjena,
            Mandante = new Mandante { TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad, Documento = "12345672", Nombre = "x" },
        }));
        Assert.Contains("SecretoProfesional", TestData.Campos(compra with
        {
            CompraMonedaExtranjera = CompraMonedaExtranjera.ReventaPorCuentaAjena, SecretoProfesional = true, Adenda = "x",
            Mandante = new Mandante { Documento = "214844360018", Nombre = "x" },
        }));
        Assert.Contains("CompraMonedaExtranjera", TestData.Campos(TestData.CfeValido() with { CompraMonedaExtranjera = CompraMonedaExtranjera.Reventa }));
    }

    [Fact]
    public void EBoleta_NoSeEnviaAlVendedor()
    {
        var cfe = TestData.CfeValido(TipoCfe.EBoleta);

        Assert.Equal("RUC VENDEDOR", CfePdf.Receptor(cfe).Recuadro);
        Assert.StartsWith("e-Boleta de entrada", CfePdf.NombreCompleto(TipoCfe.EBoleta));
        Assert.Null(CfePdf.ConstanciaIvaAlDia(TestData.Emitido(cfe)));
    }

    // ------------------------------------------------------------- indicadores de la operación

    [Fact]
    public void SecretoProfesional_ReceptorOpcionalConLaIdentificacionEnLaAdenda()
    {
        var sinReceptor = TestData.CfeValido(TipoCfe.EFactura) with { SecretoProfesional = true, Receptor = null };

        Assert.Contains("Adenda", TestData.Campos(sinReceptor));
        var cfe = sinReceptor with { Adenda = "Receptor: Juan Pérez, C.I. 1.234.567-2" };
        Assert.Empty(TestData.Validar(cfe));
        Assert.Equal("1", Texto(Xml(cfe), "//c:IdDoc/c:SecProf"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void SecretoProfesional_EnCuentaAjena_InformaDosYNoExigeElMandante()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFacturaCuentaAjena) with { SecretoProfesional = true, Mandante = null };

        Assert.Empty(TestData.Validar(cfe));
        Assert.Equal("2", Texto(Xml(cfe), "//c:IdDoc/c:SecProf"));
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void PagoCuentaTerceros_SeInformaYElReporteLoAgrupaAparte()
    {
        var reintegro = TestData.CfeValido() with { PagoCuentaTerceros = true };
        var cfes = new[] { TestData.Emitido(reintegro, 1), TestData.Emitido(TestData.CfeValido(), 2) };

        var reporte = ReporteDiario.Generar("219999820013", TestData.Dia, 1, 1, new DateTimeOffset(2026, 6, 16, 1, 0, 0, TimeSpan.Zero), cfes);

        Assert.Equal("1", Texto(Xml(reintegro), "//c:IdDoc/c:IndPagCta3ros"));
        Assert.Equal(2, reporte.Split("<Mnts_FyT_Item>").Length - 1);
        Assert.Contains("<IndPagCta3ros>1</IndPagCta3ros>", reporte);
        Assert.Contains("Pagos por cuenta de terceros", CfePdf.Leyendas(reintegro));
        Assert.Contains("PagoCuentaTerceros", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with { PagoCuentaTerceros = true }));
    }

    [Fact]
    public void CobranzaPropia_LineasNoFacturablesConReferenciaYMontoNoFacturable()
    {
        var cfe = TestData.CfeValido() with
        {
            CobranzaPropia = true,
            Detalle = [TestData.Linea(1, 500m, IndicadorFacturacion.NoFacturable)],
            Referencias = [new RefCfe { TipoCfe = TipoCfe.EFactura, Serie = "A", NroCfe = 10 }],
        };

        var doc = Xml(cfe);

        Assert.Equal("1", Texto(doc, "//c:IdDoc/c:IndCobPropia"));
        Assert.Equal("500.00", Texto(doc, "//c:Totales/c:MontoNF"));
        Assert.Equal("500.00", Texto(doc, "//c:Totales/c:MntPagar"));
        Assert.Equal("e-Ticket Cobranza", CfePdf.NombreCompleto(cfe));
        ValidoContraXsd(cfe);

        Assert.Contains("Detalle[0].IndFact", TestData.Campos(cfe with { Detalle = [TestData.Linea()] }));
        Assert.Contains("Referencias", TestData.Campos(cfe with { Referencias = [] }));
        Assert.Contains("DescuentosRecargos", TestData.Campos(cfe with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "d", Monto = 1m, IndFact = IndicadorFacturacion.NoFacturable }],
        }));
        Assert.Contains("CobranzaPropia", TestData.Campos(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with { CobranzaPropia = true }));
    }

    [Fact]
    public void BienesDeTerceros_InformaAlPropietarioEnElRemito()
    {
        var cfe = TestData.CfeValido(TipoCfe.ERemito) with
        {
            Propietario = new Propietario { TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "AR", Documento = "AB123", Nombre = "Dueño SA" },
        };

        var doc = Xml(cfe);

        Assert.Equal("1", Texto(doc, "//c:IdDoc/c:IndPropiedad"));
        Assert.Equal("AB123", Texto(doc, "//c:IdDoc/c:DocPropExt"));
        Assert.Equal("Dueño SA", Texto(doc, "//c:IdDoc/c:RznSocProp"));
        Assert.Contains(CfePdf.Leyendas(cfe), l => l.StartsWith("Bienes de Terceros - Dueño SA", StringComparison.Ordinal));
        ValidoContraXsd(cfe);

        Assert.Contains("Propietario.Documento", TestData.Campos(cfe with { Propietario = cfe.Propietario! with { TipoDocumento = TipoDocumentoReceptor.Rut, CodigoPais = "UY" } }));
        Assert.Contains("Propietario", TestData.Campos(TestData.CfeValido() with { Propietario = cfe.Propietario }));
    }

    [Fact]
    public void FechaValor_SoloEnElResguardoYNoPosteriorAlMes()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo) with { FechaValor = new DateOnly(2026, 5, 20) };

        Assert.Equal("2026-05-20", Texto(Xml(cfe), "//c:IdDoc/c:FchValor"));
        Assert.Contains("Fecha valor: 20/05/2026", CfePdf.Leyendas(cfe));
        ValidoContraXsd(cfe);
        Assert.Contains("FechaValor", TestData.Campos(cfe with { FechaValor = new DateOnly(2026, 7, 1) }));
        Assert.Contains("FechaValor", TestData.Campos(TestData.CfeValido() with { FechaValor = TestData.Dia }));
    }

    [Fact]
    public void Periodo_SeInformaYNoTerminaAntesDeEmpezar()
    {
        var cfe = TestData.CfeValido() with { PeriodoDesde = new DateOnly(2026, 5, 1), PeriodoHasta = new DateOnly(2026, 5, 31) };

        Assert.Equal("2026-05-31", Texto(Xml(cfe), "//c:IdDoc/c:PeriodoHasta"));
        ValidoContraXsd(cfe);
        Assert.Contains("PeriodoHasta", TestData.Campos(cfe with { PeriodoHasta = new DateOnly(2026, 4, 30) }));
        Assert.Contains("PeriodoDesde", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with { PeriodoDesde = TestData.Dia }));
    }

    [Theory]
    [InlineData(TipoCfe.ETicket, 80, true)]
    [InlineData(TipoCfe.ETicket, 90, true)]
    [InlineData(TipoCfe.EFactura, 90, true)]
    [InlineData(TipoCfe.EFactura, 80, false)]
    [InlineData(TipoCfe.ETicketCuentaAjena, 90, false)]
    [InlineData(TipoCfe.EFacturaExportacion, 91, true)]
    [InlineData(TipoCfe.EFacturaExportacion, 80, false)]
    [InlineData(TipoCfe.EBoleta, 90, false)]
    public void ModalidadDeVenta_SegunElTipo(TipoCfe tipo, int modalidad, bool admitida)
    {
        var cfe = TestData.CfeValido(tipo) with { ModalidadVenta = modalidad };

        Assert.Equal(!admitida, TestData.Campos(cfe).Contains("ModalidadVenta"));
        if (admitida)
        {
            Assert.Equal(modalidad.ToString(), Texto(Xml(cfe), "//c:IdDoc/c:ModVenta"));
            ValidoContraXsd(cfe);
        }
    }

    [Fact]
    public void ViaDeTransporte_NoAplica()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFacturaExportacion) with { ViaTransporte = 8 };

        Assert.Empty(TestData.Validar(cfe));
        ValidoContraXsd(cfe);
        Assert.Contains("ViaTransporte", TestData.Campos(TestData.CfeValido() with { ViaTransporte = 1 }));
    }

    // ------------------------------------------------------------- receptor

    [Fact]
    public void Receptor_SegunElTipo()
    {
        Assert.Contains("Receptor.Direccion", TestData.Campos(TestData.CfeValido(TipoCfe.EFactura) with { Receptor = TestData.ReceptorRut() with { Direccion = null } }));
        Assert.Contains("Receptor", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with { Receptor = null }));
        Assert.Contains("Receptor.Documento", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with { Receptor = TestData.ReceptorRut() with { Documento = null } }));
        Assert.Contains("Receptor.Departamento", TestData.Campos(TestData.CfeValido(TipoCfe.EFacturaExportacion) with
        {
            Receptor = TestData.CfeValido(TipoCfe.EFacturaExportacion).Receptor! with { Departamento = null },
        }));
        // El e-Remito y el e-Resguardo identifican al receptor con cualquier documento.
        Assert.Empty(TestData.Validar(TestData.CfeValido(TipoCfe.ERemito) with
        {
            Receptor = TestData.ReceptorRut() with { TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad, Documento = "12345672" },
        }));
        Assert.Empty(TestData.Validar(TestData.CfeValido(TipoCfe.EResguardo) with
        {
            Receptor = new Receptor { TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad, Documento = "12345672", RazonSocial = "Juan Pérez" },
        }));
    }

    [Theory]
    [InlineData(TipoDocumentoReceptor.Rut, "AR", "214844360018", "Receptor.CodigoPais")]
    [InlineData(TipoDocumentoReceptor.CedulaIdentidad, "UY", "12345678", "Receptor.Documento")]
    [InlineData(TipoDocumentoReceptor.Nie, "UY", "9A123", "Receptor.Documento")]
    [InlineData(TipoDocumentoReceptor.Dni, "UY", "30111222", "Receptor.CodigoPais")]
    [InlineData(TipoDocumentoReceptor.Pasaporte, "ZZ", "P1", "Receptor.CodigoPais")]
    public void DocumentoDelReceptor_TipoPaisYDigitoVerificador(TipoDocumentoReceptor tipo, string pais, string documento, string campo) =>
        Assert.Contains(campo, TestData.Campos(TestData.CfeValido() with { Receptor = new Receptor { TipoDocumento = tipo, CodigoPais = pais, Documento = documento } }));

    [Fact]
    public void Mandante_ConRutInvalido_Falla() =>
        Assert.Contains("Mandante.Documento", TestData.Campos(TestData.CfeValido(TipoCfe.ETicketCuentaAjena) with
        {
            Mandante = new Mandante { Documento = "214844360019", Nombre = "x" },
        }));

    [Theory]
    [InlineData("12345672", true)]
    [InlineData("1234567-2", false)]
    [InlineData("12345678", false)]
    [InlineData("4444442", true)]
    [InlineData("4444443", false)]
    [InlineData("12345", false)]
    public void Ci_DigitoVerificador(string ci, bool valida) => Assert.Equal(valida, Ci.EsValida(ci));

    // ------------------------------------------------------------- líneas

    [Fact]
    public void Linea_PrecioCero_SoloEnEntregasGratuitas()
    {
        Assert.Contains("Detalle[0].PrecioUnitario", TestData.Campos(TestData.CfeValido() with { Detalle = [TestData.Linea(1, 0m)] }));
        Assert.Empty(TestData.Validar(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(), TestData.Linea(1, 0m, IndicadorFacturacion.EntregaGratuita)],
        }));
    }

    [Fact]
    public void Linea_CantidadNegativa_EliminaUnItemDelPropioComprobante()
    {
        var cfe = TestData.CfeValido() with { Detalle = [TestData.Linea(3, 100m), TestData.Linea(-1, 100m)] };

        Assert.Equal(200m, cfe.CalcularTotales().MontoNetoBasico);
        ValidoContraXsd(cfe);
        Assert.Contains("Detalle[1].Cantidad", TestData.Campos(cfe with { Detalle = [TestData.Linea(3, 100m), TestData.Linea(-1, 100m) with { DescuentoMonto = 1m }] }));
        Assert.Contains("DescuentosRecargos", TestData.Campos(cfe with { Detalle = [TestData.Linea(1, 100m), TestData.Linea(-3, 100m)] }));
        Assert.Contains("Detalle[0].Cantidad", TestData.Campos(cfe with { Detalle = [TestData.Linea(0, 100m)] }));
    }

    [Fact]
    public void Linea_RecargoEnPorcentajeYDistribucion()
    {
        var cfe = TestData.CfeValido() with
        {
            Detalle =
            [
                TestData.Linea(2, 100m) with
                {
                    RecargoPorcentaje = 10m, DescuentoMonto = 5m,
                    Subdescuentos = [new DistribucionAjuste { Tipo = TipoValor.Monto, Valor = 5m }],
                    Subrecargos = [new DistribucionAjuste { Tipo = TipoValor.Porcentaje, Valor = 10m }],
                },
            ],
        };

        var doc = Xml(cfe);

        Assert.Equal(215m, cfe.Detalle[0].MontoTotal);
        Assert.Equal("10.000", Texto(doc, "//c:Item/c:RecargoPct"));
        Assert.Equal("20.00", Texto(doc, "//c:Item/c:RecargoMnt"));
        Assert.Equal("2", Texto(doc, "//c:Item/c:SubRecargo/c:RecargoTipo"));
        Assert.Equal("5.00", Texto(doc, "//c:Item/c:SubDescuento/c:DescVal"));
        ValidoContraXsd(cfe);
        Assert.Contains("Detalle[0].Subdescuentos", TestData.Campos(cfe with { Detalle = [TestData.Linea() with { Subdescuentos = [new DistribucionAjuste { Valor = 1m }] }] }));
    }

    [Fact]
    public void Ncm_SoloEnExportacion()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFacturaExportacion) with { Detalle = [TestData.Linea() with { Ncm = "0201.30.00" }] };

        Assert.Equal("0201.30.00", Texto(Xml(cfe), "//c:Item/c:NCM"));
        Assert.Contains("(NCM 0201.30.00)", CfePdf.Filas(TestData.Emitido(cfe)).Single()[1]);
        ValidoContraXsd(cfe);
        Assert.Contains("Detalle[0].Ncm", TestData.Campos(TestData.CfeValido() with { Detalle = [TestData.Linea() with { Ncm = "1" }] }));
    }

    [Fact]
    public void CodigoGtin_ValidaElDigitoVerificador()
    {
        Assert.Contains("Detalle[0].Codigos[0].Codigo", TestData.Campos(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea() with { Codigos = [new CodigoItem { Tipo = "GTIN13", Codigo = "7790001000001" }] }],
        }));
        Assert.Empty(TestData.Validar(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea() with { Codigos = [new CodigoItem { Tipo = "GTIN13", Codigo = "7790001000002" }] }],
        }));
    }

    [Fact]
    public void EFactura_AdmiteHasta200Lineas() =>
        Assert.Contains("Detalle", TestData.Campos(TestData.CfeValido(TipoCfe.EFactura) with { Detalle = [.. Enumerable.Repeat(TestData.Linea(), 201)] }));

    [Fact]
    public void SubtotalesInformativos_NoCambianLosTotales()
    {
        var cfe = TestData.CfeValido() with { SubtotalesInformativos = [new SubtotalInformativo { Glosa = "Bebidas", Valor = 150m, Orden = 1 }] };

        Assert.Equal(TestData.CfeValido().CalcularTotales(), cfe.CalcularTotales());
        Assert.Equal("Bebidas", Texto(Xml(cfe), "//c:SubTotInfo/c:STI_Item/c:GlosaSTI"));
        ValidoContraXsd(cfe);
        Assert.Contains("SubtotalesInformativos[0].Orden", TestData.Campos(cfe with { SubtotalesInformativos = [new SubtotalInformativo { Glosa = "x", Orden = 0 }] }));
    }

    // ------------------------------------------------------------- moneda y UI

    [Fact]
    public void Moneda_CualquierCodigoDeLaTablaYSeGuardaComoCodigo()
    {
        var cfe = TestData.CfeValido() with { Moneda = new Moneda("clp"), TipoCambio = 0.042m };

        Assert.Equal("CLP", Texto(Xml(cfe), "//c:Totales/c:TpoMoneda"));
        Assert.Equal(Moneda.DolarAmericano, new Moneda(" usd "));
        Assert.Equal("\"UYI\"", JsonSerializer.Serialize(Moneda.UnidadIndexada));
        Assert.Equal(Moneda.Euro, JsonSerializer.Deserialize<Moneda>("\"EUR\""));
        Assert.Equal(Moneda.PesoUruguayo, default);
        ValidoContraXsd(cfe);
    }

    [Fact]
    public void UnidadIndexada_ElTopeSaleDeLaUiDelAnioAnterior()
    {
        var config = TestData.Config() with { UnidadIndexada = new Dictionary<int, decimal> { [2025] = 0.01m, [2026] = 1_000m } };

        Assert.Equal(50m, config.TopeMontoNeto(TestData.Dia));
        Assert.Equal(5_000_000m, config.TopeMontoNeto(new DateOnly(2027, 1, 1)));
        Assert.Null(config.TopeMontoNeto(new DateOnly(2030, 1, 1)));
        Assert.Throws<UruFacturaException>(() => (config with { Ambiente = Ambiente.Produccion }).TopeMontoNeto(new DateOnly(2030, 1, 1)));
    }
}
