using System.Xml;

namespace UruFactura.Tests;

/// <summary>
/// Tests del XML de CFE. La verificación principal es contra los XSD oficiales de la DGI
/// (carpeta Schemas/dgi, versión 1.44.2): el CFE firmado se coloca dentro de un sobre EnvioCFE y se valida.
/// </summary>
public class CfeXmlTests
{
    private const string Ns = "http://cfe.dgi.gub.uy";

    internal static XmlDocument XmlDe(Cfe cfe, long numero = 1)
    {
        var doc = new XmlDocument();
        doc.LoadXml(TestData.Emitido(cfe, numero).Xml);
        return doc;
    }

    internal static string Texto(XmlDocument doc, string xpath)
    {
        var nsm = new XmlNamespaceManager(doc.NameTable);
        nsm.AddNamespace("c", Ns);
        return doc.SelectSingleNode(xpath, nsm)?.InnerText ?? throw new InvalidOperationException($"No existe {xpath}");
    }

    public static IEnumerable<object[]> TodosLosTipos() => Enum.GetValues<TipoCfe>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(TodosLosTipos))]
    public void Sobre_ConCfeDeCadaTipo_ValidaContraXsdOficialDeLaDgi(TipoCfe tipo) =>
        TestData.AssertValidoContraXsd(TestData.Sobre(TestData.Emitido(TestData.CfeValido(tipo))));

    [Theory]
    [InlineData(AlgoritmoFirma.Sha256)]
    [InlineData(AlgoritmoFirma.Sha1)]
    public void Sobre_ConCfeFirmadoConCadaAlgoritmo_ValidaContraXsd(AlgoritmoFirma algoritmo)
    {
        var cfe = TestData.CfeValido() with { Emisor = TestData.Emisor() };
        var sinFirmar = UruFactura.Xml.CfeXml.Generar(cfe, TestData.Cae(), 1, cfe.CalcularTotales(), DateTimeOffset.UtcNow);
        var (xml, _) = FirmaDigital.Firmar(sinFirmar, TestData.Certificado, algoritmo);

        TestData.AssertValidoContraXsd(UruFactura.Xml.CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, TestData.Certificado.RawData, [xml]));
    }

    [Fact]
    public void Sobre_ConVariosTiposDeIvaYDescuentos_ValidaContraXsd()
    {
        var cfe = TestData.CfeValido(TipoCfe.EFactura) with
        {
            Detalle =
            [
                new LineaDetalle { NombreItem = "Exento", Cantidad = 1.5m, PrecioUnitario = 10.123456m, IndFactIva = TipoIva.Exento, UnidadMedida = "KG" },
                new LineaDetalle { NombreItem = "Minimo", Cantidad = 3, PrecioUnitario = 50m, IndFactIva = TipoIva.Minimo, DescuentoMonto = 5m },
                new LineaDetalle { NombreItem = "Basico", Cantidad = 1, PrecioUnitario = 200m, IndFactIva = TipoIva.Basico, RecargoMonto = 10m },
                new LineaDetalle { NombreItem = "Suspenso", Cantidad = 1, PrecioUnitario = 100m, IndFactIva = TipoIva.Suspendido },
            ],
        };

        TestData.AssertValidoContraXsd(TestData.Sobre(TestData.Emitido(cfe)));
    }

    [Fact]
    public void Sobre_ETicketConReceptorExtranjeroYMonedaExtranjera_ValidaContraXsd()
    {
        var cfe = TestData.CfeValido() with
        {
            Moneda = Moneda.DolarAmericano,
            TipoCambio = 40.123m,
            Receptor = new Receptor
            {
                Documento = "AB123456", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "AR",
                RazonSocial = "Turista", Direccion = "Calle 1", Ciudad = "Buenos Aires", Pais = "Argentina",
            },
        };

        TestData.AssertValidoContraXsd(TestData.Sobre(TestData.Emitido(cfe)));
    }

    [Fact]
    public void Raiz_EsCfeConVersion1_0_YLaFirmaComoUltimoHijo()
    {
        var doc = XmlDe(TestData.CfeValido());

        Assert.Equal("CFE", doc.DocumentElement!.LocalName);
        Assert.Equal(Ns, doc.DocumentElement.NamespaceURI);
        Assert.Equal("1.0", doc.DocumentElement.GetAttribute("version"));
        Assert.Equal("eTck", doc.DocumentElement.FirstChild!.LocalName);
        Assert.Equal("Signature", doc.DocumentElement.LastChild!.LocalName);
    }

    [Theory]
    [InlineData(TipoCfe.ETicket, "eTck")]
    [InlineData(TipoCfe.NotaCreditoEFactura, "eFact")]
    [InlineData(TipoCfe.EFacturaExportacion, "eFact_Exp")]
    [InlineData(TipoCfe.ERemito, "eRem")]
    [InlineData(TipoCfe.ERemitoExportacion, "eRem_Exp")]
    [InlineData(TipoCfe.EResguardo, "eResg")]
    [InlineData(TipoCfe.EFacturaContingencia, "eFact")]
    public void ElementoPorTipo(TipoCfe tipo, string esperado) =>
        Assert.Equal(esperado, XmlDe(TestData.CfeValido(tipo)).DocumentElement!.FirstChild!.LocalName);

    [Fact]
    public void IncluyeCaeDataYTmstFirma()
    {
        var doc = XmlDe(TestData.CfeValido());

        Assert.Equal("90110001945", Texto(doc, "//c:CAEData/c:CAE_ID"));
        Assert.Equal("1", Texto(doc, "//c:CAEData/c:DNro"));
        Assert.Equal("100", Texto(doc, "//c:CAEData/c:HNro"));
        Assert.Equal("2040-12-31", Texto(doc, "//c:CAEData/c:FecVenc"));
        Assert.Equal("2026-06-15T12:00:00-03:00", Texto(doc, "//c:TmstFirma"));
    }

    [Fact]
    public void IdDocYEmisor_ConLosNombresDelEsquema()
    {
        var doc = XmlDe(TestData.CfeValido(), numero: 7);

        Assert.Equal("101", Texto(doc, "//c:IdDoc/c:TipoCFE"));
        Assert.Equal("A", Texto(doc, "//c:IdDoc/c:Serie"));
        Assert.Equal("7", Texto(doc, "//c:IdDoc/c:Nro"));
        Assert.Equal("2026-06-15", Texto(doc, "//c:IdDoc/c:FchEmis"));
        Assert.Equal("1", Texto(doc, "//c:IdDoc/c:FmaPago"));
        Assert.Equal("219999820013", Texto(doc, "//c:Emisor/c:RUCEmisor"));
        Assert.Equal("1", Texto(doc, "//c:Emisor/c:CdgDGISucur"));
    }

    [Fact]
    public void NoEscribeMntBruto_PorqueLosPreciosSonNetos() =>
        Assert.DoesNotContain("MntBruto", TestData.Emitido().Xml);

    [Fact]
    public void TotalesConIvaBasico()
    {
        var doc = XmlDe(TestData.CfeValido()); // 2 x 100 = 200 neto

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
    public void IndFactSegunTipoDeIva(TipoIva iva, string indFact) =>
        Assert.Equal(indFact, Texto(XmlDe(TestData.CfeValido() with { Detalle = [TestData.Linea(iva: iva)] }), "//c:Item/c:IndFact"));

    [Fact]
    public void ExentoYSuspensoVanEnSusCamposDeTotales()
    {
        var doc = XmlDe(TestData.CfeValido() with
        {
            Detalle = [TestData.Linea(), TestData.Linea(1, 30m, TipoIva.Exento), TestData.Linea(1, 70m, TipoIva.Suspendido)],
        });

        Assert.Equal("30.00", Texto(doc, "//c:Totales/c:MntNoGrv"));
        Assert.Equal("70.00", Texto(doc, "//c:Totales/c:MntIVaenSusp"));
        Assert.Equal("344.00", Texto(doc, "//c:Totales/c:MntTotal"));
    }

    [Fact]
    public void LineaSinUnidadDeMedida_UsaNA() =>
        Assert.Equal("N/A", Texto(XmlDe(TestData.CfeValido() with { Detalle = [TestData.Linea() with { UnidadMedida = null }] }), "//c:Item/c:UniMed"));

    [Fact]
    public void LineasSeNumeranPorPosicion()
    {
        var doc = XmlDe(TestData.CfeValido() with { Detalle = [TestData.Linea(), TestData.Linea()] });
        Assert.Equal("2", Texto(doc, "//c:Item[2]/c:NroLinDet"));
    }

    [Fact]
    public void EFactura_ReceptorSiempreRucUruguayo()
    {
        var doc = XmlDe(TestData.CfeValido(TipoCfe.EFactura));

        Assert.Equal("2", Texto(doc, "//c:Receptor/c:TipoDocRecep"));
        Assert.Equal("UY", Texto(doc, "//c:Receptor/c:CodPaisRecep"));
        Assert.Equal("214844360018", Texto(doc, "//c:Receptor/c:DocRecep"));
    }

    [Theory]
    [InlineData(TipoDocumentoReceptor.Rut, "2")]
    [InlineData(TipoDocumentoReceptor.CedulaIdentidad, "3")]
    [InlineData(TipoDocumentoReceptor.Otro, "4")]
    [InlineData(TipoDocumentoReceptor.Pasaporte, "5")]
    [InlineData(TipoDocumentoReceptor.Dni, "6")]
    [InlineData(TipoDocumentoReceptor.Nife, "7")]
    public void TipoDocumentoReceptor_UsaLosCodigosDeLaDgi(TipoDocumentoReceptor tipo, string codigo)
    {
        var cfe = TestData.CfeValido() with
        {
            Receptor = new Receptor { Documento = tipo == TipoDocumentoReceptor.Rut ? "214844360018" : "1234567", TipoDocumento = tipo },
        };

        Assert.Equal(codigo, Texto(XmlDe(cfe), "//c:Receptor/c:TipoDocRecep"));
    }

    [Fact]
    public void DocumentoExtranjero_VaEnDocRecepExt()
    {
        var doc = XmlDe(TestData.CfeValido() with
        {
            Receptor = new Receptor { Documento = "P99", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "BR" },
        });

        Assert.Equal("BR", Texto(doc, "//c:Receptor/c:CodPaisRecep"));
        Assert.Equal("P99", Texto(doc, "//c:Receptor/c:DocRecepExt"));
    }

    [Fact]
    public void Exportacion_UsaIndFact10SinIvaYTotalesDeExportacion()
    {
        var doc = XmlDe(TestData.CfeValido(TipoCfe.EFacturaExportacion));

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
    public void ERemito_TipoTrasladoVentaOInterno(IndTraslado ind, string esperado)
    {
        var doc = XmlDe(TestData.CfeValido(TipoCfe.ERemito) with { IndTraslado = ind });

        Assert.Equal(esperado, Texto(doc, "//c:IdDoc/c:TipoTraslado"));
        Assert.Equal("1", Texto(doc, "//c:Totales/c:CantLinDet"));
    }

    [Fact]
    public void NotaDeCredito_ReferenciaConLosNombresDelEsquema()
    {
        var doc = XmlDe(TestData.CfeValido(TipoCfe.NotaCreditoETicket));

        Assert.Equal("1", Texto(doc, "//c:Referencia/c:Referencia/c:NroLinRef"));
        Assert.Equal("101", Texto(doc, "//c:Referencia/c:Referencia/c:TpoDocRef"));
        Assert.Equal("A", Texto(doc, "//c:Referencia/c:Referencia/c:Serie"));
        Assert.Equal("10", Texto(doc, "//c:Referencia/c:Referencia/c:NroCFERef"));
        Assert.Equal("2026-06-01", Texto(doc, "//c:Referencia/c:Referencia/c:FechaCFEref"));
        Assert.Equal("500.00", Texto(doc, "//c:Referencia/c:Referencia/c:MntCFEref"));
        Assert.Equal("UYU", Texto(doc, "//c:Referencia/c:Referencia/c:TpoMonedaRef"));
    }

    [Fact]
    public void ElementosEnElOrdenDelEsquema()
    {
        var doc = XmlDe(TestData.CfeValido(TipoCfe.NotaCreditoETicket));

        var hijos = doc.DocumentElement!.FirstChild!.ChildNodes.Cast<XmlNode>().Select(n => n.LocalName).ToList();

        Assert.Equal(["TmstFirma", "Encabezado", "Detalle", "Referencia", "CAEData"], hijos);
    }

    [Fact]
    public void CaracteresEspecialesEnTexto_SeEscapan()
    {
        var doc = XmlDe(TestData.CfeValido() with { Detalle = [TestData.Linea() with { NombreItem = "Café & <té> \"caliente\"" }] });

        Assert.Equal("Café & <té> \"caliente\"", Texto(doc, "//c:Item/c:NomItem"));
    }

    [Fact]
    public void EmisorDelDocumento_ReemplazaAlDeLaConfiguracion()
    {
        var cfe = TestData.CfeValido() with { Emisor = TestData.Emisor() with { CodigoSucursal = 3, Sucursal = "Pocitos" } };

        var doc = XmlDe(cfe);

        Assert.Equal("3", Texto(doc, "//c:Emisor/c:CdgDGISucur"));
        Assert.Equal("Pocitos", Texto(doc, "//c:Emisor/c:EmiSucursal"));
    }
}
