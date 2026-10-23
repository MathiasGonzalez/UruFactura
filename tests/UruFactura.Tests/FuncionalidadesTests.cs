using static UruFactura.Tests.CfeXmlTests;

namespace UruFactura.Tests;

/// <summary>
/// Funcionalidades ampliadas del CFE (descuentos globales, medios de pago, retenciones, cuenta ajena,
/// contingencia, datos adicionales, QR), validadas contra los XSD oficiales de la DGI.
/// </summary>
public class FuncionalidadesTests
{
    private static void EsValidoContraXsd(Cfe cfe) => TestData.AssertValidoContraXsd(TestData.Sobre(TestData.Emitido(cfe)));

    public static IEnumerable<object[]> TodosLosTipos() => Enum.GetValues<TipoCfe>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void CadaTipoDeCfe_TieneFamiliaYClasificacionCoherente(TipoCfe tipo)
    {
        var familia = tipo.Familia();
        if (tipo.EsContingencia())
            Assert.True((int)tipo >= 200);
        if (tipo.EsRemito())
            Assert.True(familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion);
        Assert.False(tipo.EsNotaCorreccion() && familia is FamiliaCfe.Remito or FamiliaCfe.RemitoExportacion or FamiliaCfe.Resguardo);
    }

    [Theory]
    [InlineData(TipoCfe.NotaCreditoETicket, true)]
    [InlineData(TipoCfe.NotaDebitoETicket, false)]
    [InlineData(TipoCfe.NotaCreditoEFacturaContingencia, true)]
    [InlineData(TipoCfe.EResguardo, false)]
    [InlineData(TipoCfe.EResguardoContingencia, false)]
    public void EsNotaCredito(TipoCfe tipo, bool esperado) => Assert.Equal(esperado, tipo.EsNotaCredito());

