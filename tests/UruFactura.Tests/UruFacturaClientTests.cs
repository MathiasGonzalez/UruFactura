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
        var store = new InMemoryStore(reloj ?? TestData.Reloj);
        var client = dgi.Cliente(store, reloj, config);
        await client.RegistrarCaeAsync(TestData.Cae(tipo));
        return (client, dgi, store);
    }

    private static async Task<List<CfeEmitido>> Listar(IUruFacturaStore store, DateOnly? fecha = null) =>
        await store.ListarCfesAsync(fecha ?? TestData.Dia).ToListAsync();

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

    private static UruFacturaConfig Produccion() =>
        TestData.Config() with { Ambiente = Ambiente.Produccion, UrlEnvio = "https://e", UrlConsultas = "https://c" };

    [Fact]
    public void Constructor_ProduccionSinAlmacenamiento_LanzaUruFacturaException()
    {
        // Con el almacenamiento en memoria por defecto, un reinicio repetiría números.
        var ex = Assert.Throws<UruFacturaException>(() => new UruFacturaClient(Produccion(), dgiClient: new DgiStub()));
        Assert.Contains("almacenamiento", ex.Message);
    }

    [Fact]
    public void Constructor_ProduccionConAlmacenamientoExplicito_SeCrea()
    {
        using var client = new UruFacturaClient(Produccion(), new InMemoryStore(), dgiClient: new DgiStub());
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
    public async Task RegistrarCae_RangoSuperpuestoConOtroDeLaMismaSerie_LanzaCaeException()
    {
        using var client = new DgiStub().Cliente();
        await client.RegistrarCaeAsync(TestData.Cae(desde: 1, hasta: 10));

        // Un error de tipeo en RangoDesde emitiría dos veces los números 5 a 10.
        var ex = await Assert.ThrowsAsync<CaeException>(() => client.RegistrarCaeAsync(TestData.Cae(desde: 5, hasta: 20)));

        Assert.Contains("se superpone", ex.Message);
        Assert.Single(await client.ObtenerCaesAsync());
    }

    [Fact]
    public async Task RegistrarCae_MismoRangoEnOtraSerieOTipo_NoSeSuperpone()
    {
        using var client = new DgiStub().Cliente();
        await client.RegistrarCaeAsync(TestData.Cae(desde: 1, hasta: 10));

        await client.RegistrarCaeAsync(TestData.Cae(serie: "B", desde: 1, hasta: 10));
        await client.RegistrarCaeAsync(TestData.Cae(TipoCfe.EFactura, desde: 1, hasta: 10));
        // El mismo CAE (tipo, serie y comienzo de rango) se actualiza.
        await client.RegistrarCaeAsync(TestData.Cae(desde: 1, hasta: 20));

        Assert.Equal(3, (await client.ObtenerCaesAsync()).Count);
    }

    [Fact]
    public async Task EliminarCae_SinUsar_LoElimina()
    {
        using var client = new DgiStub().Cliente();
        await client.RegistrarCaeAsync(TestData.Cae(desde: 101, hasta: 200));

        await client.EliminarCaeAsync(TestData.Cae(desde: 101, hasta: 200));

        Assert.Empty(await client.ObtenerCaesAsync());
    }

    [Fact]
    public async Task EliminarCae_ConNumerosUsados_LanzaCaeException()
    {
        var (client, _, _) = await ClienteConCae();
        using var _ = client;
        await client.EmitirAsync(TestData.CfeValido());

        await Assert.ThrowsAsync<CaeException>(() => client.EliminarCaeAsync(TestData.Cae()));
        Assert.Single(await client.ObtenerCaesAsync());
    }

    [Fact]
    public async Task EliminarCae_NoRegistrado_LanzaCaeException()
    {
        using var client = new DgiStub().Cliente();
        await Assert.ThrowsAsync<CaeException>(() => client.EliminarCaeAsync(TestData.Cae()));
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
        Assert.NotNull(FirmaDigital.VerificarFirma(emitido.Xml));
        Assert.Contains($"<DigestValue>{emitido.CodigoSeguridad}</DigestValue>", emitido.Xml);
        Assert.False(emitido.Enviado);
        Assert.Same(emitido, Assert.Single(await Listar(store)));
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
    public async Task Emitir_EnParalelo_NoRepiteNiSaltaNumeros()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;

        var emitidos = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => client.EmitirAsync(TestData.CfeValido()))));

        Assert.Equal(Enumerable.Range(1, 50).Select(n => (long)n), emitidos.Select(e => e.Numero).Order());
        Assert.Equal(50, (await Listar(store)).Count);
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

        Assert.Contains(ex.Errores, e => e.Campo == "Detalle");
        Assert.Equal(0, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task Emitir_TextoConCaracterDeControl_FallaLaValidacionSinConsumirNumero()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;
        var cfe = TestData.CfeValido() with { Detalle = [TestData.Linea() with { NombreItem = "Café\u0002" }] };

        var ex = await Assert.ThrowsAsync<CfeValidationException>(() => client.EmitirAsync(cfe));

        Assert.Contains(ex.Errores, e => e.Campo == "Detalle[0].NombreItem");
        Assert.Equal(0, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task Emitir_SiFallaElRegistro_NoConsumeNumero()
    {
        var store = new StoreQueFallaAlRegistrar(new InMemoryStore(TestData.Reloj));
        using var client = new DgiStub().Cliente(store);
        await client.RegistrarCaeAsync(TestData.Cae());

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EmitirAsync(TestData.CfeValido()));

        Assert.Equal(0, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
        Assert.Empty(await Listar(store));
    }

    [Fact]
    public async Task Emitir_Cancelado_NoConsumeNumero()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.EmitirAsync(TestData.CfeValido(), new CancellationToken(canceled: true)));

        Assert.Equal(0, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task Emitir_ListasModificadasDespues_NoCambianLoEmitido()
    {
        var (client, _, _) = await ClienteConCae();
        using var _ = client;
        var detalle = new List<LineaDetalle> { TestData.Linea() };

        var emitido = await client.EmitirAsync(TestData.CfeValido() with { Detalle = detalle });
        detalle[0] = TestData.Linea(precio: 999m);
        detalle.Add(TestData.Linea());

        Assert.Equal(100m, Assert.Single(emitido.Documento.Detalle).PrecioUnitario);
        Assert.Equal(emitido.Totales, emitido.Documento.CalcularTotales());
    }

    [Fact]
    public async Task Emitir_ConElMismoNroInterno_LanzaCfeDuplicadoConElYaEmitido()
    {
        var (client, _, store) = await ClienteConCae();
        using var _ = client;
        var cfe = TestData.CfeValido() with { NroInterno = "PEDIDO-7" };
        var primero = await client.EmitirAsync(cfe);

        // Un reintento (p. ej. tras un timeout del request) no factura dos veces.
        var ex = await Assert.ThrowsAsync<CfeDuplicadoException>(() => client.EmitirAsync(cfe));

        Assert.Same(primero, ex.Existente);
        Assert.Equal(1, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task Emitir_MismoNroInternoEnOtroTipo_SeEmite()
    {
        var (client, _, _) = await ClienteConCae();
        using var _ = client;
        await client.RegistrarCaeAsync(TestData.Cae(TipoCfe.NotaCreditoETicket));
        await client.EmitirAsync(TestData.CfeValido() with { NroInterno = "PEDIDO-7" });

        var nota = await client.EmitirAsync(TestData.CfeValido(TipoCfe.NotaCreditoETicket) with { NroInterno = "PEDIDO-7" });

        Assert.Equal(TipoCfe.NotaCreditoETicket, nota.Tipo);
    }

    [Fact]
    public async Task Emitir_MismoNroInternoDeUnCfeRechazado_EmiteUnoNuevo()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var cfe = TestData.CfeValido() with { NroInterno = "PEDIDO-7" };
        var rechazado = await client.EmitirAsync(cfe);
        dgi.EstadoPorNumero[rechazado.Numero] = EstadoCfe.Rechazado;
        await client.ConsultarCfeAsync(rechazado);

        var nuevo = await client.EmitirAsync(cfe);

        Assert.Equal(2, nuevo.Numero);
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
    public void Validar_EmisorPropioConOtroRut_TieneError()
    {
        using var client = new DgiStub().Cliente();
        var errores = client.Validar(TestData.CfeValido() with { Emisor = TestData.Emisor() with { Rut = "214844360018" } });
        Assert.Contains(errores, e => e.Campo == "Emisor.Rut" && e.Mensaje.Contains("RUT de la configuración"));
    }

    [Fact]
    public void Validar_FechaDeEmisionFutura_TieneError()
    {
        using var client = new DgiStub().Cliente();
        Assert.Contains(client.Validar(TestData.CfeValido() with { FechaEmision = TestData.Dia.AddDays(1) }),
            e => e.Campo == "FechaEmision");
    }

    [Fact]
    public void Validar_ConValorUnidadIndexada_AplicaElTopeDe5000UI()
    {
        using var client = new DgiStub().Cliente(config: TestData.Config() with { ValorUnidadIndexada = 0.01m }); // tope: 50 pesos

        Assert.Contains(client.Validar(TestData.CfeValido()), e => e.Mensaje.Contains("tope"));
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
        Assert.Equal("tok", (await Listar(store)).Single().TokenDgi);
    }

    [Fact]
    public async Task Enviar_SobreRechazado_NoLoMarcaEnviado_YSePuedeReenviar()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        dgi.EstadoSobre = EstadoSobre.Rechazado;
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        var acuse = await client.EnviarAsync(emitido);

        Assert.False(acuse.Recibido);
        Assert.False(emitido.Enviado);
        Assert.Equal("Rechazado", emitido.MensajeDgi);

        dgi.EstadoSobre = EstadoSobre.Recibido;
        Assert.True((await client.EnviarAsync(emitido)).Recibido);
        Assert.True(emitido.Enviado);
    }

    [Fact]
    public async Task Enviar_CfeYaRecibido_LanzaInvalidOperationException()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());
        await client.EnviarAsync(emitido);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EnviarAsync(emitido));
        Assert.Single(dgi.Sobres);
    }

    [Fact]
    public async Task Enviar_CfeAceptado_NoSeReenviaNiPierdeElEstado()
    {
        var (client, _, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());
        await client.ConsultarCfeAsync(emitido); // aceptado (p. ej. tras un envío cuyo acuse no llegó)

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.EnviarAsync(emitido));
        Assert.True(emitido.AceptadoPorDgi);
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
    public async Task Enviar_ElMismoCfeDosVecesEnElSobre_LanzaUruFacturaException()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());

        await Assert.ThrowsAsync<UruFacturaException>(() => client.EnviarAsync([emitido, emitido]));
        Assert.Empty(dgi.Sobres);
    }

    [Fact]
    public async Task ConsultarEnvio_ActualizaElEstadoDeCadaCfe_YLoGuarda()
    {
        var (client, dgi, store) = await ClienteConCae();
        using var _ = client;
        var ok = await client.EmitirAsync(TestData.CfeValido());
        var mal = await client.EmitirAsync(TestData.CfeValido());
        dgi.EstadoPorNumero[mal.Numero] = EstadoCfe.Rechazado;
        var acuse = await client.EnviarAsync([ok, mal]);

        var estado = await client.ConsultarEnvioAsync(acuse);

        Assert.True(estado.Procesado);
        Assert.False(estado.TodosAceptados);
        Assert.True(ok.AceptadoPorDgi);
        Assert.Equal(EstadoCfe.Rechazado, mal.EstadoDgi);
        Assert.Contains("Fuera de rango", mal.MensajeDgi);
        Assert.Equal<EstadoCfe?>([EstadoCfe.Aceptado, EstadoCfe.Rechazado], (await Listar(store)).Select(c => c.EstadoDgi));
    }

    [Fact]
    public async Task ConsultarEnvio_ResultadoSoloConNroOrdinal_LoAsociaPorLaPosicionEnElSobre()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var primero = await client.EmitirAsync(TestData.CfeValido());
        var segundo = await client.EmitirAsync(TestData.CfeValido());
        dgi.EstadoPorNumero[segundo.Numero] = EstadoCfe.Rechazado;
        dgi.SoloOrdinal = true; // sin TipoCFE, Serie ni NroCFE, y en otro orden
        var acuse = await client.EnviarAsync([primero, segundo]);

        await client.ConsultarEnvioAsync(acuse);

        Assert.Equal(EstadoCfe.Aceptado, primero.EstadoDgi);
        Assert.Equal(EstadoCfe.Rechazado, segundo.EstadoDgi);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ConsultarEnvioAsync(new AcuseSobre { Estado = EstadoSobre.Rechazado }));
    }

    [Fact]
    public async Task ConsultarCfe_ActualizaElEstado()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;
        var emitido = await client.EmitirAsync(TestData.CfeValido());
        dgi.EstadoPorNumero[emitido.Numero] = EstadoCfe.Observado;

        var estado = await client.ConsultarCfeAsync(emitido);

        Assert.Equal(EstadoCfe.Observado, estado.Estado);
        Assert.Equal(EstadoCfe.Observado, emitido.EstadoDgi);
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
        Assert.NotNull(FirmaDigital.VerificarFirma(dgi.UltimoReporte!));
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
    public async Task ReporteDiario_PorDefecto_EsElPrimerEnvioDelDia()
    {
        // ReporteDiarioCFE.xsd: "El primer envío del día trae el número 1".
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        await client.EnviarReporteDiarioAsync(TestData.Dia);

        Assert.Contains("<SecEnvio>1</SecEnvio>", dgi.UltimoReporte);
    }

    [Fact]
    public async Task ReporteDiario_InformaLaSecuenciaDelEnvio()
    {
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        await client.EnviarReporteDiarioAsync(TestData.Dia, secEnvio: 2);

        Assert.Contains("<SecEnvio>2</SecEnvio>", dgi.UltimoReporte);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task ReporteDiario_SecuenciaFueraDeRango_LanzaUruFacturaException(int secEnvio)
    {
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        await Assert.ThrowsAsync<UruFacturaException>(() => client.EnviarReporteDiarioAsync(TestData.Dia, secEnvio));
        Assert.Empty(dgi.Reportes);
    }

    [Fact]
    public async Task ReporteDiario_FechaFutura_LanzaUruFacturaException()
    {
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        await Assert.ThrowsAsync<UruFacturaException>(() => client.EnviarReporteDiarioAsync(TestData.Dia.AddDays(1)));
        Assert.Empty(dgi.Reportes);
    }

    [Fact]
    public async Task ReporteDiario_DeUnDiaQueElAlmacenamientoEnMemoriaNoTiene_NoDeclaraCeroCfe()
    {
        // Tras un reinicio, el almacenamiento en memoria no tiene los CFE de ayer: enviar un reporte
        // vacío sería una declaración falsa.
        var dgi = new DgiStub();
        using var client = dgi.Cliente();

        await Assert.ThrowsAsync<UruFacturaException>(() => client.EnviarReporteDiarioAsync(TestData.Dia.AddDays(-1)));
        Assert.Empty(dgi.Reportes);
    }

    [Fact]
    public async Task NumerosDeEnvio_SonUnicosEntreSobresYReportes()
    {
        var (client, dgi, _) = await ClienteConCae();
        using var _ = client;

        await client.EnviarAsync(await client.EmitirAsync(TestData.CfeValido()));
        await client.EnviarAsync(await client.EmitirAsync(TestData.CfeValido()));
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

    /// <summary>Almacenamiento cuyo registro de emisiones falla (por ejemplo, la base no responde).</summary>
    private sealed class StoreQueFallaAlRegistrar(InMemoryStore interno) : IUruFacturaStore
    {
        public ValueTask GuardarCaeAsync(Cae cae, CancellationToken ct = default) => interno.GuardarCaeAsync(cae, ct);
        public ValueTask<IReadOnlyList<Cae>> ListarCaesAsync(CancellationToken ct = default) => interno.ListarCaesAsync(ct);
        public ValueTask<bool> EliminarCaeAsync(Cae cae, CancellationToken ct = default) => interno.EliminarCaeAsync(cae, ct);
        public ValueTask<bool> RegistrarEmisionAsync(CfeEmitido emitido, long ultimo, CancellationToken ct = default) =>
            throw new InvalidOperationException("La base no responde.");
        public ValueTask<long> ReservarIdEnvioAsync(CancellationToken ct = default) => interno.ReservarIdEnvioAsync(ct);
        public ValueTask GuardarCfesAsync(IReadOnlyCollection<CfeEmitido> cfes, CancellationToken ct = default) => interno.GuardarCfesAsync(cfes, ct);
        public ValueTask<IReadOnlyList<CfeEmitido>> BuscarCfesAsync(string nroInterno, CancellationToken ct = default) =>
            interno.BuscarCfesAsync(nroInterno, ct);
        public IAsyncEnumerable<CfeEmitido> ListarCfesAsync(DateOnly fecha, CancellationToken ct = default) => interno.ListarCfesAsync(fecha, ct);
    }
}
