using System.Xml;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Models;
using UruFactura.Xml;
using Xunit;

namespace UruFactura.Tests;

/// <summary>
/// Tests del XML de CFE. La verificación principal es contra los XSD oficiales de la DGI
/// (carpeta Schemas/dgi, versión 1.44.2): el CFE se coloca dentro de un sobre EnvioCFE y se valida.
/// </summary>
public class CfeXmlBuilderTests
{
    private const string Ns = "http://cfe.dgi.gub.uy";
    private readonly CfeXmlBuilder _builder = new();

    private static XmlDocument Cargar(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static string Texto(XmlDocument doc, string xpath)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", Ns);
        return doc.SelectSingleNode(xpath, nsm)?.InnerText ?? throw new InvalidOperationException($"No existe {xpath}");
    }

    /// <summary>
    /// Firma el CFE con un certificado de prueba y lo envuelve en un sobre para validarlo
    /// contra EnvioCFE.xsd (que exige la firma y la carátula).
    /// </summary>
    internal static string EnSobreFirmado(Cfe cfe, AlgoritmoFirma algoritmo = AlgoritmoFirma.Sha256)
    {
        using var cert = TestData.CertificadoAutofirmado();
        using var firmante = new UruFactura.Signature.CfeFirmante(cert, algoritmo);
        cfe.CalcularTotales();
        var firmado = firmante.Firmar(new CfeXmlBuilder().Generar(cfe));
        return CfeSobreBuilder.Generar(cfe.RutEmisor, 1, DateTimeOffset.UtcNow, cert.RawData, [firmado]);
    }

    [Theory]
    [InlineData(TipoCfe.ETicket)]
    [InlineData(TipoCfe.NotaCreditoETicket)]
    [InlineData(TipoCfe.NotaDebitoETicket)]
    [InlineData(TipoCfe.EFactura)]
    [InlineData(TipoCfe.NotaCreditoEFactura)]
    [InlineData(TipoCfe.NotaDebitoEFactura)]
    [InlineData(TipoCfe.EFacturaExportacion)]
    [InlineData(TipoCfe.NotaCreditoEFacturaExportacion)]
    [InlineData(TipoCfe.NotaDebitoEFacturaExportacion)]
    [InlineData(TipoCfe.ERemito)]
    [InlineData(TipoCfe.ERemitoExportacion)]
    public void Sobre_ConCfeDeCadaTipo_ValidaContraXsdOficialDeLaDgi(TipoCfe tipo)
    {
        var sobre = EnSobreFirmado(TestData.CfeValido(tipo));

        var errores = TestData.ValidarContraXsd(sobre);

        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Sobre_ConVariosTiposDeIvaYDescuentos_ValidaContraXsd()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura);
        cfe.Detalle.Clear();
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Exento", Cantidad = 1.5m, PrecioUnitario = 10.123456m, IndFactIva = TipoIva.Exento, UnidadMedida = "KG" });
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Minimo", Cantidad = 3, PrecioUnitario = 50m, IndFactIva = TipoIva.Minimo, DescuentoMonto = 5m });
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Basico", Cantidad = 1, PrecioUnitario = 200m, IndFactIva = TipoIva.Basico, RecargoMonto = 10m });
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Suspenso", Cantidad = 1, PrecioUnitario = 100m, IndFactIva = TipoIva.Suspendido });

        var errores = TestData.ValidarContraXsd(EnSobreFirmado(cfe));

        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Sobre_ETicketConReceptorExtranjeroYMonedaExtranjera_ValidaContraXsd()
    {
        var cfe = TestData.CfeValido();
        cfe.Moneda = Moneda.DolarAmericano;
        cfe.TipoCambio = 40.123m;
        cfe.Receptor = new Receptor
        {
            Documento = "AB123456", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "AR",
            RazonSocial = "Turista", Direccion = "Calle 1", Ciudad = "Buenos Aires", Pais = "Argentina",
        };

        var errores = TestData.ValidarContraXsd(EnSobreFirmado(cfe));

        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }

