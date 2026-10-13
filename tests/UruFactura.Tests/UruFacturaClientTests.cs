using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.Enums;
using UruFactura.Models;
using UruFactura.Pdf;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;
using Xunit;

namespace UruFactura.Tests;

/// <summary>
/// Tests para <see cref="UruFacturaClient"/>.
/// Verifica la creación de CFE, la delegación a cada dependencia,
/// el comportamiento de Dispose y las guardas post-dispose.
/// Las dependencias externas (firma, SOAP, PDF) se sustituyen por stubs
/// para evitar I/O real de red o disco.
/// </summary>
public class UruFacturaClientTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static UruFacturaConfig ConfigValida() => new()
    {
        RutEmisor = "219999820013",
        RazonSocialEmisor = "Empresa Test S.A.",
        DomicilioFiscal = "Av. 18 de Julio 1234",
        Ciudad = "Montevideo",
        Departamento = "Montevideo",
        Ambiente = Ambiente.Homologacion,
        RutaCertificado = "/tmp/fake.p12",
        PasswordCertificado = "secret",
    };

    private static UruFacturaClient CrearCliente(
        XmlBuilderStub? xmlBuilder = null,
        FirmanteStub? firmante = null,
        ICaeManager? caeManager = null,
        SoapClientStub? soapClient = null,
        ICfePdfGenerator? pdfGenerator = null) =>
        new(
            ConfigValida(),
            xmlBuilder ?? new XmlBuilderStub(),
            firmante ?? new FirmanteStub(),
            caeManager ?? new CaeManager(),
            soapClient ?? new SoapClientStub(),
            pdfGenerator);

    // -----------------------------------------------------------------------
    // Fábrica de CFE — tipo correcto
    // -----------------------------------------------------------------------

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
    [InlineData(TipoCfe.ERemitoExportacion)]
    [InlineData(TipoCfe.EResguardo)]
    [InlineData(TipoCfe.ERemito)]
    public void CrearXxx_RetornaCfeConTipoCorrecto(TipoCfe tipo)
    {
        using var client = CrearCliente();

        Cfe cfe = tipo switch
        {
            TipoCfe.ETicket                        => client.CrearETicket(),
            TipoCfe.NotaCreditoETicket             => client.CrearNotaCreditoETicket(),
            TipoCfe.NotaDebitoETicket              => client.CrearNotaDebitoETicket(),
            TipoCfe.EFactura                       => client.CrearEFactura(),
            TipoCfe.NotaCreditoEFactura            => client.CrearNotaCreditoEFactura(),
            TipoCfe.NotaDebitoEFactura             => client.CrearNotaDebitoEFactura(),
            TipoCfe.EFacturaExportacion            => client.CrearEFacturaExportacion(),
            TipoCfe.NotaCreditoEFacturaExportacion => client.CrearNotaCreditoEFacturaExportacion(),
            TipoCfe.NotaDebitoEFacturaExportacion  => client.CrearNotaDebitoEFacturaExportacion(),
            TipoCfe.ERemitoExportacion             => client.CrearERemitoExportacion(),
            TipoCfe.EResguardo                     => client.CrearEResguardo(),
            TipoCfe.ERemito                        => client.CrearERemito(),
            _ => throw new InvalidOperationException($"Tipo no manejado: {tipo}"),
        };

        Assert.Equal(tipo, cfe.Tipo);
    }

    [Fact]
    public void CrearETicket_PropagaDatosEmisorDesdeConfig()
    {
        var config = ConfigValida() with { NombreComercialEmisor = "Comercio X", Giro = "Venta de servicios" };
        using var client = new UruFacturaClient(
            config,
            new XmlBuilderStub(),
            new FirmanteStub(),
            new CaeManager(),
            new SoapClientStub(),
            null);

        var cfe = client.CrearETicket();

        Assert.Equal("219999820013", cfe.RutEmisor);
        Assert.Equal("Empresa Test S.A.", cfe.RazonSocialEmisor);
        Assert.Equal("Comercio X", cfe.NombreComercialEmisor);
        Assert.Equal("Venta de servicios", cfe.Giro);
        Assert.Equal("Av. 18 de Julio 1234", cfe.DomicilioFiscalEmisor);
        Assert.Equal("Montevideo", cfe.CiudadEmisor);
        Assert.Equal("Montevideo", cfe.DepartamentoEmisor);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), cfe.FechaEmision);
    }

    [Fact]
    public void CrearETicket_FechaEmisionEsHoy()
    {
        using var client = CrearCliente();
        var cfe = client.CrearETicket();
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), cfe.FechaEmision);
    }

    // -----------------------------------------------------------------------
    // GenerarXml
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerarXml_LlamaXmlBuilder()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        using var client = CrearCliente(xmlBuilder: xmlStub);

        client.GenerarXml(client.CrearETicket());

        Assert.True(xmlStub.LlamadoGenerar);
    }

    [Fact]
    public void GenerarXml_RetornaXmlDelBuilder()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        using var client = CrearCliente(xmlBuilder: xmlStub);

        var xml = client.GenerarXml(client.CrearETicket());

        Assert.Equal("<CFE/>", xml);
    }

    [Fact]
    public void GenerarXml_GuardaXmlSinFirmarEnCfe()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        using var client = CrearCliente(xmlBuilder: xmlStub);

        var cfe = client.CrearETicket();
        client.GenerarXml(cfe);

        Assert.Equal("<CFE/>", cfe.XmlSinFirmar);
    }

    // -----------------------------------------------------------------------
    // FirmarCfe
    // -----------------------------------------------------------------------

    [Fact]
    public void FirmarCfe_LlamaFirmante()
    {
        var firmanteStub = new FirmanteStub();
        using var client = CrearCliente(firmante: firmanteStub);

        var cfe = client.CrearETicket();
        client.GenerarXml(cfe);
        client.FirmarCfe(cfe);

        Assert.True(firmanteStub.LlamadoFirmar);
    }

    [Fact]
    public void FirmarCfe_GuardaXmlFirmadoEnCfe()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        using var client = CrearCliente(xmlBuilder: xmlStub);

        var cfe = client.CrearETicket();
        var xmlFirmado = client.FirmarCfe(cfe);

        Assert.Equal("<CFE><Firmado/></CFE>", xmlFirmado);
        Assert.Equal("<CFE><Firmado/></CFE>", cfe.XmlFirmado);
    }

    [Fact]
    public void FirmarCfe_SinXmlPrevio_GeneraXmlPrimeroLuegoFirma()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        using var client = CrearCliente(xmlBuilder: xmlStub);

        var cfe = client.CrearETicket();
        Assert.Null(cfe.XmlSinFirmar);

        client.FirmarCfe(cfe);

        Assert.True(xmlStub.LlamadoGenerar);
        Assert.NotNull(cfe.XmlSinFirmar);
    }

    // -----------------------------------------------------------------------
    // GenerarYFirmar
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerarYFirmar_GeneraYFirmaEnUnPaso()
    {
        var xmlStub = new XmlBuilderStub("<CFE/>");
        var firmanteStub = new FirmanteStub();
        using var client = CrearCliente(xmlBuilder: xmlStub, firmante: firmanteStub);

        var cfe = client.CrearETicket();
        var resultado = client.GenerarYFirmar(cfe);

        Assert.True(xmlStub.LlamadoGenerar);
        Assert.True(firmanteStub.LlamadoFirmar);
        Assert.NotEmpty(resultado);
        Assert.Equal(cfe.XmlFirmado, resultado);
    }

    // -----------------------------------------------------------------------
    // EnviarCfeAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task EnviarCfeAsync_LlamaSoapClient()
    {
        var soapStub = new SoapClientStub();
        using var client = CrearCliente(soapClient: soapStub);

        await client.EnviarCfeAsync(client.CrearETicket());

        Assert.True(soapStub.LlamadoEnviarCfe);
    }

    [Fact]
    public async Task EnviarCfeAsync_SinXmlFirmado_FirmaAutomaticamente()
    {
        var firmanteStub = new FirmanteStub();
        using var client = CrearCliente(firmante: firmanteStub);

        var cfe = client.CrearETicket();
        Assert.Null(cfe.XmlFirmado);

        await client.EnviarCfeAsync(cfe);

        Assert.True(firmanteStub.LlamadoFirmar);
        Assert.NotNull(cfe.XmlFirmado);
    }

    [Fact]
    public async Task EnviarCfeAsync_SobreRecibido_GuardaIdReceptorYTokenPeroNoMarcaAceptado()
    {
        var soapStub = new SoapClientStub(codigo: "AS", mensaje: "Sobre recibido", exitoso: true);
        using var client = CrearCliente(soapClient: soapStub);

        var cfe = client.CrearETicket();
        var respuesta = await client.EnviarCfeAsync(cfe);

        Assert.True(respuesta.Exitoso);
        Assert.Equal("AS", cfe.CodigoRespuestaDgi);
        Assert.Equal("Sobre recibido", cfe.MensajeRespuestaDgi);
        Assert.Equal("555", cfe.IdReceptorDgi);
        Assert.Equal("tok", cfe.TokenDgi);
        Assert.False(cfe.AceptadoPorDgi); // recibir el sobre no implica que el CFE fue aceptado
    }

    [Fact]
    public async Task EnviarCfeAsync_RespuestaError_GuardaCodigoEnCfe()
    {
        var soapStub = new SoapClientStub(codigo: "BS", mensaje: "Rechazado", exitoso: false);
        using var client = CrearCliente(soapClient: soapStub);

        var cfe = client.CrearETicket();
        var respuesta = await client.EnviarCfeAsync(cfe);

        Assert.False(respuesta.Exitoso);
        Assert.Equal("BS", cfe.CodigoRespuestaDgi);
        Assert.False(cfe.AceptadoPorDgi);
    }

    [Fact]
    public async Task EnviarCfeAsync_ArmaUnSobreEnvioCfeConElCfeFirmadoYElCertificado()
    {
        var soapStub = new SoapClientStub();
        using var client = CrearCliente(soapClient: soapStub);

        await client.EnviarCfeAsync(client.CrearETicket());

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(soapStub.UltimoSobre!);
        Assert.Equal("EnvioCFE", doc.DocumentElement!.LocalName);
        Assert.Equal("1", doc.GetElementsByTagName("CantCFE")[0]!.InnerText);
        Assert.Equal("219999820013", doc.GetElementsByTagName("RUCEmisor")[0]!.InnerText);
        Assert.Equal("214844360018", doc.GetElementsByTagName("RutReceptor")[0]!.InnerText);
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), doc.GetElementsByTagName("X509Certificate")[0]!.InnerText);
        Assert.Single(doc.GetElementsByTagName("Firmado"));
    }

    [Fact]
    public async Task EnviarCfesAsync_VariosCfeEnUnSoloSobre()
    {
        var soapStub = new SoapClientStub();
        using var client = CrearCliente(soapClient: soapStub);

        await client.EnviarCfesAsync([client.CrearETicket(), client.CrearETicket(), client.CrearETicket()]);

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(soapStub.UltimoSobre!);
        Assert.Equal("3", doc.GetElementsByTagName("CantCFE")[0]!.InnerText);
        Assert.Equal(3, doc.GetElementsByTagName("Firmado").Count);
    }

    [Fact]
    public async Task EnviarCfesAsync_SinCfes_LanzaUruFacturaException()
    {
        using var client = CrearCliente();
        await Assert.ThrowsAsync<UruFactura.Exceptions.UruFacturaException>(() => client.EnviarCfesAsync([]));
    }

    [Fact]
    public async Task EnviarCfeAsync_ReintentoNoVuelveAFirmar()
    {
        var firmante = new FirmanteStub();
        using var client = CrearCliente(firmante: firmante);
        var cfe = client.CrearETicket();

        await client.EnviarCfeAsync(cfe);
        await client.EnviarCfeAsync(cfe);

        Assert.Equal(1, firmante.ContadorFirmar);
    }

    // -----------------------------------------------------------------------
    // ConsultarEstadoEnvioAsync / ConsultarEstadoCfeAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConsultarEstadoEnvioAsync_ActualizaAceptadoPorDgiDeCadaCfe()
    {
        var soapStub = new SoapClientStub(codigo: "BE", exitoso: false);
        soapStub.EstadoEnvio.Add(new EstadoComprobante { TipoCfe = 101, Serie = "A", Numero = 1, Estado = "AE" });
        soapStub.EstadoEnvio.Add(new EstadoComprobante { TipoCfe = 101, Serie = "A", Numero = 2, Estado = "BE", Motivos = ["E01 Fuera de rango"] });
        using var client = CrearCliente(soapClient: soapStub);

        var ok = client.CrearETicket(); ok.Serie = "A"; ok.Numero = 1;
        var mal = client.CrearETicket(); mal.Serie = "A"; mal.Numero = 2;
        var envio = await client.EnviarCfesAsync([ok, mal]);

        await client.ConsultarEstadoEnvioAsync(envio, [ok, mal]);

        Assert.Equal("555", soapStub.UltimoIdReceptorConsultado);
        Assert.True(ok.AceptadoPorDgi);
        Assert.Equal("AE", ok.CodigoRespuestaDgi);
        Assert.False(mal.AceptadoPorDgi);
        Assert.Equal("BE", mal.CodigoRespuestaDgi);
        Assert.Contains("Fuera de rango", mal.MensajeRespuestaDgi);
    }

    [Fact]
    public async Task ConsultarEstadoEnvioAsync_SobreNoRecibido_LanzaInvalidOperationException()
    {
        using var client = CrearCliente();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConsultarEstadoEnvioAsync(RespuestaDgi.Error("BS", "rechazado")));
    }

    [Fact]
    public async Task ConsultarEstadoCfeAsync_LlamaSoapClientYGuardaEstado()
    {
        var soapStub = new SoapClientStub(codigo: "AE", mensaje: "Recibido", exitoso: true);
        using var client = CrearCliente(soapClient: soapStub);

        var cfe = client.CrearETicket();
        cfe.Serie = "A";
        cfe.Numero = 1;
        var respuesta = await client.ConsultarEstadoCfeAsync(cfe);

        Assert.True(soapStub.LlamadoConsultarEstado);
        Assert.Equal("AE", respuesta.Codigo);
        Assert.True(cfe.AceptadoPorDgi);
    }

    [Fact]
    public async Task ConsultarEstadoCfeAsync_SinSerie_LanzaInvalidOperationException()
    {
        using var client = CrearCliente();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConsultarEstadoCfeAsync(client.CrearETicket()));
    }

    // -----------------------------------------------------------------------
    // EnviarReporteDiarioAsync
    // -----------------------------------------------------------------------

    private static Cfe CfeDelDia(UruFacturaClient client, DateOnly fecha, long numero)
    {
        var cfe = client.CrearETicket();
        cfe.FechaEmision = fecha;
        cfe.Serie = "A";
        cfe.Numero = numero;
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "x", Cantidad = 1, PrecioUnitario = 100m });
        return cfe;
    }

    [Fact]
    public async Task EnviarReporteDiarioAsync_FirmaElReporteYLoEnvia()
    {
        var soapStub = new SoapClientStub();
        var firmante = new FirmanteStub();
        using var client = CrearCliente(soapClient: soapStub, firmante: firmante);
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var r = await client.EnviarReporteDiarioAsync(DateTime.Today, [CfeDelDia(client, hoy, 1), CfeDelDia(client, hoy, 2)]);

        Assert.True(soapStub.LlamadoEnviarReporte);
        Assert.Equal(1, firmante.ContadorFirmar); // se firma el reporte, no cada CFE
        Assert.Equal(2, r.CantidadCfe);

        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(soapStub.UltimoReporte!.Replace("<Reporte", "<Reporte").Replace("</Reporte>", "</Reporte>"));
        Assert.Equal("Reporte", doc.DocumentElement!.LocalName);
        Assert.Equal("2", doc.GetElementsByTagName("CantDocsUtil")[0]!.InnerText);
        Assert.Equal("1", doc.GetElementsByTagName("NroDesde")[0]!.InnerText);
        Assert.Equal("2", doc.GetElementsByTagName("NroHasta")[0]!.InnerText);
    }

    [Fact]
    public async Task EnviarReporteDiarioAsync_IgnoraCfesDeOtraFecha()
    {
        var soapStub = new SoapClientStub();
        using var client = CrearCliente(soapClient: soapStub);
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var r = await client.EnviarReporteDiarioAsync(DateTime.Today,
            [CfeDelDia(client, hoy, 1), CfeDelDia(client, hoy.AddDays(-1), 2)]);

        Assert.Equal(1, r.CantidadCfe);
    }

    [Fact]
    public async Task EnviarReporteDiarioAsync_SinCfes_EnviaReporteVacio()
    {
        var soapStub = new SoapClientStub();
        using var client = CrearCliente(soapClient: soapStub);

        var r = await client.EnviarReporteDiarioAsync(DateTime.Today, []);

        Assert.Equal(0, r.CantidadCfe);
        Assert.Contains("<CantComprobantes>0</CantComprobantes>", soapStub.UltimoReporte);
    }

    // -----------------------------------------------------------------------
    // AsignarCae
    // -----------------------------------------------------------------------

    private static Models.Cae NuevoCae(string serie = "A", long desde = 10, long hasta = 20) => new()
    {
        NroSerie = "CAE-1",
        NumeroAutorizacion = "90110001945",
        Serie = serie,
        TipoCfe = TipoCfe.ETicket,
        RangoDesde = desde,
        RangoHasta = hasta,
        FechaVencimiento = DateOnly.FromDateTime(DateTime.Today).AddYears(1),
    };

    [Fact]
    public void AsignarCae_CompletaNumeroSerieYCaeData()
    {
        using var client = CrearCliente();
        client.Cae.RegistrarCae(NuevoCae("B", 10, 20));
        var cfe = client.CrearETicket();

        client.AsignarCae(cfe);

        Assert.Equal(10, cfe.Numero);
        Assert.Equal("B", cfe.Serie);
        Assert.Equal("90110001945", cfe.CaeData!.NumeroAutorizacion);
        Assert.Equal(10, cfe.CaeData.RangoDesde);
        Assert.Equal(20, cfe.CaeData.RangoHasta);
    }

    [Fact]
    public void AsignarCae_CfesSucesivos_RecibenNumerosConsecutivos()
    {
        using var client = CrearCliente();
        client.Cae.RegistrarCae(NuevoCae());

        var a = client.CrearETicket(); client.AsignarCae(a);
        var b = client.CrearETicket(); client.AsignarCae(b);

        Assert.Equal(10, a.Numero);
        Assert.Equal(11, b.Numero);
    }

    [Fact]
    public void AsignarCae_SinCae_LanzaCaeException()
    {
        using var client = CrearCliente();
        Assert.Throws<UruFactura.Exceptions.CaeException>(() => client.AsignarCae(client.CrearETicket()));
    }

    [Fact]
    public void AsignarCae_DescartaXmlAnterior()
    {
        using var client = CrearCliente();
        client.Cae.RegistrarCae(NuevoCae());
        var cfe = client.CrearETicket();
        cfe.XmlSinFirmar = "<viejo/>";
        cfe.XmlFirmado = "<viejo/>";

        client.AsignarCae(cfe);

        Assert.Null(cfe.XmlSinFirmar);
        Assert.Null(cfe.XmlFirmado);
    }

    [Fact]
    public async Task AsignarCaeAsync_PersisteElUltimoNumeroUsadoEnElRepositorio()
    {
        var repo = new InMemoryCaeRepository();
        var cae = NuevoCae();
        await repo.GuardarCaeAsync(cae);
        using var client = new UruFacturaClient(
            ConfigValida(), new XmlBuilderStub(), new FirmanteStub(), new CaeManager(), new SoapClientStub(), null, repo);
        client.Cae.RegistrarCae(cae);

        var cfe = client.CrearETicket();
        await client.AsignarCaeAsync(cfe);

        var guardado = (await repo.CargarTodosAsync()).Single();
        Assert.Equal(10, guardado.UltimoNroUsado);
    }

    [Fact]
    public async Task CargarCaesAsync_RegistraLosDelRepositorioConSuUltimoNumero()
    {
        var repo = new InMemoryCaeRepository();
        var cae = NuevoCae();
        await repo.GuardarCaeAsync(cae);
        await repo.ActualizarUltimoNroUsadoAsync(cae.NroSerie, 14);
        using var client = new UruFacturaClient(
            ConfigValida(), new XmlBuilderStub(), new FirmanteStub(), new CaeManager(), new SoapClientStub(), null, repo);

        var cargados = await client.CargarCaesAsync();
        var cfe = client.CrearETicket();
        client.AsignarCae(cfe);

        Assert.Equal(1, cargados);
        Assert.Equal(15, cfe.Numero); // continúa donde quedó, sin repetir números
    }

    [Fact]
    public async Task CargarCaesAsync_EsIdempotente()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(NuevoCae());
        using var client = new UruFacturaClient(
            ConfigValida(), new XmlBuilderStub(), new FirmanteStub(), new CaeManager(), new SoapClientStub(), null, repo);

        await client.CargarCaesAsync();
        await client.CargarCaesAsync();

        Assert.Single(client.Cae.ObtenerTodosLosCaes());
    }

    [Fact]
    public async Task CargarCaesAsync_SinRepositorio_RetornaCero()
    {
        using var client = CrearCliente();
        Assert.Equal(0, await client.CargarCaesAsync());
    }

    [Fact]
    public async Task RegistrarCaeAsync_LoGuardaEnElRepositorio()
    {
        var repo = new InMemoryCaeRepository();
        using var client = new UruFacturaClient(
            ConfigValida(), new XmlBuilderStub(), new FirmanteStub(), new CaeManager(), new SoapClientStub(), null, repo);

        await client.RegistrarCaeAsync(NuevoCae());

        Assert.Single(client.Cae.ObtenerTodosLosCaes());
        Assert.Single(await repo.CargarTodosAsync());
    }

    [Fact]
    public void CrearETicket_PropagaElCodigoDeSucursalDeLaConfig()
    {
        using var client = new UruFacturaClient(
            ConfigValida() with { CodigoSucursal = 7 }, new XmlBuilderStub(), new FirmanteStub(),
            new CaeManager(), new SoapClientStub(), null);

        Assert.Equal(7, client.CrearETicket().CodigoSucursal);
    }

    // -----------------------------------------------------------------------
    // Extensibilidad: métodos virtuales
    // -----------------------------------------------------------------------

    private sealed class ClientePersonalizado(
        UruFacturaConfig config, ICfeXmlBuilder xml, ICfeFirmante firmante, ICaeManager cae, IDgiSoapClient soap)
        : UruFacturaClient(config, xml, firmante, cae, soap, null)
    {
        public int Generados { get; private set; }

        public override string GenerarXml(Cfe cfe)
        {
            Generados++;
            return base.GenerarXml(cfe);
        }

        protected override Cfe CrearCfe(TipoCfe tipo)
        {
            var cfe = base.CrearCfe(tipo);
            cfe.CodigoSucursal = 9; // valor por defecto propio
            return cfe;
        }
    }

    [Fact]
    public void Cliente_PermiteSobreescribirMetodosYValoresPorDefecto()
    {
        using var client = new ClientePersonalizado(
            ConfigValida(), new XmlBuilderStub(), new FirmanteStub(), new CaeManager(), new SoapClientStub());

        var cfe = client.CrearETicket();
        client.GenerarYFirmar(cfe);

        Assert.Equal(9, cfe.CodigoSucursal);
        Assert.Equal(1, client.Generados);
    }

    // -----------------------------------------------------------------------
    // GenerarPdfA4 / GenerarPdfTermico
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerarPdfA4_SinGenerador_LanzaInvalidOperationException()
    {
        using var client = CrearCliente(pdfGenerator: null);
        Assert.Throws<InvalidOperationException>(() => client.GenerarPdfA4(client.CrearETicket()));
    }

    [Fact]
    public void GenerarPdfTermico_SinGenerador_LanzaInvalidOperationException()
    {
        using var client = CrearCliente(pdfGenerator: null);
        Assert.Throws<InvalidOperationException>(() => client.GenerarPdfTermico(client.CrearETicket()));
    }

    [Fact]
    public void GenerarPdfA4_ConGenerador_LlamaGenerarA4()
    {
        var pdfStub = new PdfGeneratorStub();
        using var client = CrearCliente(pdfGenerator: pdfStub);

        var bytes = client.GenerarPdfA4(client.CrearETicket());

        Assert.True(pdfStub.LlamadoGenerarA4);
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void GenerarPdfTermico_ConGenerador_LlamaGenerarTermico()
    {
        var pdfStub = new PdfGeneratorStub();
        using var client = CrearCliente(pdfGenerator: pdfStub);

        var bytes = client.GenerarPdfTermico(client.CrearETicket());

        Assert.True(pdfStub.LlamadoGenerarTermico);
        Assert.NotEmpty(bytes);
    }

    // -----------------------------------------------------------------------
    // Dispose
    // -----------------------------------------------------------------------

    [Fact]
    public void Dispose_LlamaDisposeEnSoapClient()
    {
        var soapStub = new SoapClientStub();
        var client = CrearCliente(soapClient: soapStub);

        client.Dispose();

        Assert.True(soapStub.Disposed);
    }

    [Fact]
    public void Dispose_LlamaDisposeEnFirmante()
    {
        var firmanteStub = new FirmanteStub();
        var client = CrearCliente(firmante: firmanteStub);

        client.Dispose();

        Assert.True(firmanteStub.Disposed);
    }

    [Fact]
    public void Dispose_SegundoDispose_NoLanzaExcepcion()
    {
        var client = CrearCliente();
        client.Dispose();
        var ex = Record.Exception(() => client.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void GenerarXml_PostDispose_LanzaObjectDisposedException()
    {
        var client = CrearCliente();
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.GenerarXml(new Cfe { Tipo = TipoCfe.ETicket }));
    }

    [Fact]
    public async Task EnviarCfeAsync_PostDispose_LanzaObjectDisposedException()
    {
        var client = CrearCliente();
        client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => client.EnviarCfeAsync(new Cfe { Tipo = TipoCfe.ETicket }));
    }

    [Fact]
    public void GenerarPdfA4_PostDispose_LanzaObjectDisposedException()
    {
        var pdfStub = new PdfGeneratorStub();
        var client = CrearCliente(pdfGenerator: pdfStub);
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.GenerarPdfA4(new Cfe { Tipo = TipoCfe.ETicket }));
    }

    [Fact]
    public void GenerarPdfTermico_PostDispose_LanzaObjectDisposedException()
    {
        var pdfStub = new PdfGeneratorStub();
        var client = CrearCliente(pdfGenerator: pdfStub);
        client.Dispose();
        Assert.Throws<ObjectDisposedException>(() => client.GenerarPdfTermico(new Cfe { Tipo = TipoCfe.ETicket }));
    }

    // -----------------------------------------------------------------------
    // Stubs internos
    // -----------------------------------------------------------------------

    private sealed class XmlBuilderStub(string xml = "<CFE/>") : ICfeXmlBuilder
    {
        public bool LlamadoGenerar { get; private set; }
        public string Generar(Cfe cfe) { LlamadoGenerar = true; return xml; }
    }

    private sealed class FirmanteStub : ICfeFirmante
    {
        public bool LlamadoFirmar => ContadorFirmar > 0;
        public int ContadorFirmar { get; set; }
        public bool Disposed { get; private set; }

        public string Firmar(string xmlSinFirmar)
        {
            ContadorFirmar++;
            return xmlSinFirmar.Replace("<CFE/>", "<CFE><Firmado/></CFE>");
        }

        public byte[] ObtenerCertificadoDer() => [1, 2, 3];
        public void Dispose() { Disposed = true; }
    }

    private sealed class SoapClientStub(
        string codigo = "AS",
        string mensaje = "OK",
        bool exitoso = true) : IDgiSoapClient
    {
        public bool LlamadoEnviarCfe { get; private set; }
        public bool LlamadoConsultarEstado { get; private set; }
        public bool LlamadoEnviarReporte { get; private set; }
        public bool Disposed { get; private set; }
        public string? UltimoSobre { get; private set; }
        public string? UltimoReporte { get; private set; }
        public string? UltimoIdReceptorConsultado { get; private set; }
        public List<EstadoComprobante> EstadoEnvio { get; } = [];

        private RespuestaDgi Respuesta() => exitoso
            ? RespuestaDgi.Exito(codigo, mensaje)
            : RespuestaDgi.Error(codigo, mensaje);

        public Task<RespuestaDgi> EnviarSobreAsync(string sobreXml, CancellationToken ct = default)
        {
            LlamadoEnviarCfe = true;
            UltimoSobre = sobreXml;
            var r = Respuesta();
            r.IdReceptor = "555";
            r.Token = "tok";
            return Task.FromResult(r);
        }

        public Task<RespuestaDgi> ConsultarEstadoEnvioAsync(string idReceptor, string token, CancellationToken ct = default)
        {
            LlamadoConsultarEstado = true;
            UltimoIdReceptorConsultado = idReceptor;
            var r = Respuesta();
            r.Comprobantes = EstadoEnvio;
            return Task.FromResult(r);
        }

        public Task<RespuestaDgi> ConsultarEstadoCfeAsync(int tipo, string serie, long numero, CancellationToken ct = default)
        {
            LlamadoConsultarEstado = true;
            return Task.FromResult(Respuesta());
        }

        public Task<RespuestaDgi> EnviarReporteDiarioAsync(string reporteFirmadoXml, CancellationToken ct = default)
        {
            LlamadoEnviarReporte = true;
            UltimoReporte = reporteFirmadoXml;
            return Task.FromResult(Respuesta());
        }

        public IDgiSoapClient WithHttpClient(HttpClient httpClient) => this;
        public void Dispose() { Disposed = true; }
    }

    private sealed class PdfGeneratorStub : ICfePdfGenerator
    {
        public bool LlamadoGenerarA4 { get; private set; }
        public bool LlamadoGenerarTermico { get; private set; }
        public byte[] GenerarA4(Cfe cfe) { LlamadoGenerarA4 = true; return [1, 2, 3]; }
        public byte[] GenerarTermico(Cfe cfe) { LlamadoGenerarTermico = true; return [4, 5, 6]; }
    }
}
