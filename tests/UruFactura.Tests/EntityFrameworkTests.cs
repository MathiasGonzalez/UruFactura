using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UruFactura.EntityFrameworkCore;

namespace UruFactura.Tests;

/// <summary>
/// Tests de UruFactura.EntityFrameworkCore contra una base SQLite en archivo, compartida por varios
/// almacenamientos y clientes que simulan instancias distintas de la aplicación.
/// </summary>
public sealed class EntityFrameworkTests : IDisposable
{
    private static readonly DateOnly Hoy = TestData.Dia;
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"urufactura-{Guid.NewGuid():N}.db");

    public EntityFrameworkTests()
    {
        using var db = new Fabrica(_ruta).CreateDbContext();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_ruta);
    }

    private sealed class Fabrica(string ruta) : IDbContextFactory<UruFacturaDbContext>
    {
        private readonly DbContextOptions<UruFacturaDbContext> _options =
            new DbContextOptionsBuilder<UruFacturaDbContext>().UseSqlite($"Data Source={ruta}").Options;

        public UruFacturaDbContext CreateDbContext() => new(_options);
    }

    /// <summary>El almacenamiento de una "instancia" (fábrica y conexiones propias).</summary>
    private EfUruFacturaStore<UruFacturaDbContext> Store() => new(new Fabrica(_ruta));

    private UruFacturaDbContext Db() => new Fabrica(_ruta).CreateDbContext();

    private static Cae NuevoCae(string serie = "A", long desde = 1, long hasta = 1000, DateOnly? vence = null) =>
        TestData.Cae(serie: serie, desde: desde, hasta: hasta, vence: vence ?? Hoy.AddYears(1));

    // -----------------------------------------------------------------------
    // CAE y numeración
    // -----------------------------------------------------------------------

    /// <summary>Un CFE numerado con el CAE indicado, listo para registrar.</summary>
    private static CfeEmitido Emitido(long numero, Cae? cae = null, string? nroInterno = null) =>
        TestData.Emitido(TestData.CfeValido() with { NroInterno = nroInterno }, numero, cae ?? NuevoCae());

    [Fact]
    public async Task VariasInstancias_EmitenEnParalelo_SinRepetirNiSaltarNumeros()
    {
        await Store().GuardarCaeAsync(NuevoCae());
        var dgi = new DgiStub();
        var instancias = Enumerable.Range(0, 8).Select(_ => dgi.Cliente(Store())).ToList();

        var emitidos = await Task.WhenAll(instancias.SelectMany(cliente => Enumerable.Range(0, 25).Select(_ =>
            Task.Run(() => cliente.EmitirAsync(new Cfe(TipoCfe.ETicket) { Detalle = [TestData.Linea()] })))));

        Assert.Equal(Enumerable.Range(1, 200).Select(n => (long)n), emitidos.Select(e => e.Numero).Order());
        Assert.Equal(200, (await Store().ListarCaesAsync()).Single().UltimoNroUsado);
        using var db = Db();
        Assert.Equal(200, await db.Cfes.CountAsync());
        foreach (var cliente in instancias)
            cliente.Dispose();
    }

    [Fact]
    public async Task RegistrarEmision_AvanzaElCaeYGuardaElCfeJuntos()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae(desde: 1001, hasta: 2000));

        Assert.True(await store.RegistrarEmisionAsync(Emitido(1001, NuevoCae(desde: 1001, hasta: 2000)), ultimoNroUsadoLeido: 0));

        Assert.Equal(1001, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
        using var db = Db();
        Assert.Equal(1001, (await db.Cfes.SingleAsync()).Numero);
    }

    [Fact]
    public async Task RegistrarEmision_ConElCaeDesactualizado_NoGuardaNada()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae());
        await store.RegistrarEmisionAsync(Emitido(1), 0);

        // Otra instancia leyó el CAE antes de que se usara el 1: ni avanza el CAE ni inserta el CFE.
        Assert.False(await Store().RegistrarEmisionAsync(Emitido(1), 0));

        Assert.Equal(1, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
        using var db = Db();
        Assert.Equal(1, await db.Cfes.CountAsync());
    }

    [Fact]
    public async Task RegistrarEmision_SiFallaElInsert_NoAvanzaElCae()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae());
        await store.GuardarCfesAsync([Emitido(1)]); // el número ya está registrado (p. ej. por otro CAE superpuesto)

        await Assert.ThrowsAsync<DbUpdateException>(async () => await store.RegistrarEmisionAsync(Emitido(1), 0));

        Assert.Equal(0, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task EliminarCae_SinUsar_LoElimina_YUsadoNo()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae("A"));
        await store.GuardarCaeAsync(NuevoCae("B") with { UltimoNroUsado = 3 });

        Assert.True(await store.EliminarCaeAsync(NuevoCae("A")));
        Assert.False(await store.EliminarCaeAsync(NuevoCae("B")));
        Assert.Equal("B", (await store.ListarCaesAsync()).Single().Serie);
    }

    [Fact]
    public async Task Emitir_EmpiezaEnElInicioDelRango_YPrefiereElCaeQueVenceMasTarde()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae("V", vence: Hoy.AddDays(-1)));
        await store.GuardarCaeAsync(NuevoCae("G", desde: 1, hasta: 10, vence: Hoy.AddYears(3)) with { UltimoNroUsado = 10 });
        await store.GuardarCaeAsync(NuevoCae("C", desde: 1, hasta: 100, vence: Hoy.AddDays(30)));
        await store.GuardarCaeAsync(NuevoCae("L", desde: 101, hasta: 200, vence: Hoy.AddYears(2)));
        using var cliente = new DgiStub().Cliente(store);

        var emitido = await cliente.EmitirAsync(TestData.CfeValido());

        Assert.Equal(("L", 101L), (emitido.Serie, emitido.Numero));
    }

    [Fact]
    public async Task GuardarCae_ElMismoConContadorMenor_ActualizaDatosSinRetrocederElContador()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae());
        for (var i = 0; i < 5; i++)
            await store.RegistrarEmisionAsync(Emitido(i + 1), i);

        // Otra instancia vuelve a registrar el CAE con un contador viejo y nueva fecha de vencimiento.
        var vence = Hoy.AddYears(2);
        await Store().GuardarCaeAsync(NuevoCae(vence: vence));

        var guardado = (await store.ListarCaesAsync()).Single();
        Assert.Equal(5, guardado.UltimoNroUsado);
        Assert.Equal(vence, guardado.FechaVencimiento);
    }

    [Fact]
    public async Task GuardarCae_ConContadorMayor_LoAvanza()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae());
        await store.GuardarCaeAsync(NuevoCae() with { UltimoNroUsado = 40 });

        Assert.Equal(40, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task ReservarIdEnvio_VariasInstanciasEnParalelo_SonUnicosYDeHasta10Digitos()
    {
        var instancias = Enumerable.Range(0, 5).Select(_ => Store()).ToList();

        var ids = await Task.WhenAll(instancias.SelectMany(store => Enumerable.Range(0, 20).Select(_ =>
            Task.Run(async () => await store.ReservarIdEnvioAsync()))));

        Assert.Equal(100, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, 1, 9_999_999_999));
    }

    [Fact]
    public void MaxIntentos_MenorQueUno_LanzaArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new EfUruFacturaStore<UruFacturaDbContext>(new Fabrica(_ruta)) { MaxIntentos = 0 });

    // -----------------------------------------------------------------------
    // Claves
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Claves_SonGuidV7OrdenadosPorTiempo()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae("A"));
        await Task.Delay(5);
        await store.GuardarCaeAsync(NuevoCae("B"));

        using var db = Db();
        var ids = await db.Caes.OrderBy(c => c.Serie).Select(c => c.Id).ToListAsync();

        Assert.All(ids, id => Assert.Equal(7, id.Version));
        Assert.True(ids[0].CompareTo(ids[1]) < 0);
    }

    [Fact]
    public void Claves_EnSqlServer_UsanElGuidSecuencialDeSqlServer()
    {
        var options = new DbContextOptionsBuilder<UruFacturaDbContext>()
            .UseSqlServer("Server=localhost;Database=UruFactura;Trusted_Connection=True").Options;
        using var db = new UruFacturaDbContext(options);

        // Generar la clave no requiere conexión: ocurre al agregar la entidad.
        var a = db.Cfes.Add(new CfeEntity { Serie = "A", Numero = 1 }).Entity.Id;
        var b = db.Cfes.Add(new CfeEntity { Serie = "A", Numero = 2 }).Entity.Id;

        Assert.NotEqual(7, a.Version);
        // SqlGuid compara con el mismo orden que SQL Server usa en el índice.
        Assert.True(new System.Data.SqlTypes.SqlGuid(a).CompareTo(new System.Data.SqlTypes.SqlGuid(b)) < 0);
    }

    // -----------------------------------------------------------------------
    // CFE
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(TipoCfe.ETicket)]
    [InlineData(TipoCfe.EFacturaExportacion)]
    [InlineData(TipoCfe.EResguardo)]
    [InlineData(TipoCfe.NotaCreditoEFacturaCuentaAjena)]
    [InlineData(TipoCfe.EBoleta)]
    public async Task GuardarCfe_YListar_ReconstruyeElCfeEmitidoCompleto(TipoCfe tipo)
    {
        var cfe = TestData.Emitido(TestData.CfeValido(tipo), 7, firma: new DateTimeOffset(2026, 6, 15, 21, 30, 0, HoraUruguay.Offset));
        await Store().GuardarCfesAsync([cfe]);

        var leido = Assert.Single(await Store().ListarCfesAsync(TestData.Dia).ToListAsync());

        Assert.Equal((cfe.Tipo, cfe.Serie, cfe.Numero), (leido.Tipo, leido.Serie, leido.Numero));
        Assert.Equal(cfe.Xml, leido.Xml);
        Assert.Equal(cfe.Totales, leido.Totales);
        Assert.Equal(cfe.Cae, leido.Cae);
        Assert.Equal(cfe.Emisor with { Telefonos = [] }, leido.Emisor with { Telefonos = [] });
        Assert.Equal(cfe.CodigoSeguridad, leido.CodigoSeguridad);
        Assert.Equal(cfe.FechaFirma, leido.FechaFirma);
        Assert.Equal(cfe.UrlQr, leido.UrlQr);
        Assert.Equal(cfe.Documento.Detalle.Count, leido.Documento.Detalle.Count);
        Assert.Equal(cfe.Documento.Receptor, leido.Documento.Receptor);
        Assert.Equal(cfe.Documento.Moneda, leido.Documento.Moneda);
    }

    [Fact]
    public async Task ListarCfes_PorFechaDeReporte_LaDeLaFirmaEnUruguay()
    {
        var store = Store();
        // Firmado el 15 a las 22:30 de Uruguay (16 en UTC) con fecha de comprobante del 14.
        var firma = new DateTimeOffset(2026, 6, 16, 1, 30, 0, TimeSpan.Zero);
        await store.GuardarCfesAsync([TestData.Emitido(TestData.CfeValido() with { FechaEmision = Hoy.AddDays(-1) }, 1, NuevoCae(), firma)]);

        Assert.Single(await store.ListarCfesAsync(Hoy).ToListAsync());
        Assert.Empty(await store.ListarCfesAsync(Hoy.AddDays(-1)).ToListAsync());
        Assert.Empty(await store.ListarCfesAsync(Hoy.AddDays(1)).ToListAsync());
    }

    [Fact]
    public async Task GuardarCae_ConservaElCaeEspecial()
    {
        var store = Store();
        await store.GuardarCaeAsync(NuevoCae() with { Especial = CaeEspecial.Exonerado, CausalEspecial = 7 });

        var cae = Assert.Single(await Store().ListarCaesAsync());
        Assert.Equal((CaeEspecial.Exonerado, 7), (cae.Especial!.Value, cae.CausalEspecial!.Value));
    }

    [Fact]
    public async Task GuardarCfe_FechaFirmaSeGuardaEnUtc()
    {
        var cfe = TestData.Emitido(firma: new DateTimeOffset(2026, 6, 15, 21, 30, 0, HoraUruguay.Offset));
        await Store().GuardarCfesAsync([cfe]);

        using var db = Db();
        var fila = await db.Cfes.SingleAsync();
        Assert.Equal(TimeSpan.Zero, fila.FechaFirma.Offset); // Npgsql rechaza offsets distintos de 0
        Assert.Equal(cfe.FechaFirma, fila.FechaFirma);
    }

    [Fact]
    public async Task GuardarCfe_ExistenteActualizaSoloElEstado()
    {
        var cfe = TestData.Emitido();
        await Store().GuardarCfesAsync([cfe]);

        var leido = (await Store().ListarCfesAsync(TestData.Dia).ToListAsync()).Single();
        await Store().GuardarCfesAsync([new CfeEmitido(leido.Documento, leido.Cae, leido.Numero, leido.Totales, leido.FechaFirma, leido.CodigoSeguridad, leido.Xml)
        {
            IdReceptorDgi = "9",
            EstadoDgi = EstadoCfe.Aceptado,
        }]);

        using var db = Db();
        var fila = await db.Cfes.SingleAsync();
        Assert.Equal(("9", EstadoCfe.Aceptado), (fila.IdReceptorDgi, fila.EstadoDgi));
        Assert.Equal(cfe.Xml, fila.Xml);
        Assert.Equal(244m, fila.MontoTotal);
        Assert.True(fila.ToCfeEmitido().AceptadoPorDgi);
        // El estado se guarda como texto legible en la base.
        Assert.Equal("Aceptado", await db.Database.SqlQueryRaw<string>("SELECT EstadoDgi AS Value FROM UruFacturaCfes").SingleAsync());
    }

    [Fact]
    public async Task BuscarCfes_PorNroInterno()
    {
        var store = Store();
        await store.GuardarCfesAsync([Emitido(1, nroInterno: "P-1"), Emitido(2, nroInterno: "P-2"), Emitido(3, nroInterno: "P-1")]);

        Assert.Equal([1L, 3L], (await store.BuscarCfesAsync("P-1")).Select(c => c.Numero));
    }

    // -----------------------------------------------------------------------
    // UruFacturaClient con varias instancias
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VariasInstancias_EmitenEnParalelo_YElReporteDiarioIncluyeLosDeTodas()
    {
        var dgi = new DgiStub();
        await Store().GuardarCaeAsync(NuevoCae());
        var instancias = Enumerable.Range(0, 4).Select(_ => dgi.Cliente(Store())).ToList();

        await Task.WhenAll(instancias.SelectMany(cliente => Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var emitido = await cliente.EmitirAsync(new Cfe(TipoCfe.ETicket) { Detalle = [TestData.Linea()] });
            await cliente.EnviarAsync(emitido);
        }))));

        // El reporte lo envía otra instancia (p. ej. un job nocturno) que no emitió ningún CFE.
        using var job = dgi.Cliente(Store());
        var acuse = await job.EnviarReporteDiarioAsync(Hoy);

        Assert.Equal(40, acuse.CantidadCfe);
        var reporte = dgi.Reportes.Single();
        TestData.AssertValidoContraXsd(reporte);
        Assert.Contains("<CantDocsUtil>40</CantDocsUtil>", reporte);
        Assert.Contains("<NroDesde>1</NroDesde><NroHasta>40</NroHasta>", reporte);
        Assert.Contains("<TotMntTotal>9760.00</TotMntTotal>", reporte); // 40 × (200 + 22 % de IVA)
        Assert.Equal(41, dgi.IdsEnvio.Distinct().Count()); // 40 sobres y el reporte, sin repetir Idemisor

        using var db = Db();
        var filas = await db.Cfes.ToListAsync();
        Assert.Equal(40, filas.Count);
        Assert.All(filas, f => Assert.Equal("tok", f.TokenDgi));

        foreach (var cliente in instancias)
            cliente.Dispose();
    }

    [Fact]
    public async Task ConsultarCfe_DesdeOtraInstancia_ActualizaElEstadoEnLaBase()
    {
        var dgi = new DgiStub();
        await Store().GuardarCaeAsync(NuevoCae());
        using var cliente = dgi.Cliente(Store());
        var emitido = await cliente.EmitirAsync(new Cfe(TipoCfe.ETicket) { Detalle = [TestData.Linea()] });
        await cliente.EnviarAsync(emitido);

        // Otra instancia (p. ej. tras un reinicio) consulta los pendientes guardados en la base.
        using var otra = dgi.Cliente(Store());
        foreach (var pendiente in await Store().ListarCfesAsync(Hoy).ToListAsync())
            await otra.ConsultarCfeAsync(pendiente);

        using var db = Db();
        Assert.Equal(EstadoCfe.Aceptado, (await db.Cfes.SingleAsync()).EstadoDgi);
    }

    [Fact]
    public async Task Emitir_SinCaeEnLaBase_LanzaCaeException()
    {
        using var cliente = new DgiStub().Cliente(Store());
        await Assert.ThrowsAsync<CaeException>(() => cliente.EmitirAsync(TestData.CfeValido()));
    }
}
