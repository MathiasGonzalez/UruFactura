using UruFactura.Cae;
using UruFactura.Enums;
using Xunit;

namespace UruFactura.Tests;

public class CaeRepositoryTests
{
    private static Models.Cae CrearCae(
        string nroSerie = "CAE-001",
        TipoCfe tipo = TipoCfe.ETicket,
        long desde = 1,
        long hasta = 100,
        long ultimoUsado = 0,
        DateOnly? fechaVencimiento = null) =>
        new()
        {
            NroSerie         = nroSerie,
            TipoCfe          = tipo,
            RangoDesde       = desde,
            RangoHasta       = hasta,
            FechaVencimiento = fechaVencimiento ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(3)),
            UltimoNroUsado   = ultimoUsado,
        };

    // ── CargarTodosAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task CargarTodosAsync_RepositorioVacio_RetornaColeccionVacia()
    {
        var repo = new InMemoryCaeRepository();

        var caes = await repo.CargarTodosAsync();

        Assert.Empty(caes);
    }

    [Fact]
    public async Task CargarTodosAsync_DespuesDeGuardar_RetornaCaeGuardado()
    {
        var repo = new InMemoryCaeRepository();
        var cae = CrearCae();

        await repo.GuardarCaeAsync(cae);
        var caes = (await repo.CargarTodosAsync()).ToList();

        Assert.Single(caes);
        Assert.Equal("CAE-001", caes[0].NroSerie);
        Assert.Equal(TipoCfe.ETicket, caes[0].TipoCfe);
        Assert.Equal(1, caes[0].RangoDesde);
        Assert.Equal(100, caes[0].RangoHasta);
    }

    [Fact]
    public async Task CargarTodosAsync_MultiplesCAEs_RetornaTodos()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", TipoCfe.ETicket));
        await repo.GuardarCaeAsync(CrearCae("CAE-002", TipoCfe.EFactura));

        var caes = (await repo.CargarTodosAsync()).ToList();

        Assert.Equal(2, caes.Count);
    }

    // ── GuardarCaeAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GuardarCaeAsync_NroSerieExistente_SobreescribeCAE()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", hasta: 100));
        await repo.GuardarCaeAsync(CrearCae("CAE-001", hasta: 500)); // mismo NroSerie

        var caes = (await repo.CargarTodosAsync()).ToList();

        Assert.Single(caes);
        Assert.Equal(500, caes[0].RangoHasta);
    }

    [Fact]
    public async Task GuardarCaeAsync_PreservaUltimoNroUsado()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", ultimoUsado: 42));

        var caes = (await repo.CargarTodosAsync()).ToList();

        Assert.Equal(42, caes[0].UltimoNroUsado);
    }

    [Fact]
    public async Task GuardarCaeAsync_CaeNull_LanzaArgumentNullException()
    {
        var repo = new InMemoryCaeRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await repo.GuardarCaeAsync(null!));
    }

    // ── ActualizarUltimoNroUsadoAsync ─────────────────────────────────────────

    [Fact]
    public async Task ActualizarUltimoNroUsadoAsync_ActualizaValor()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", ultimoUsado: 0));

        await repo.ActualizarUltimoNroUsadoAsync("CAE-001", 55);

        var caes = (await repo.CargarTodosAsync()).ToList();
        Assert.Equal(55, caes[0].UltimoNroUsado);
    }

    [Fact]
    public async Task ActualizarUltimoNroUsadoAsync_NroSerieInexistente_LanzaKeyNotFoundException()
    {
        var repo = new InMemoryCaeRepository();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            async () => await repo.ActualizarUltimoNroUsadoAsync("NO-EXISTE", 1));
    }

    [Fact]
    public async Task ActualizarUltimoNroUsadoAsync_NroSerieNull_LanzaArgumentNullException()
    {
        var repo = new InMemoryCaeRepository();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await repo.ActualizarUltimoNroUsadoAsync(null!, 1));
    }

    [Fact]
    public async Task ActualizarUltimoNroUsadoAsync_ActualizacionesMultiples_ConservaUltimoValor()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001"));

        await repo.ActualizarUltimoNroUsadoAsync("CAE-001", 10);
        await repo.ActualizarUltimoNroUsadoAsync("CAE-001", 20);
        await repo.ActualizarUltimoNroUsadoAsync("CAE-001", 30);

        var caes = (await repo.CargarTodosAsync()).ToList();
        Assert.Equal(30, caes[0].UltimoNroUsado);
    }

    // ── Integración CaeManager + ICaeRepository ───────────────────────────────

    [Fact]
    public async Task Integracion_CargarDesdeRepositorio_CaeManagerUsaCAECargado()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", desde: 50, hasta: 200, ultimoUsado: 49));

        var manager = new CaeManager();
        manager.RegistrarCaes(await repo.CargarTodosAsync());

        var (_, numero) = manager.ObtenerProximoNumero(TipoCfe.ETicket);

        Assert.Equal(50, numero);
    }

    [Fact]
    public async Task Integracion_EmisionYPersistencia_UltimoNroUsadoQuedaActualizado()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", desde: 1, hasta: 100, ultimoUsado: 0));

        var manager = new CaeManager();
        manager.RegistrarCaes(await repo.CargarTodosAsync());

        var (cae, numero) = manager.ObtenerProximoNumero(TipoCfe.ETicket);
        await repo.ActualizarUltimoNroUsadoAsync(cae.NroSerie, cae.UltimoNroUsado);

        var caes = (await repo.CargarTodosAsync()).ToList();
        Assert.Equal(numero, caes[0].UltimoNroUsado);
    }

    [Fact]
    public async Task Integracion_ReinicioSimulado_ContinuaDesdeUltimoNumero()
    {
        var repo = new InMemoryCaeRepository();
        await repo.GuardarCaeAsync(CrearCae("CAE-001", desde: 1, hasta: 100, ultimoUsado: 0));

        // Primera "instancia" de la app — emite 3 comprobantes
        var manager1 = new CaeManager();
        manager1.RegistrarCaes(await repo.CargarTodosAsync());

        for (var i = 0; i < 3; i++)
        {
            var (cae, _) = manager1.ObtenerProximoNumero(TipoCfe.ETicket);
            await repo.ActualizarUltimoNroUsadoAsync(cae.NroSerie, cae.UltimoNroUsado);
        }

        // Segunda "instancia" (reinicio) — debe continuar desde 4
        var manager2 = new CaeManager();
        manager2.RegistrarCaes(await repo.CargarTodosAsync());

        var (_, numero) = manager2.ObtenerProximoNumero(TipoCfe.ETicket);
        Assert.Equal(4, numero);
    }

    [Fact]
    public async Task GuardarYCargar_ConservaNumeroAutorizacionYSerie()
    {
        var repo = new InMemoryCaeRepository();
        var cae = CrearCae();
        cae.NumeroAutorizacion = "90110001945";
        cae.Serie = "AB";

        await repo.GuardarCaeAsync(cae);
        var cargado = (await repo.CargarTodosAsync()).Single();

        Assert.Equal("90110001945", cargado.NumeroAutorizacion);
        Assert.Equal("AB", cargado.Serie);
    }
}
