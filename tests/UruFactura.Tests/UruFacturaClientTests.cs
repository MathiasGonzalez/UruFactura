using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace UruFactura.Tests;

/// <summary>Flujo completo del cliente contra una DGI simulada y el almacenamiento en memoria.</summary>
public class UruFacturaClientTests
{
    private static async Task<(UruFacturaClient Client, DgiStub Dgi, InMemoryStore Store)> ClienteConCae(
        TipoCfe tipo = TipoCfe.ETicket, TimeProvider? reloj = null, UruFacturaConfig? config = null)
    {
        var dgi = new DgiStub();
        var store = new InMemoryStore();
        var client = dgi.Cliente(store, reloj, config);
        await client.RegistrarCaeAsync(TestData.Cae(tipo));
        return (client, dgi, store);
    }

    // -----------------------------------------------------------------------
    // Construcción
    // -----------------------------------------------------------------------

    [Fact]
    public void Constructor_ConfigInvalida_LanzaUruFacturaException()
    {
        var ex = Assert.Throws<UruFacturaException>(() => new UruFacturaClient(TestData.Config() with { Emisor = new Emisor() }));
        Assert.Contains("Emisor.Rut", ex.Message);
    }

    [Fact]
    public void Constructor_SinCertificado_LanzaUruFacturaException() =>
        Assert.Throws<UruFacturaException>(() => new UruFacturaClient(TestData.Config() with { Certificado = null }));

    [Fact]
    public void Constructor_CertificadoInexistente_LanzaFirmaDigitalException() =>
        Assert.Throws<FirmaDigitalException>(() =>
            new UruFacturaClient(TestData.Config() with { Certificado = null, RutaCertificado = "/no/existe.p12" }));

    [Fact]
    public void Constructor_CertificadoSinClavePrivada_LanzaFirmaDigitalException()
    {
        using var soloPublico = X509CertificateLoader.LoadCertificate(TestData.Certificado.RawData);
        Assert.Throws<FirmaDigitalException>(() => new UruFacturaClient(TestData.Config() with { Certificado = soloPublico }));
    }

