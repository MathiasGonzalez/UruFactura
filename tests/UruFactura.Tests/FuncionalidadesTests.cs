using System.Xml;
using UruFactura.Configuration;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Models;
using UruFactura.Pdf;
using UruFactura.Xml;
using Xunit;

namespace UruFactura.Tests;

/// <summary>
/// Funcionalidades ampliadas del CFE (descuentos globales, medios de pago, retenciones, cuenta ajena,
/// contingencia, datos adicionales), validadas contra los XSD oficiales de la DGI.
/// </summary>
public class FuncionalidadesTests
{
    private const string Ns = "http://cfe.dgi.gub.uy";
    private readonly CfeXmlBuilder _builder = new();

    private static string Texto(XmlDocument doc, string xpath)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", Ns);
        return doc.SelectSingleNode(xpath, nsm)?.InnerText ?? throw new InvalidOperationException($"No existe {xpath}");
    }

    private XmlDocument Xml(Cfe cfe)
    {
        var doc = new XmlDocument();
        doc.LoadXml(_builder.Generar(cfe));
        return doc;
    }

    private static void EsValidoContraXsd(Cfe cfe)
    {
        var errores = TestData.ValidarContraXsd(CfeXmlBuilderTests.EnSobreFirmado(cfe));
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    // ------------------------------------------------------------ todos los tipos

    public static IEnumerable<object[]> TodosLosTipos() =>
        Enum.GetValues<TipoCfe>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void CadaTipoDeCfe_GeneraXmlValidoContraXsd(TipoCfe tipo) => EsValidoContraXsd(TestData.CfeValido(tipo));

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
    public void Crear_TipoDesconocido_LanzaArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => ((TipoCfe)999).Familia());

    // ------------------------------------------------------------ descuentos / recargos globales

    [Fact]
    public void DescuentoGlobal_ReduceLaBaseDelIva()
    {
        var cfe = TestData.CfeValido(); // neto 200 al 22 %
        cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Descuento, Glosa = "Promo", Monto = 50m, IndFact = TipoIva.Basico });

        var doc = Xml(cfe);

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
        var cfe = TestData.CfeValido(TipoCfe.EFactura);
        cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Recargo, Glosa = "Flete", Monto = 100m, IndFact = TipoIva.Basico, Codigo = 7 });
        cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Descuento, Glosa = "Exento", Monto = 10m, IndFact = TipoIva.Exento });
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Libro", Cantidad = 1, PrecioUnitario = 100m, IndFactIva = TipoIva.Exento });

        var doc = Xml(cfe);

        Assert.Equal("300.00", Texto(doc, "//c:Totales/c:MntNetoIVATasaBasica"));
        Assert.Equal("90.00", Texto(doc, "//c:Totales/c:MntNoGrv"));
        Assert.Equal("7", Texto(doc, "//c:DRG_Item/c:CodDR"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void DescuentoGlobal_EnExportacion_UsaIndFact10()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFacturaExportacion);
        cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Tipo = TipoMovimientoGlobal.Descuento, Glosa = "Pronto pago", Monto = 20m });

        var doc = Xml(cfe);

        Assert.Equal("10", Texto(doc, "//c:DRG_Item/c:IndFactDR"));
        Assert.Equal("180.00", Texto(doc, "//c:Totales/c:MntTotal"));
        EsValidoContraXsd(cfe);
    }

    [Theory]
    [InlineData(0, "Glosa")]
    [InlineData(-5, "Glosa")]
    [InlineData(10, "")]
    public void DescuentoGlobal_Invalido_FallaLaValidacion(double monto, string glosa)
    {
        var cfe = TestData.CfeValido();
        cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Glosa = glosa, Monto = (decimal)monto });

        Assert.NotEmpty(cfe.Validar());
    }

    [Fact]
    public void DescuentosGlobales_EnRemito_NoSeAdmiten()
    {
        var cfe = TestData.CfeValido(TipoCfe.ERemito);
        cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Glosa = "x", Monto = 1m });

        Assert.Contains(cfe.Validar(), e => e.Contains("remitos"));
    }

    // ------------------------------------------------------------ medios de pago

    [Fact]
    public void MediosDePago_SeEscribenEnOrdenYValidanContraXsd()
    {
        var cfe = TestData.CfeValido();
        cfe.MediosPago.Add(new MedioPago { Glosa = "Efectivo", Valor = 144m, Codigo = 1, Orden = 1 });
        cfe.MediosPago.Add(new MedioPago { Glosa = "Tarjeta de crédito", Valor = 100m, Orden = 2 });

        var doc = Xml(cfe);

        Assert.Equal("1", Texto(doc, "//c:MedioPago[1]/c:NroLinMP"));
        Assert.Equal("Efectivo", Texto(doc, "//c:MedioPago[1]/c:GlosaMP"));
        Assert.Equal("2", Texto(doc, "//c:MedioPago[2]/c:NroLinMP"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void MediosDePago_SinGlosa_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido();
        cfe.MediosPago.Add(new MedioPago { Glosa = " ", Valor = 1m });
        Assert.Contains(cfe.Validar(), e => e.Contains("glosa"));
    }

    // ------------------------------------------------------------ e-Resguardo

    [Fact]
    public void EResguardo_TotalizaRetencionesPorCodigo()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo);
        cfe.Detalle.Add(new LineaDetalle { Retenciones = { new RetencionPercepcion { Codigo = "R1", MontoSujeto = 100m, Valor = 10m } } });

        var doc = Xml(cfe);

        Assert.Equal("155.00", Texto(doc, "//c:Totales/c:MntTotRetenido"));
        Assert.Equal("2", Texto(doc, "//c:Totales/c:CantLinDet"));
        Assert.Equal("130.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='R1']/c:ValRetPerc"));
        Assert.Equal("25.00", Texto(doc, "//c:Totales/c:RetencPercep[c:CodRet='R2']/c:ValRetPerc"));
        Assert.Equal("12.000", Texto(doc, "//c:Item[1]/c:RetencPercep[1]/c:Tasa"));
        Assert.Equal(155m, cfe.MontoRetenido);
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void EResguardo_SinRetenciones_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo);
        cfe.Detalle[0].Retenciones.Clear();

        Assert.NotEmpty(cfe.Validar());
    }

    [Fact]
    public void EResguardo_ConMasDeCincoRetencionesPorLinea_Falla()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo);
        for (int i = 0; i < 5; i++)
            cfe.Detalle[0].Retenciones.Add(new RetencionPercepcion { Codigo = $"X{i}", Valor = 1m });

        Assert.Contains(cfe.Validar(), e => e.Contains("1 a 5"));
    }

    [Fact]
    public void Retenciones_EnOtrosTipos_NoSeAdmiten()
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle[0].Retenciones.Add(new RetencionPercepcion { Codigo = "R1", Valor = 1m });

        Assert.Contains(cfe.Validar(), e => e.Contains("e-Resguardo"));
    }

    [Fact]
    public void EResguardo_SinReceptor_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido(TipoCfe.EResguardo);
        cfe.Receptor = null;

        Assert.Contains(cfe.Validar(), e => e.Contains("receptor"));
    }

    // ------------------------------------------------------------ cuenta ajena

    [Theory]
    [InlineData(TipoCfe.ETicketCuentaAjena)]
    [InlineData(TipoCfe.EFacturaCuentaAjena)]
    public void CuentaAjena_IncluyeComplementoFiscalConElMandante(TipoCfe tipo)
    {
        var doc = Xml(TestData.CfeValido(tipo));

        Assert.Equal("219999820013", Texto(doc, "//c:Compl_Fiscal/c:Compl_Fiscal_Data/c:RUCEmisor"));
        Assert.Equal("2", Texto(doc, "//c:Compl_Fiscal_Data/c:TipoDocMdte"));
        Assert.Equal("UY", Texto(doc, "//c:Compl_Fiscal_Data/c:Pais"));
        Assert.Equal("214844360018", Texto(doc, "//c:Compl_Fiscal_Data/c:DocMdte"));
        Assert.Equal("Mandante S.A.", Texto(doc, "//c:Compl_Fiscal_Data/c:NombreMdte"));
    }

    [Fact]
    public void CuentaAjena_SinMandante_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido(TipoCfe.ETicketCuentaAjena);
        cfe.Mandante = null;

        Assert.Contains(cfe.Validar(), e => e.Contains("mandante"));
    }

    [Fact]
    public void Mandante_EnUnTipoComun_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido();
        cfe.Mandante = new Mandante { Documento = "1", Nombre = "x" };

        Assert.Contains(cfe.Validar(), e => e.Contains("cuenta ajena"));
    }

    // ------------------------------------------------------------ datos adicionales

    [Fact]
    public void DatosAdicionales_SeEscribenEnElOrdenDelEsquema()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura);
        cfe.NroInterno = "INT-77";
        cfe.FormaPago = FormaPago.Credito;
        cfe.FechaVencimiento = new DateOnly(2026, 7, 15);
        cfe.InfoAdicional = "Pedido 123";
        cfe.TelefonosEmisor = ["29001234", "099123456"];
        cfe.CorreoEmisor = "ventas@empresa.com.uy";
        cfe.SucursalEmisor = "Casa central";
        cfe.InfoAdicionalEmisor = "Emisor info";
        cfe.NombreComercialEmisor = "Comercio";
        cfe.Giro = "Servicios";
        cfe.Receptor!.Direccion = "Calle 1";
        cfe.Receptor.Ciudad = "Montevideo";
        cfe.Receptor.Departamento = "Montevideo";
        cfe.Receptor.Pais = "Uruguay";
        cfe.Receptor.CodigoPostal = "11200";
        cfe.Receptor.InfoAdicional = "Rec info";
        cfe.Receptor.LugarDestinoEntrega = "Depósito";
        cfe.Receptor.CompraId = "OC-1";
        cfe.Detalle[0].Descripcion = "Descripción larga";
        cfe.Detalle[0].Codigos.Add(new CodigoItem { Tipo = "EAN13", Codigo = "7730000000001" });
        cfe.Detalle[0].Codigos.Add(new CodigoItem { Tipo = "INT1", Codigo = "A-1" });

        var doc = Xml(cfe);

        Assert.Equal("INT-77", Texto(doc, "//c:IdDoc/c:NroInterno"));
        Assert.Equal("2", Texto(doc, "//c:IdDoc/c:FmaPago"));
        Assert.Equal("2026-07-15", Texto(doc, "//c:IdDoc/c:FchVenc"));
        Assert.Equal("Pedido 123", Texto(doc, "//c:IdDoc/c:InfoAdicionalDoc"));
        Assert.Equal("2", doc.GetElementsByTagName("Telefono").Count.ToString());
        Assert.Equal("11200", Texto(doc, "//c:Receptor/c:CP"));
        Assert.Equal("EAN13", Texto(doc, "//c:Item/c:CodItem[1]/c:TpoCod"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void DescuentoPorLinea_PorPorcentaje_SeCalculaYSeInforma()
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle[0].DescuentoPorcentaje = 10m; // 10 % de 200 = 20

        var doc = Xml(cfe);

        Assert.Equal(180m, cfe.Detalle[0].MontoTotal);
        Assert.Equal("10.000", Texto(doc, "//c:Item/c:DescuentoPct"));
        Assert.Equal("20.00", Texto(doc, "//c:Item/c:DescuentoMonto"));
        Assert.Equal("180.00", Texto(doc, "//c:Item/c:MontoItem"));
        EsValidoContraXsd(cfe);
    }

    [Fact]
    public void DescuentoPorLinea_MontoTienePrioridadSobreElPorcentaje()
    {
        var linea = new LineaDetalle { Cantidad = 1, PrecioUnitario = 100m, DescuentoMonto = 5m, DescuentoPorcentaje = 50m };
        Assert.Equal(95m, linea.MontoTotal);
    }

    [Fact]
    public void Contingencia_UsaLaEstructuraDelTipoQueSustituye()
    {
        var doc = Xml(TestData.CfeValido(TipoCfe.EFacturaContingencia));

        Assert.Equal("eFact", doc.DocumentElement!.FirstChild!.LocalName);
        Assert.Equal("211", Texto(doc, "//c:IdDoc/c:TipoCFE"));
    }

    // ------------------------------------------------------------ validaciones locales

    [Theory]
    [InlineData("219999820013", true)]
    [InlineData("214844360018", true)]
    [InlineData("210000000012", false)]
    [InlineData("219999820014", false)]
    [InlineData("21999982001", false)]
    [InlineData("21999982001X", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Rut_EsValido(string? rut, bool esperado) => Assert.Equal(esperado, Rut.EsValido(rut));

    [Fact]
    public void RutEmisorConDigitoVerificadorInvalido_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido();
        cfe.RutEmisor = "210000000012";

        Assert.Contains(cfe.Validar(), e => e.Contains("RutEmisor"));
    }

    [Fact]
    public void RutReceptorDeEFacturaInvalido_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura);
        cfe.Receptor!.Documento = "210000000012";

        Assert.Contains(cfe.Validar(), e => e.Contains("RUT del receptor"));
    }

    [Fact]
    public void EFactura_ConReceptorNoRut_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura);
        cfe.Receptor!.TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad;

        Assert.Contains(cfe.Validar(), e => e.Contains("RUT uruguayo"));
    }

    [Fact]
    public void ETicketSobreElTope_SinReceptor_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido();
        cfe.TopeMontoNetoSinReceptor = 150m; // el neto es 200

        Assert.Contains(cfe.Validar(), e => e.Contains("tope"));

        cfe.Receptor = new Receptor { Documento = "12345678", TipoDocumento = TipoDocumentoReceptor.CedulaIdentidad };
        Assert.DoesNotContain(cfe.Validar(), e => e.Contains("tope"));
    }

    [Fact]
    public void ETicketBajoElTope_NoRequiereReceptor()
    {
        var cfe = TestData.CfeValido();
        cfe.TopeMontoNetoSinReceptor = 1000m;

        Assert.Empty(cfe.Validar());
    }

    [Fact]
    public void FechaDeVencimientoAnteriorALaEmision_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido();
        cfe.FechaVencimiento = cfe.FechaEmision.AddDays(-1);

        Assert.Contains(cfe.Validar(), e => e.Contains("FechaVencimiento"));
    }

    [Theory]
    [InlineData(3)]
    public void TelefonosEmisor_MasDeDos_FallaLaValidacion(int cantidad)
    {
        var cfe = TestData.CfeValido();
        cfe.TelefonosEmisor = Enumerable.Range(1, cantidad).Select(i => $"2900000{i}").ToList();

        Assert.Contains(cfe.Validar(), e => e.Contains("TelefonosEmisor"));
    }

    [Fact]
    public void NombreComercialMuyLargo_FallaLaValidacion()
    {
        var cfe = TestData.CfeValido();
        cfe.NombreComercialEmisor = new string('x', 31);

        Assert.Contains(cfe.Validar(), e => e.Contains("NombreComercial"));
    }

    // ------------------------------------------------------------ config

    [Fact]
    public void Config_RutEmisorInvalido_LanzaExcepcion()
    {
        var config = new UruFacturaConfig
        {
            RutEmisor = "210000000012", RazonSocialEmisor = "x", DomicilioFiscal = "y", Ciudad = "M", Departamento = "M",
            RutaCertificado = "/c.p12", PasswordCertificado = "p",
        };

        Assert.Throws<UruFacturaException>(() => config.Validate());
    }

    // ------------------------------------------------------------ QR y PDF

    private static Cfe Firmado(TipoCfe tipo = TipoCfe.ETicket)
    {
        var cfe = TestData.CfeValido(tipo);
        cfe.CalcularTotales();
        cfe.FechaFirma = new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.FromHours(-3));
        cfe.CodigoSeguridad = "AbCd+/Ef12345=";
        return cfe;
    }

    [Fact]
    public void Qr_TieneElFormatoDeLaDgi()
    {
        var url = CfePdfGenerator.ConstruirContenidoQr(Firmado(), Ambiente.Produccion);

        Assert.Equal(
            "https://www.efactura.dgi.gub.uy/consultaQR/cfe?219999820013,101,A,1,244.00,15/06/2026,AbCd%2B%2FEf12345%3D",
            url);
    }

    [Theory]
    [InlineData(TipoCfe.ERemito, "0.00")]
    [InlineData(TipoCfe.EResguardo, "145.00")]
    [InlineData(TipoCfe.ERemitoExportacion, "200.00")]
    [InlineData(TipoCfe.EFacturaExportacion, "200.00")]
    public void Qr_MontoSegunElTipo(TipoCfe tipo, string monto)
    {
        var url = CfePdfGenerator.ConstruirContenidoQr(Firmado(tipo), Ambiente.Produccion);
        Assert.Contains($",{monto},", url);
    }

    [Fact]
    public void Qr_SinFirmar_UsaLaFechaDeEmisionYHashVacio()
    {
        var cfe = TestData.CfeValido();
        var url = CfePdfGenerator.ConstruirContenidoQr(cfe, Ambiente.Produccion);

        Assert.EndsWith(",15/06/2026,", url);
    }

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void Pdf_SeGeneraParaCadaTipo(TipoCfe tipo)
    {
        var config = new UruFacturaConfig
        {
            RutEmisor = "219999820013", RazonSocialEmisor = "Empresa", DomicilioFiscal = "Calle", Ciudad = "M", Departamento = "M",
            FechaIncorporacionEmisor = "01/01/2020", UrlVerificacionEmisor = "www.empresa.com.uy",
        };
        var cfe = Firmado(tipo);
        cfe.MediosPago.Add(new MedioPago { Glosa = "Efectivo", Valor = 1m });
        if (!tipo.EsRemito() && tipo.Familia() != FamiliaCfe.Resguardo)
            cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal { Glosa = "d", Monto = 1m });
        if (tipo.EsRemito() || tipo.Familia() == FamiliaCfe.Resguardo)
            cfe.MediosPago.Clear();
        var generator = new CfePdfGenerator(config);

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(generator.GenerarA4(cfe).Take(4).ToArray()));
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(generator.GenerarTermico(cfe).Take(4).ToArray()));
    }

    [Theory]
    [InlineData(TipoCfe.ETicket, "e-Ticket")]
    [InlineData(TipoCfe.NotaCreditoEFacturaExportacion, "Nota de Crédito e-Factura de Exportación")]
    [InlineData(TipoCfe.NotaDebitoETicketCuentaAjenaContingencia, "Nota de Débito e-Ticket Cuenta Ajena (Contingencia)")]
    [InlineData(TipoCfe.EResguardo, "e-Resguardo")]
    public void NombreCompleto_DelTipo(TipoCfe tipo, string esperado) =>
        Assert.Equal(esperado, CfePdfGenerator.NombreCompleto(tipo));
}
