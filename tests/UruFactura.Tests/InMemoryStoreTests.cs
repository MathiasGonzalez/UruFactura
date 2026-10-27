namespace UruFactura.Tests;

public class InMemoryStoreTests
{
    private static readonly DateOnly Hoy = TestData.Dia;

    private static InMemoryStore Store(TimeProvider? reloj = null) => new(reloj ?? TestData.Reloj);

    /// <summary>Un CFE numerado con el CAE de prueba y firmado en <paramref name="fecha"/>, listo para registrar.</summary>
    private static CfeEmitido Emitido(long numero, Cae? cae = null, DateOnly? fecha = null, string? nroInterno = null) =>
        TestData.Emitido(
            TestData.CfeValido() with { FechaEmision = fecha ?? Hoy, NroInterno = nroInterno }, numero, cae,
            firma: new DateTimeOffset((fecha ?? Hoy).ToDateTime(new TimeOnly(12, 0)), HoraUruguay.Offset));

    private static async Task<List<CfeEmitido>> Listar(InMemoryStore store, DateOnly fecha) =>
        await store.ListarCfesAsync(fecha).ToListAsync();

    // --- CAE ---

    [Fact]
    public async Task GuardarCae_YListar()
    {
        var store = Store();
        await store.GuardarCaeAsync(TestData.Cae());
        await store.GuardarCaeAsync(TestData.Cae(TipoCfe.EFactura));

        Assert.Equal(2, (await store.ListarCaesAsync()).Count);
    }

    [Fact]
    public async Task GuardarCae_ElMismo_ActualizaDatosSinRetrocederElContador()
    {
        var store = Store();
        await store.GuardarCaeAsync(TestData.Cae(ultimo: 20));

        await store.GuardarCaeAsync(TestData.Cae(ultimo: 5, vence: new DateOnly(2050, 1, 1)));

        var cae = Assert.Single(await store.ListarCaesAsync());
        Assert.Equal(20, cae.UltimoNroUsado);
        Assert.Equal(new DateOnly(2050, 1, 1), cae.FechaVencimiento);
    }

    [Fact]
    public async Task EliminarCae_SinUsar_LoElimina_YUsadoNo()
    {
        var store = Store();
        await store.GuardarCaeAsync(TestData.Cae(serie: "A"));
        await store.GuardarCaeAsync(TestData.Cae(serie: "B", ultimo: 3));

        Assert.True(await store.EliminarCaeAsync(TestData.Cae(serie: "A")));
        Assert.False(await store.EliminarCaeAsync(TestData.Cae(serie: "B")));
        Assert.False(await store.EliminarCaeAsync(TestData.Cae(serie: "C")));
        Assert.Equal("B", Assert.Single(await store.ListarCaesAsync()).Serie);
    }

    // --- Emisión ---

    [Fact]
    public async Task RegistrarEmision_AvanzaElCaeYGuardaElCfe()
    {
        var store = Store();
        await store.GuardarCaeAsync(TestData.Cae());

        Assert.True(await store.RegistrarEmisionAsync(Emitido(1), ultimoNroUsadoLeido: 0));

        Assert.Equal(1, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
        Assert.Equal(1, Assert.Single(await Listar(store, Hoy)).Numero);
    }

    [Fact]
    public async Task RegistrarEmision_ConElCaeDesactualizado_NoGuardaNada()
    {
        var store = Store();
        await store.GuardarCaeAsync(TestData.Cae());
        await store.RegistrarEmisionAsync(Emitido(1), 0);

        // Otra emisión leyó el CAE antes de que se usara el 1 y numeró con el mismo número.
        Assert.False(await store.RegistrarEmisionAsync(Emitido(1), 0));

        Assert.Equal(1, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
        Assert.Single(await Listar(store, Hoy));
    }

    [Fact]
    public async Task RegistrarEmision_SinElCae_DevuelveFalse() =>
        Assert.False(await Store().RegistrarEmisionAsync(Emitido(1), 0));

    [Fact]
    public async Task RegistrarEmision_EnParalelo_SoloUnoPorNumero()
    {
        var store = Store();
        await store.GuardarCaeAsync(TestData.Cae());

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            Task.Run(async () => await store.RegistrarEmisionAsync(Emitido(1), 0))));

        Assert.Single(resultados, r => r);
    }

