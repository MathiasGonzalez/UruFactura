namespace UruFactura.Tests;

public class InMemoryStoreTests
{
    private static readonly DateOnly Hoy = new(2026, 6, 15);

    [Fact]
    public async Task GuardarCae_YListar()
    {
        var store = new InMemoryStore();
        await store.GuardarCaeAsync(TestData.Cae());
        await store.GuardarCaeAsync(TestData.Cae(TipoCfe.EFactura));

        Assert.Equal(2, (await store.ListarCaesAsync()).Count);
    }

    [Fact]
    public async Task GuardarCae_ElMismo_ActualizaDatosSinRetrocederElContador()
    {
        var store = new InMemoryStore();
        await store.GuardarCaeAsync(TestData.Cae(ultimo: 20));

        await store.GuardarCaeAsync(TestData.Cae(ultimo: 5, vence: new DateOnly(2050, 1, 1)));

        var cae = Assert.Single(await store.ListarCaesAsync());
        Assert.Equal(20, cae.UltimoNroUsado);
        Assert.Equal(new DateOnly(2050, 1, 1), cae.FechaVencimiento);
    }

    [Fact]
    public async Task ReservarNumero_EsConsecutivo_YActualizaElContador()
    {
        var store = new InMemoryStore();
        await store.GuardarCaeAsync(TestData.Cae(desde: 10, hasta: 20));

        var a = await store.ReservarNumeroAsync(TipoCfe.ETicket, Hoy);
        var b = await store.ReservarNumeroAsync(TipoCfe.ETicket, Hoy);

        Assert.Equal(10, a!.UltimoNroUsado);
        Assert.Equal(11, b!.UltimoNroUsado);
        Assert.Equal(11, (await store.ListarCaesAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task ReservarNumero_EnParalelo_NoRepite()
    {
        var store = new InMemoryStore();
        await store.GuardarCaeAsync(TestData.Cae(hasta: 1000));

        var numeros = await Task.WhenAll(Enumerable.Range(0, 500).Select(_ =>
            Task.Run(async () => (await store.ReservarNumeroAsync(TipoCfe.ETicket, Hoy))!.UltimoNroUsado)));

        Assert.Equal(Enumerable.Range(1, 500).Select(n => (long)n), numeros.Order());
    }

    [Fact]
    public async Task ReservarNumero_RangoAgotado_PasaAlSiguienteCae()
    {
        var store = new InMemoryStore();
        await store.GuardarCaeAsync(TestData.Cae(serie: "A", hasta: 1, vence: Hoy.AddYears(2)));
        await store.GuardarCaeAsync(TestData.Cae(serie: "B", vence: Hoy.AddYears(1)));

        var primero = await store.ReservarNumeroAsync(TipoCfe.ETicket, Hoy);
        var segundo = await store.ReservarNumeroAsync(TipoCfe.ETicket, Hoy);

        Assert.Equal("A", primero!.Serie);
        Assert.Equal("B", segundo!.Serie);
    }

    [Fact]
    public async Task ReservarNumero_SinCaeDisponible_DevuelveNull()
    {
        var store = new InMemoryStore();
        await store.GuardarCaeAsync(TestData.Cae(vence: Hoy.AddDays(-1)));

        Assert.Null(await store.ReservarNumeroAsync(TipoCfe.ETicket, Hoy));
        Assert.Null(await store.ReservarNumeroAsync(TipoCfe.EFactura, Hoy));
    }

    [Fact]
    public async Task ReservarIdEnvio_EsCrecienteYDeHasta10Digitos()
    {
        var store = new InMemoryStore();
        var ids = new List<long>();
        for (var i = 0; i < 5; i++)
            ids.Add(await store.ReservarIdEnvioAsync());

        Assert.Equal(ids.Order(), ids);
        Assert.Equal(5, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, 1, 9_999_999_999));
    }

    [Fact]
    public async Task GuardarCfes_YListarPorFecha()
    {
        var store = new InMemoryStore();
        await store.GuardarCfesAsync([TestData.Emitido(numero: 2), TestData.Emitido(numero: 1)]);
        await store.GuardarCfesAsync([TestData.Emitido(TestData.CfeValido() with { FechaEmision = Hoy.AddDays(-1) }, 3)]);

        var delDia = await store.ListarCfesAsync(Hoy);

        Assert.Equal([1L, 2L], delDia.Select(c => c.Numero));
    }

    [Fact]
    public async Task GuardarCfes_ElMismo_LoReemplaza()
    {
        var store = new InMemoryStore();
        var cfe = TestData.Emitido();
        await store.GuardarCfesAsync([cfe]);

        cfe.EstadoDgi = "AE";
        await store.GuardarCfesAsync([cfe]);

        Assert.True(Assert.Single(await store.ListarCfesAsync(Hoy)).AceptadoPorDgi);
    }

    [Fact]
    public async Task GuardarCfes_DescartaLosDeMasDeLosDiasDeRetencion()
    {
        var store = new InMemoryStore { DiasRetencionCfe = 2 };
        await store.GuardarCfesAsync([TestData.Emitido(TestData.CfeValido() with { FechaEmision = Hoy.AddDays(-2) }, 1)]);
        await store.GuardarCfesAsync([TestData.Emitido(TestData.CfeValido() with { FechaEmision = Hoy.AddDays(-1) }, 2)]);
        await store.GuardarCfesAsync([TestData.Emitido(numero: 3)]);

        Assert.Empty(await store.ListarCfesAsync(Hoy.AddDays(-2)));
        Assert.Single(await store.ListarCfesAsync(Hoy.AddDays(-1)));
    }

    [Fact]
    public void DiasRetencion_MenorQueUno_LanzaArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new InMemoryStore { DiasRetencionCfe = 0 });
}