    [Fact]
    public void Generar_RaizEsCfeConVersion1_0_NoLaVersionDelDocumento()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido()));

        Assert.Equal("CFE", doc.DocumentElement!.LocalName);
        Assert.Equal(Ns, doc.DocumentElement.NamespaceURI);
        Assert.Equal("1.0", doc.DocumentElement.GetAttribute("version"));
        Assert.Equal("eTck", doc.DocumentElement.FirstChild!.LocalName);
    }

    [Theory]
    [InlineData(TipoCfe.ETicket, "eTck")]
    [InlineData(TipoCfe.NotaCreditoEFactura, "eFact")]
    [InlineData(TipoCfe.EFacturaExportacion, "eFact_Exp")]
    [InlineData(TipoCfe.ERemito, "eRem")]
    [InlineData(TipoCfe.ERemitoExportacion, "eRem_Exp")]
    public void Generar_ElementoPorTipo(TipoCfe tipo, string esperado)
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido(tipo)));
        Assert.Equal(esperado, doc.DocumentElement!.FirstChild!.LocalName);
    }

    [Fact]
    public void Generar_IncluyeCaeDataYTmstFirma()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido()));

        Assert.Equal("90110001945", Texto(doc, "//c:CAEData/c:CAE_ID"));
        Assert.Equal("1", Texto(doc, "//c:CAEData/c:DNro"));
        Assert.Equal("100", Texto(doc, "//c:CAEData/c:HNro"));
        Assert.Equal("2040-12-31", Texto(doc, "//c:CAEData/c:FecVenc"));
        Assert.Matches(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d-03:00$", Texto(doc, "//c:TmstFirma"));
    }

    [Fact]
    public void Generar_IdDocYEmisor_ConLosNombresDelEsquema()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido()));

        Assert.Equal("101", Texto(doc, "//c:IdDoc/c:TipoCFE"));
        Assert.Equal("A", Texto(doc, "//c:IdDoc/c:Serie"));
        Assert.Equal("1", Texto(doc, "//c:IdDoc/c:Nro"));
        Assert.Equal("2026-06-15", Texto(doc, "//c:IdDoc/c:FchEmis"));
        Assert.Equal("1", Texto(doc, "//c:IdDoc/c:FmaPago"));
        Assert.Equal("219999820013", Texto(doc, "//c:Emisor/c:RUCEmisor"));
        Assert.Equal("1", Texto(doc, "//c:Emisor/c:CdgDGISucur"));
    }

    [Fact]
    public void Generar_NoEscribeMntBruto_PorqueLosPreciosSonNetos()
    {
        var xml = _builder.Generar(TestData.CfeValido());
        Assert.DoesNotContain("MntBruto", xml);
    }

    [Fact]
    public void Generar_TotalesConIvaBasico()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido())); // 2 x 100 = 200 neto

        Assert.Equal("UYU", Texto(doc, "//c:Totales/c:TpoMoneda"));
        Assert.Equal("200.00", Texto(doc, "//c:Totales/c:MntNetoIVATasaBasica"));
        Assert.Equal("22.000", Texto(doc, "//c:Totales/c:IVATasaBasica"));
        Assert.Equal("44.00", Texto(doc, "//c:Totales/c:MntIVATasaBasica"));
        Assert.Equal("244.00", Texto(doc, "//c:Totales/c:MntTotal"));
        Assert.Equal("244.00", Texto(doc, "//c:Totales/c:MntPagar"));
        Assert.Equal("1", Texto(doc, "//c:Totales/c:CantLinDet"));
    }

    [Theory]
    [InlineData(TipoIva.Exento, "1")]
    [InlineData(TipoIva.Minimo, "2")]
    [InlineData(TipoIva.Basico, "3")]
    [InlineData(TipoIva.Suspendido, "12")]
    public void Generar_IndFactSegunTipoDeIva(TipoIva iva, string indFact)
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle[0].IndFactIva = iva;

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal(indFact, Texto(doc, "//c:Item/c:IndFact"));
    }

    [Fact]
    public void Generar_ExentoYSuspensoVanEnSusCamposDeTotales()
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Exento", Cantidad = 1, PrecioUnitario = 30m, IndFactIva = TipoIva.Exento });
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Susp", Cantidad = 1, PrecioUnitario = 70m, IndFactIva = TipoIva.Suspendido });

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal("30.00", Texto(doc, "//c:Totales/c:MntNoGrv"));
        Assert.Equal("70.00", Texto(doc, "//c:Totales/c:MntIVaenSusp"));
        Assert.Equal("344.00", Texto(doc, "//c:Totales/c:MntTotal"));
    }

    [Fact]
    public void Generar_LineaSinUnidadDeMedida_UsaNA()
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle[0].UnidadMedida = null;

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal("N/A", Texto(doc, "//c:Item/c:UniMed"));
    }

    [Fact]
    public void Generar_EFactura_ReceptorSiempreRucUruguayo()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido(TipoCfe.EFactura)));

        Assert.Equal("2", Texto(doc, "//c:Receptor/c:TipoDocRecep"));
        Assert.Equal("UY", Texto(doc, "//c:Receptor/c:CodPaisRecep"));
        Assert.Equal("210000000012", Texto(doc, "//c:Receptor/c:DocRecep"));
    }

    [Theory]
    [InlineData(TipoDocumentoReceptor.Rut, "2")]
    [InlineData(TipoDocumentoReceptor.CedulaIdentidad, "3")]
    [InlineData(TipoDocumentoReceptor.Otro, "4")]
    [InlineData(TipoDocumentoReceptor.Pasaporte, "5")]
    public void Generar_TipoDocumentoReceptor_UsaLosCodigosDeLaDgi(TipoDocumentoReceptor tipo, string codigo)
    {
        var cfe = TestData.CfeValido();
        cfe.Receptor = new Receptor { Documento = "1234567", TipoDocumento = tipo };

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal(codigo, Texto(doc, "//c:Receptor/c:TipoDocRecep"));
    }

    [Fact]
    public void Generar_TipoDocumentoReceptor_DocumentoExterior_SeMapeaComoOtro()
    {
        var cfe = TestData.CfeValido();
        cfe.Receptor = new Receptor { Documento = "1234567", TipoDocumento = TipoDocumentoReceptor.DocumentoExterior };

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal("4", Texto(doc, "//c:Receptor/c:TipoDocRecep"));
    }

    [Fact]
    public void Generar_DocumentoExtranjero_VaEnDocRecepExt()
    {
        var cfe = TestData.CfeValido();
        cfe.Receptor = new Receptor { Documento = "P99", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "BR" };

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal("BR", Texto(doc, "//c:Receptor/c:CodPaisRecep"));
        Assert.Equal("P99", Texto(doc, "//c:Receptor/c:DocRecepExt"));
    }

    [Fact]
    public void Generar_Exportacion_UsaIndFact10SinIvaYTotalesDeExportacion()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido(TipoCfe.EFacturaExportacion)));

        Assert.Equal("10", Texto(doc, "//c:Item/c:IndFact"));
        Assert.Equal("200.00", Texto(doc, "//c:Totales/c:MntExpoyAsim"));
        Assert.Equal("200.00", Texto(doc, "//c:Totales/c:MntTotal"));
        Assert.Equal("USD", Texto(doc, "//c:Totales/c:TpoMoneda"));
        Assert.Equal("40.500", Texto(doc, "//c:Totales/c:TpoCambio"));
        Assert.Equal("FOB", Texto(doc, "//c:IdDoc/c:ClauVenta"));
        Assert.Equal("1", Texto(doc, "//c:IdDoc/c:ViaTransp"));
    }

    [Theory]
    [InlineData(IndTraslado.TrasladoPorVenta, "1")]
    [InlineData(IndTraslado.TrasladoPropio, "2")]
    [InlineData(IndTraslado.Devolucion, "2")]
    public void Generar_ERemito_TipoTrasladoVentaOInterno(IndTraslado ind, string esperado)
    {
        var cfe = TestData.CfeValido(TipoCfe.ERemito);
        cfe.IndTraslado = ind;

        var doc = Cargar(_builder.Generar(cfe));

        Assert.Equal(esperado, Texto(doc, "//c:IdDoc/c:TipoTraslado"));
        Assert.Equal("1", Texto(doc, "//c:Totales/c:CantLinDet"));
    }

    [Fact]
    public void Generar_NotaDeCredito_ReferenciaConLosNombresDelEsquema()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido(TipoCfe.NotaCreditoETicket)));

        Assert.Equal("1", Texto(doc, "//c:Referencia/c:Referencia/c:NroLinRef"));
        Assert.Equal("101", Texto(doc, "//c:Referencia/c:Referencia/c:TpoDocRef"));
        Assert.Equal("A", Texto(doc, "//c:Referencia/c:Referencia/c:Serie"));
        Assert.Equal("10", Texto(doc, "//c:Referencia/c:Referencia/c:NroCFERef"));
        Assert.Equal("2026-06-01", Texto(doc, "//c:Referencia/c:Referencia/c:FechaCFEref"));
        Assert.Equal("500.00", Texto(doc, "//c:Referencia/c:Referencia/c:MntCFEref"));
        Assert.Equal("UYU", Texto(doc, "//c:Referencia/c:Referencia/c:TpoMonedaRef"));
    }

    [Fact]
    public void Generar_ElementosEnElOrdenDelEsquema()
    {
        var doc = Cargar(_builder.Generar(TestData.CfeValido(TipoCfe.NotaCreditoETicket)));

        var hijos = doc.DocumentElement!.FirstChild!.ChildNodes.Cast<XmlNode>().Select(n => n.LocalName).ToList();

        Assert.Equal(["TmstFirma", "Encabezado", "Detalle", "Referencia", "CAEData"], hijos);
    }

    [Fact]
    public void Generar_CfeInvalido_LanzaCfeValidationException()
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle.Clear();

        var ex = Assert.Throws<CfeValidationException>(() => _builder.Generar(cfe));
        Assert.Contains(ex.Errors, e => e.Contains("línea de detalle"));
    }

    [Fact]
    public void Generar_SinCae_LanzaCfeValidationException()
    {
        var cfe = TestData.CfeValido();
        cfe.CaeData = null;

        var ex = Assert.Throws<CfeValidationException>(() => _builder.Generar(cfe));
        Assert.Contains(ex.Errors, e => e.Contains("CAE"));
    }

    [Fact]
    public void Generar_NumeroFueraDelRangoDelCae_LanzaCfeValidationException()
    {
        var cfe = TestData.CfeValido(numero: 101);

        var ex = Assert.Throws<CfeValidationException>(() => _builder.Generar(cfe));
        Assert.Contains(ex.Errors, e => e.Contains("fuera del rango"));
    }

    [Fact]
    public void Generar_EResguardo_NoSoportado()
    {
        var cfe = TestData.CfeValido();
        cfe.Tipo = TipoCfe.EResguardo;

        var ex = Assert.Throws<CfeValidationException>(() => _builder.Generar(cfe));
        Assert.Contains(ex.Errors, e => e.Contains("e-Resguardo"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ABC")]
    [InlineData("0A")]
    [InlineData("A1")]
    public void Generar_SerieInvalida_LanzaCfeValidationException(string serie)
    {
        var cfe = TestData.CfeValido();
        cfe.Serie = serie;

        var ex = Assert.Throws<CfeValidationException>(() => _builder.Generar(cfe));
        Assert.Contains(ex.Errors, e => e.Contains("serie"));
    }

    [Theory]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("1A")]
    public void Generar_SerieValida_NoLanza(string serie)
    {
        var cfe = TestData.CfeValido();
        cfe.Serie = serie;

        var ex = Record.Exception(() => _builder.Generar(cfe));

        Assert.Null(ex);
    }

    [Fact]
    public void Generar_CaracteresEspecialesEnTexto_SeEscapan()
    {
        var cfe = TestData.CfeValido();
        cfe.Detalle[0].NombreItem = "Café & <té> \"caliente\"";

        var xml = _builder.Generar(cfe);
        var doc = Cargar(xml);

        Assert.Equal("Café & <té> \"caliente\"", Texto(doc, "//c:Item/c:NomItem"));
    }
}