    [Fact]
    public void Constructor_HttpClientYDgiClient_LanzaArgumentException()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new UruFacturaClient(TestData.Config(), httpClient: http, dgiClient: new DgiStub()));
    }

    [Fact]
    public void Constructor_HttpClientConTimeoutInfinito_LanzaArgumentException()
    {
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        Assert.Throws<ArgumentException>(() => new UruFacturaClient(TestData.Config(), httpClient: http));
    }

    [Fact]
    public void Constructor_PorDefecto_UsaElTransporteSoap()
    {
        using var client = new UruFacturaClient(TestData.Config());
        Assert.NotNull(client);
    }

    // -----------------------------------------------------------------------
    // CAE
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RegistrarCae_Invalido_LanzaCaeException()
    {
        using var client = new DgiStub().Cliente();
        var ex = await Assert.ThrowsAsync<CaeException>(() => client.RegistrarCaeAsync(TestData.Cae(serie: "a1")));
        Assert.Contains("serie", ex.Message);
    }

    [Fact]
    public async Task RegistrarCae_YObtenerCaes()
    {
        using var client = new DgiStub().Cliente();
        await client.RegistrarCaeAsync(TestData.Cae());
        await client.RegistrarCaeAsync(TestData.Cae(TipoCfe.EFactura));

        Assert.Equal(2, (await client.ObtenerCaesAsync()).Count);
    }

    [Fact]
    public async Task ObtenerAdvertenciasCae_UsaLaFechaDelReloj()
    {
        var reloj = new RelojFijo(new DateTimeOffset(2040, 12, 29, 12, 0, 0, TimeSpan.Zero));
        var (client, _, _) = await ClienteConCae(reloj: reloj); // el CAE vence el 31/12/2040
        using var _ = client;

        Assert.Contains(await client.ObtenerAdvertenciasCaeAsync(), a => a.Contains("vence en 2"));
    }

    // -----------------------------------------------------------------------
    // Emisión
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Emitir_AsignaNumeroSerieYCae_FirmaYRegistra()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;

        var emitido = await client.EmitirAsync(TestData.CfeValido());

        Assert.Equal(1, emitido.Numero);
        Assert.Equal("A", emitido.Serie);
        Assert.Equal("90110001945", emitido.Cae.NumeroAutorizacion);
        Assert.Equal(244m, emitido.Totales.MontoTotal);
        Assert.Equal(TestData.Emisor(), emitido.Emisor);
        Assert.True(FirmaDigital.Verificar(emitido.Xml));
        Assert.Contains($"<DigestValue>{emitido.CodigoSeguridad}</DigestValue>", emitido.Xml);
        Assert.False(emitido.Enviado);
        Assert.Same(emitido, Assert.Single(await store.ListarCfesAsync(TestData.Dia)));
        Assert.Equal(1, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task Emitir_CfesSucesivos_RecibenNumerosConsecutivos()
    {
        var (client, _, _) = await ClienteConCae();
        using var _ = client;

        var a = await client.EmitirAsync(TestData.CfeValido());
        var b = await client.EmitirAsync(TestData.CfeValido());

        Assert.Equal((1L, 2L), (a.Numero, b.Numero));
    }

    [Fact]
    public async Task Emitir_SinCae_LanzaCaeException()
    {
        using var client = new DgiStub().Cliente();
        await Assert.ThrowsAsync<CaeException>(() => client.EmitirAsync(TestData.CfeValido()));
    }

    [Fact]
    public async Task Emitir_CfeInvalido_NoConsumeNumero()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;

        var ex = await Assert.ThrowsAsync<CfeValidationException>(() => client.EmitirAsync(new Cfe(TipoCfe.ETicket)));

        Assert.Contains(ex.Errors, e => e.Contains("línea de detalle"));
        Assert.Equal(0, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task Emitir_SinFechaDeEmision_UsaLaFechaYHoraDeUruguayDelReloj()
    {
        // 23:30 en Montevideo = 02:30 UTC del día siguiente.
        var reloj = new RelojFijo(new DateTimeOffset(2026, 6, 16, 2, 30, 15, 500, TimeSpan.Zero));
        var (client, _, _) = await ClienteConCae(reloj: reloj);
        using var _ = client;

        var emitido = await client.EmitirAsync(TestData.CfeValido() with { FechaEmision = null });

        Assert.Equal(new DateOnly(2026, 6, 15), emitido.FechaEmision);
        Assert.Equal(new DateTimeOffset(2026, 6, 15, 23, 30, 15, HoraUruguay.Offset), emitido.FechaFirma);
        Assert.Contains("<TmstFirma>2026-06-15T23:30:15-03:00</TmstFirma>", emitido.Xml);
    }

    [Fact]
    public async Task Emitir_ConEmisorPropio_LoUsaEnElComprobante()
    {
        var (client, _, _) = await ClienteConCae();
        using var _ = client;

        var emitido = await client.EmitirAsync(TestData.CfeValido() with { Emisor = TestData.Emisor() with { CodigoSucursal = 4 } });

        Assert.Equal(4, emitido.Emisor.CodigoSucursal);
        Assert.Contains("<CdgDGISucur>4</CdgDGISucur>", emitido.Xml);
    }

    [Fact]
    public async Task Validar_EmisorPropioConOtroRut_TieneError()
    {
        using var client = new DgiStub().Cliente();
        var errores = client.Validar(TestData.CfeValido() with { Emisor = TestData.Emisor() with { Rut = "214844360018" } });
        Assert.Contains(errores, e => e.Contains("RUT de la configuración"));
    }

    [Fact]
    public void Validar_ConValorUnidadIndexada_AplicaElTopeDe5000UI()
    {
        using var client = new DgiStub().Cliente(config: TestData.Config() with { ValorUnidadIndexada = 0.01m }); // tope: 50 pesos

        Assert.Contains(client.Validar(TestData.CfeValido()), e => e.Contains("tope"));
    }

    [Fact]
    public void Validar_CfeValido_NoTieneErrores()
    {
        using var client = new DgiStub().Cliente();
        Assert.Empty(client.Validar(TestData.CfeValido()));
    }

    // -----------------------------------------------------------------------
    // Envío y consulta
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Enviar_ArmaUnSobreValidoConElCfeFirmadoYElCertificado()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        await client.EnviarAsync(emitido);

        TestData.AssertValidoContraXsd(dgi.UltimoSobre!);
        var doc = new XmlDocument();
        doc.LoadXml(dgi.UltimoSobre!);
        Assert.Equal("1", doc.GetElementsByTagName("CantCFE")[0]!.InnerText);
        Assert.Equal("219999820013", doc.GetElementsByTagName("RUCEmisor")[0]!.InnerText);
        Assert.Equal(Convert.ToBase64String(TestData.Certificado.RawData), doc.GetElementsByTagName("X509Certificate")[0]!.InnerText);
    }

    [Fact]
    public async Task Enviar_SobreRecibido_GuardaIdReceptorYTokenPeroNoLoDaPorAceptado()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        var acuse = await client.EnviarAsync(emitido);

        Assert.True(acuse.Recibido);
        Assert.Same(emitido, Assert.Single(acuse.Comprobantes));
        Assert.True(emitido.Enviado);
        Assert.Equal("tok", emitido.TokenDgi);
        Assert.False(emitido.AceptadoPorDgi);
        Assert.Equal("tok", (await store.ListarCfesAsync(TestData.Dia)).Single().TokenDgi);
    }

    [Fact]
    public async Task Enviar_SobreRechazado_NoLoMarcaEnviado()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        dgi.EstadoSobre = "BS";
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        var acuse = await client.EnviarAsync(emitido);

        Assert.False(acuse.Recibido);
        Assert.False(emitido.Enviado);
        Assert.Equal("Rechazado", emitido.MensajeDgi);
    }

    [Fact]
    public async Task Enviar_VariosCfeEnUnSoloSobre()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var cfes = new[] { await client.EmitirAsync(TestData.CfeValido()), await client.EmitirAsync(TestData.CfeValido()) };

        await client.EnviarAsync(cfes);

        Assert.Contains("<CantCFE>2</CantCFE>", dgi.UltimoSobre);
        Assert.Single(dgi.Sobres);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(251)]
    public async Task Enviar_CantidadFueraDeRango_LanzaUruFacturaException(int cantidad)
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        await Assert.ThrowsAsync<UruFacturaException>(() => client.EnviarAsync(Enumerable.Repeat(emitido, cantidad).ToList()));
        Assert.Empty(dgi.Sobres);
    }

    [Fact]
    public async Task ConsultarEnvio_ActualizaElEstadoDeCadaCfe_YLoGuarda()
    {
        var (client, dgi, store) = await ClienteConCae();
        using var _ = client;
        var ok = await client.EmitirAsync(TestData.CfeValido());
        var mal = await client.EmitirAsync(TestData.CfeValido());
        dgi.EstadoPorNumero[mal.Numero] = "BE";
        var acuse = await client.EnviarAsync([ok, mal]);

        var estado = await client.ConsultarEnvioAsync(acuse);

        Assert.True(estado.Procesado);
        Assert.False(estado.TodosAceptados);
        Assert.True(ok.AceptadoPorDgi);
        Assert.Equal("BE", mal.EstadoDgi);
        Assert.Contains("Fuera de rango", mal.MensajeDgi);
        Assert.Equal(["AE", "BE"], (await store.ListarCfesAsync(TestData.Dia)).Select(c => c.EstadoDgi));
    }

    [Fact]
    public async Task ConsultarEnvio_SinProcesar_NoCambiaElEstado()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());
        var acuse = await client.EnviarAsync(emitido);
        dgi.SinProcesar = true;

        var estado = await client.ConsultarEnvioAsync(acuse);

        Assert.False(estado.Procesado);
        Assert.Null(emitido.EstadoDgi);
    }

    [Fact]
    public async Task ConsultarEnvio_SobreNoRecibido_LanzaInvalidOperationException()
    {
        using var client = new DgiStub().Cliente();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.ConsultarEnvioAsync(new AcuseSobre { Estado = "BS" }));
    }

    [Fact]
    public async Task ConsultarCfe_ActualizaElEstado()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());
        dgi.EstadoPorNumero[emitido.Numero] = "CE";

        var estado = await client.ConsultarCfeAsync(emitido);

        Assert.Equal("CE", estado.Estado);
        Assert.Equal("CE", emitido.EstadoDgi);
    }

    // -----------------------------------------------------------------------
    // Reporte diario
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReporteDiario_ResumeLosCfeDelAlmacenamiento_FirmadoYValido()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        await client.EmitirAsync(TestData.CfeValido());
        await client.EmitirAsync(TestData.CfeValido());
        await client.EmitirAsync(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(-1) });

        var acuse = await client.EnviarReporteDiarioAsync(TestData.Dia);

        Assert.True(acuse.Recibido);
        Assert.Equal(2, acuse.CantidadCfe);
        Assert.Equal(TestData.Dia, acuse.Fecha);
        TestData.AssertValidoContraXsd(dgi.UltimoReporte!);
        Assert.True(FirmaDigital.Verificar(dgi.UltimoReporte!));
        Assert.Contains("<NroDesde>1</NroDesde><NroHasta>2</NroHasta>", dgi.UltimoReporte);
    }

    [Fact]
    public async Task ReporteDiario_SinCfes_EnviaReporteVacio()
    {
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        var acuse = await client.EnviarReporteDiarioAsync(TestData.Dia);

        Assert.Equal(0, acuse.CantidadCfe);
        Assert.Contains("<CantComprobantes>0</CantComprobantes>", dgi.UltimoReporte);
    }

    [Fact]
    public async Task ReporteDiario_InformaLaSecuenciaDelEnvio()
    {
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        await client.EnviarReporteDiarioAsync(TestData.Dia, secEnvio: 2);

        Assert.Contains("<SecEnvio>2</SecEnvio>", dgi.UltimoReporte);
    }

    [Fact]
    public async Task NumerosDeEnvio_SonUnicosEntreSobresYReportes()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        await client.EnviarAsync(emitido);
        await client.EnviarAsync(emitido);
        await client.EnviarReporteDiarioAsync(TestData.Dia);

        Assert.Equal(3, dgi.IdsEnvio.Distinct().Count());
    }

    // -----------------------------------------------------------------------
    // Dispose
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PostDispose_LanzaObjectDisposedException()
    {
        var client = new DgiStub().Cliente();
        client.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.EmitirAsync(TestData.CfeValido()));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.EnviarReporteDiarioAsync(TestData.Dia));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.RegistrarCaeAsync(TestData.Cae()));
    }

    [Fact]
    public void Dispose_DosVeces_NoLanza_YNoLiberaElCertificadoDelLlamador()
    {
        var client = new UruFacturaClient(TestData.Config());
        client.Dispose();
        client.Dispose();

        Assert.True(TestData.Certificado.HasPrivateKey);
        Assert.NotNull(TestData.Certificado.GetRSAPrivateKey());
    }
}