    [Fact]
    public void Familia_TipoDesconocido_LanzaArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ((TipoCfe)999).Familia());

    // ------------------------------------------------------------ descuentos / recargos globales

    [Fact]
    public void DescuentoGlobal_ReduceLaBaseDelIva()
    {
        var cfe = TestData.CfeValido() with // neto 200 al 22 %
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Descuento, Glosa = "Promo", Monto = 50m }],
        };

        var doc = XmlDe(cfe);

        Assert.Equal("150.00", Texto(doc, "//c:Totales/c:MntNetoIVATasaBasica"));
        Assert.Equal("33.00", Texto(doc, "//c:Totales/c:MntIVATasaBasica"));
        Assert.Equal("183.00", Texto(doc, "//c:Totales/c:MntTotal"));
        Assert.Equal("D", Texto(doc, "//c:DRG_Item/c:TpoMovDR"));
        Assert.Equal("1", Texto(doc, "//c:DRG_Item/c:TpoDR"));
        Assert.Equal("3", Texto(doc, "//c:DRG_Item/c:IndFactDR"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void RecargoGlobal_AumentaLaBaseYSeValidaContraXsd()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura) with
        {
            Detalle = [TestData.Linea(), new LineaDetalle { NombreItem = "Libro", Cantidad = 1, PrecioUnitario = 100m, IndFactIva = TipoIva.Exento }],
            DescuentosRecargos =
            [
                new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Recargo, Glosa = "Flete", Monto = 100m, Codigo = 7 },
                new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Descuento, Glosa = "Exento", Monto = 10m, IndFact = TipoIva.Exento },
            ],
        };

        var doc = XmlDe(cfe);

        Assert.Equal("300.00", Texto(doc, "//c:Totales/c:MntNetoIVATasaBasica"));
        Assert.Equal("90.00", Texto(doc, "//c:Totales/c:MntNoGrv"));
        Assert.Equal("7", Texto(doc, "//c:DRG_Item/c:CodDR"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void DescuentoGlobal_EnExportacion_UsaIndFact10()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFacturaExportacion) with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "Pronto pago", Monto = 20m }],
        };

        var doc = XmlDe(cfe);

        Assert.Equal("10", Texto(doc, "//c:DRG_Item/c:IndFactDR"));
        Assert.Equal("180.00", Texto(doc, "//c:Totales/c:MntTotal"));
        EsValidoContraXsd(cfe);
    }

    [Theory]
    [InlineData(0, "Glosa")]
    [InlineData(-5, "Glosa")]
    [InlineData(10, "")]
    public void DescuentoGlobal_Invalido_FallaLaValidacion(double monto, string glosa) =>
        Assert.NotEmpty(TestData.Validar(TestData.CfeValido() with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = glosa, Monto = (decimal)monto }],
        }));

    [Fact]
    public void DescuentosGlobales_EnRemito_NoSeAdmiten() =>
        Assert.Contains("DescuentosRecargos", TestData.Campos(TestData.CfeValido(TipoCfe.ERemito) with
        {
            DescuentosRecargos = [new DescuentoRecargoGlobal { Glosa = "x", Monto = 1m }],
        }));

    // ------------------------------------------------------------ medios de pago

    [Fact]
    public void MediosDePago_SeEscribenEnOrdenYValidanContraXsd()
    {
        var cfe = TestData.CfeValido() with
        {
            MediosPago =
            [
                new MedioPago { Glosa = "Efectivo", Valor = 144m, Codigo = 1, Orden = 1 },
                new MedioPago { Glosa = "Tarjeta de crédito", Valor = 100m, Orden = 2 },
            ],
        };

        var doc = XmlDe(cfe);

        Assert.Equal("1", Texto(doc, "//c:MedioPago[1]/c:NroLinMP"));
        Assert.Equal("Efectivo", Texto(doc, "//c:MedioPago[1]/c:GlosaMP"));
        Assert.Equal("2", Texto(doc, "//c:MedioPago[2]/c:NroLinMP"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void MediosDePago_SinGlosa_FallaLaValidacion() =>
        Assert.Contains("MediosPago[0].Glosa", TestData.Campos(TestData.CfeValido() with { MediosPago = [new MedioPago { Glosa = " ", Valor = 1m }] }));

    // ------------------------------------------------------------ e-Resguardo

    [Fact]
    public void EResguardo_TotalizaRetencionesPorCodigo()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo);
        cfe = cfe with
        {
            Detalle = [.. cfe.Detalle, new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "R1", MontoSujeto = 100m, Valor = 10m }] }],
        };

        var doc = XmlDe(cfe);

        Assert.Equal("155.00", Texto(doc, "//c:Totales/c:MntTotRetenido"));
        Assert.Equal("2", Texto(doc, "//c:Totales/c:CantLinDet"));
        Assert.Equal("130.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='R1']/c:ValRetPerc"));
        Assert.Equal("25.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='R2']/c:ValRetPerc"));
        Assert.Equal("12.000", Texto(doc, "//c:Item[1]/c:RetencPercep[1]/c:Tasa"));
        Assert.Equal(155m, cfe.CalcularTotales().MontoRetenido);
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void EResguardo_SinRetenciones_FallaLaValidacion() =>
        Assert.Contains("Detalle[0].Retenciones", TestData.Campos(TestData.CfeValido(TipoCfe.EResguardo) with { Detalle = [new LineaDetalle()] }));

    [Fact]
    public void EResguardo_ConMasDeCincoRetencionesPorLinea_Falla()
    {
        var retenciones = Enumerable.Range(0, 6).Select(i => new RetencionPercepcion { Codigo = $"X{i}", Valor = 1m }).ToList();

        Assert.Contains(TestData.Validar(TestData.CfeValido(TipoCfe.EResguardo) with { Detalle = [new LineaDetalle { Retenciones = retenciones }] }),
            e => e.Mensaje.Contains("1 a 5"));
    }

    [Fact]
    public void Retenciones_EnOtrosTipos_NoSeAdmiten() =>
        Assert.Contains("Detalle[0].Retenciones", TestData.Campos(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea() with { Retenciones = [new RetencionPercepcion { Codigo = "R1", Valor = 1m }] }],
        }));

    // ------------------------------------------------------------ cuenta ajena

    [Theory]
    [InlineData(TipoCfe.ETicketCuentaAjena)]
    [InlineData(TipoCfe.EFacturaCuentaAjena)]
    public void CuentaAjena_IncluyeComplementoFiscalConElMandante(TipoCfe tipo)
    {
        var doc = XmlDe(TestData.CfeValido(tipo));

        Assert.Equal("219999820013", Texto(doc, "//c:Compl_Fiscal/c:Compl_Fiscal_Data/c:RUCEmisor"));
        Assert.Equal("2", Texto(doc, "//c:Compl_Fiscal_Data/c:TipoDocMdte"));
        Assert.Equal("UY", Texto(doc, "//c:Compl_Fiscal_Data/c:Pais"));
        Assert.Equal("214844360018", Texto(doc, "//c:Compl_Fiscal_Data/c:DocMdte"));
        Assert.Equal("Mandante S.A.", Texto(doc, "//c:Compl_Fiscal_Data/c:NombreMdte"));
    }

    [Fact]
    public void CuentaAjena_SinMandante_FallaLaValidacion() =>
        Assert.Contains("Mandante", TestData.Campos(TestData.CfeValido(TipoCfe.ETicketCuentaAjena) with { Mandante = null }));

    [Fact]
    public void Mandante_EnUnTipoComun_FallaLaValidacion() =>
        Assert.Contains("Mandante", TestData.Campos(TestData.CfeValido() with { Mandante = new Mandante { Documento = "1", Nombre = "x" } }));

    // ------------------------------------------------------------ datos adicionales

    [Fact]
    public void DatosAdicionales_SeEscribenEnElOrdenDelEsquema()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura) with
        {
            NroInterno = "INT-77",
            FormaPago = FormaPago.Credito,
            FechaVencimiento = new DateOnly(2026, 7, 15),
            InfoAdicional = "Pedido 123",
            Emisor = TestData.Emisor() with
            {
                Telefonos = ["29001234", "099123456"],
                Correo = "ventas@empresa.com.uy",
                Sucursal = "Casa central",
                InfoAdicional = "Emisor info",
                NombreComercial = "Comercio",
                Giro = "Servicios",
            },
            Receptor = new Receptor
            {
                Documento = "214844360018", RazonSocial = "Cliente S.A.", Direccion = "Calle 1", Ciudad = "Montevideo",
                Departamento = "Montevideo", Pais = "Uruguay", CodigoPostal = "11200", InfoAdicional = "Rec info",
                LugarDestinoEntrega = "Depósito", CompraId = "OC-1",
            },
            Detalle =
            [
                TestData.Linea() with
                {
                    Descripcion = "Descripción larga",
                    Codigos = [new CodigoItem { Tipo = "EAN13", Codigo = "7730000000001" }, new CodigoItem { Tipo = "INT1", Codigo = "A-1" }],
                },
            ],
        };

        var doc = XmlDe(cfe);

        Assert.Equal("INT-77", Texto(doc, "//c:IdDoc/c:NroInterno"));
        Assert.Equal("2", Texto(doc, "//c:IdDoc/c:FmaPago"));
        Assert.Equal("2026-07-15", Texto(doc, "//c:IdDoc/c:FchVenc"));
        Assert.Equal("Pedido 123", Texto(doc, "//c:IdDoc/c:InfoAdicionalDoc"));
        Assert.Equal(2, doc.GetElementsByTagName("Telefono").Count);
        Assert.Equal("11200", Texto(doc, "//c:Receptor/c:CP"));
        Assert.Equal("EAN13", Texto(doc, "//c:Item/c:CodItem[1]/c:TpoCod"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void DescuentoPorLinea_PorPorcentaje_SeCalculaYSeInforma()
    {
        var cfe = TestData.CfeValido() with { Detalle = [TestData.Linea() with { DescuentoPorcentaje = 10m }] }; // 10 % de 200

        var doc = XmlDe(cfe);

        Assert.Equal(180m, cfe.Detalle[0].MontoTotal);
        Assert.Equal("10.000", Texto(doc, "//c:Item/c:DescuentoPct"));
        Assert.Equal("20.00", Texto(doc, "//c:Item/c:DescuentoMonto"));
        Assert.Equal("180.00", Texto(doc, "//c:Item/c:MontoItem"));
        EsValidoContraXsd(cfe);
    }

    // ------------------------------------------------------------ RUT

    [Theory]
    [InlineData("219999820013", true)]
    [InlineData("214844360018", true)]
    [InlineData("211234560019", true)]
    [InlineData("210000000012", false)]
    [InlineData("219999820014", false)]
    [InlineData("21999982001", false)]
    [InlineData("21999982001X", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Rut_EsValido(string? rut, bool esperado) => Assert.Equal(esperado, Rut.EsValido(rut));

    // ------------------------------------------------------------ QR

    private static CfeEmitido Firmado(TipoCfe tipo = TipoCfe.ETicket) =>
        TestData.Emitido(TestData.CfeValido(tipo), firma: new DateTimeOffset(2026, 6, 15, 23, 30, 0, HoraUruguay.Offset));

    [Fact]
    public void Qr_TieneElFormatoDeLaDgi()
    {
        var cfe = Firmado();

        Assert.Equal(
            $"https://www.efactura.dgi.gub.uy/consultaQR/cfe?219999820013,101,A,1,244.00,15/06/2026,{Uri.EscapeDataString(cfe.CodigoSeguridad)}",
            cfe.UrlQr);
    }

    [Fact]
    public void Qr_UsaLaFechaDeFirmaEnHoraDeUruguay()
    {
        // 23:30 en Uruguay es 02:30 UTC del día siguiente: el QR lleva la fecha uruguaya.
        var cfe = TestData.Emitido(firma: new DateTimeOffset(2026, 6, 16, 2, 30, 0, TimeSpan.Zero));
        Assert.Contains(",15/06/2026,", cfe.UrlQr);
    }

    [Theory]
    [InlineData(TipoCfe.ERemito, "0.00")]
    [InlineData(TipoCfe.EResguardo, "145.00")]
    [InlineData(TipoCfe.ERemitoExportacion, "200.00")]
    [InlineData(TipoCfe.EFacturaExportacion, "200.00")]
    public void Qr_MontoSegunElTipo(TipoCfe tipo, string monto) =>
        Assert.Contains($",{monto},", Firmado(tipo).UrlQr);

    [Fact]
    public void CodigoDeSeguridad_EsElDigestValueDeLaFirma()
    {
        var cfe = Firmado();
        Assert.Contains($"<DigestValue>{cfe.CodigoSeguridad}</DigestValue>", cfe.Xml);
    }
}