    [Fact]
    public async Task ReservarIdEnvio_EsCrecienteYDeHasta10Digitos()
    {
        var store = Store();
        var ids = new List<long>();
        for (var i = 0; i < 5; i++)
            ids.Add(await store.ReservarIdEnvioAsync());

        Assert.Equal(ids.Order(), ids);
        Assert.Equal(5, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, 1, 9_999_999_999));
    }

    // --- CFE ---

    [Fact]
    public async Task GuardarCfes_YListarPorFecha()
    {
        var reloj = new RelojManual(new DateTimeOffset(2026, 6, 14, 12, 0, 0, HoraUruguay.Offset));
        var store = Store(reloj);
        await store.GuardarCfesAsync([Emitido(3, fecha: Hoy.AddDays(-1))]);
        reloj.Utc = reloj.Utc.AddDays(1);
        await store.GuardarCfesAsync([Emitido(2), Emitido(1)]);

        Assert.Equal([1L, 2L], (await Listar(store, Hoy)).Select(c => c.Numero));
        Assert.Equal([3L], (await Listar(store, Hoy.AddDays(-1))).Select(c => c.Numero));
    }

    [Fact]
    public async Task ListarCfes_PorFechaDeFirma_NoDeEmision()
    {
        var store = Store();
        // Firmado hoy (al mediodía de Uruguay) con fecha de comprobante de anteayer.
        await store.GuardarCfesAsync([TestData.Emitido(TestData.CfeValido() with { FechaEmision = Hoy.AddDays(-2) }, 7)]);

        Assert.Equal([7L], (await Listar(store, Hoy)).Select(c => c.Numero));
    }

    [Fact]
    public async Task GuardarCfes_ElMismo_LoReemplaza()
    {
        var store = Store();
        await store.GuardarCfesAsync([Emitido(1)]);

        await store.GuardarCfesAsync([TestData.Emitido(numero: 1, estado: EstadoCfe.Aceptado)]);

        Assert.True(Assert.Single(await Listar(store, Hoy)).AceptadoPorDgi);
    }

    [Fact]
    public async Task BuscarCfes_PorNroInterno()
    {
        var store = Store();
        await store.GuardarCfesAsync([Emitido(1, nroInterno: "P-1"), Emitido(2, nroInterno: "P-2"), Emitido(3, nroInterno: "P-1")]);

        Assert.Equal([1L, 3L], (await store.BuscarCfesAsync("P-1")).Select(c => c.Numero).Order());
        Assert.Empty(await store.BuscarCfesAsync("P-9"));
    }

    [Fact]
    public async Task Retencion_SeCuentaDesdeHoy_NoDesdeElCfeMasNuevo()
    {
        var store = Store();
        await store.GuardarCfesAsync([Emitido(1)]);

        // Un CFE con fecha futura (que la validación ya no deja emitir, pero otro proceso podría
        // guardar) no descarta los de hoy: antes vaciaba el reporte del día.
        var futuro = new CfeEmitido(
            TestData.CfeValido() with { FechaEmision = new DateOnly(2040, 1, 1), Emisor = TestData.Emisor() },
            TestData.Cae(), 2, new Totales(), DateTimeOffset.UtcNow, "x", "<CFE/>");
        await store.GuardarCfesAsync([futuro]);

        Assert.Equal([1L], (await Listar(store, Hoy)).Select(c => c.Numero));
    }

    [Fact]
    public async Task Retencion_DescartaLosDiasViejos_YNoListaFechasIncompletas()
    {
        var reloj = new RelojManual(new DateTimeOffset(2026, 6, 13, 12, 0, 0, HoraUruguay.Offset));
        var store = new InMemoryStore(reloj) { DiasRetencionCfe = 2 };
        await store.GuardarCfesAsync([Emitido(1, fecha: Hoy.AddDays(-2))]);
        reloj.Utc = reloj.Utc.AddDays(1);
        await store.GuardarCfesAsync([Emitido(2, fecha: Hoy.AddDays(-1))]);
        reloj.Utc = reloj.Utc.AddDays(1);
        await store.GuardarCfesAsync([Emitido(3)]);

        // Conserva hoy y ayer; del 13 ya no tiene todos los CFE: no puede devolver una lista incompleta.
        Assert.Single(await Listar(store, Hoy.AddDays(-1)));
        await Assert.ThrowsAsync<UruFacturaException>(() => Listar(store, Hoy.AddDays(-2)));
    }

    [Fact]
    public async Task ListarCfes_AntesDeCrearse_LanzaUruFacturaException()
    {
        // Tras un reinicio no tiene los CFE de ayer: un reporte con cero comprobantes sería falso.
        var store = Store();
        await Assert.ThrowsAsync<UruFacturaException>(() => Listar(store, Hoy.AddDays(-1)));
    }

    [Fact]
    public void DiasRetencion_MenorQueUno_LanzaArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryStore { DiasRetencionCfe = 0 });
}
