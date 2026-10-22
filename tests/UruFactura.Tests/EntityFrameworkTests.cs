using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UruFactura.Cae;
using UruFactura.Configuration;
using UruFactura.EntityFrameworkCore;
using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Models;
using UruFactura.Signature;
using UruFactura.Soap;
using UruFactura.Xml;

namespace UruFactura.Tests;

/// <summary>
/// Tests de UruFactura.EntityFrameworkCore contra una base SQLite en archivo, compartida por varios
/// repositorios y clientes que simulan instancias distintas de la aplicación.
/// </summary>
public sealed class EntityFrameworkTests : IDisposable
{
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

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed class Fabrica(string ruta) : IDbContextFactory<UruFacturaDbContext>
    {
        private readonly DbContextOptions<UruFacturaDbContext> _options =
            new DbContextOptionsBuilder<UruFacturaDbContext>().UseSqlite($"Data Source={ruta}").Options;

        public UruFacturaDbContext CreateDbContext() => new(_options);
    }

    /// <summary>Repositorio de CAE de una "instancia" (fábrica y conexiones propias).</summary>
    private EfCaeRepository<UruFacturaDbContext> RepoCae() => new(new Fabrica(_ruta));

    private EfCfeRepository<UruFacturaDbContext> RepoCfe() => new(new Fabrica(_ruta));

    private UruFacturaDbContext Db() => new Fabrica(_ruta).CreateDbContext();

    private static Models.Cae NuevoCae(
        string nroSerie = "CAE-1", long desde = 1, long hasta = 1000, DateOnly? vence = null) => new()
    {
        NroSerie = nroSerie,
        NumeroAutorizacion = "90110001945",
        Serie = "A",
        TipoCfe = TipoCfe.ETicket,
        RangoDesde = desde,
        RangoHasta = hasta,
        FechaVencimiento = vence ?? HoraUruguay.Hoy().AddYears(1),
    };

    private static readonly UruFacturaConfig Config = new()
    {
        RutEmisor = "219999820013",
        RazonSocialEmisor = "Empresa Test S.A.",
        DomicilioFiscal = "Av. 18 de Julio 1234",
        Ciudad = "Montevideo",
        Departamento = "Montevideo",
    };

    /// <summary>Una instancia de la aplicación: gestor en memoria propio, misma base.</summary>
    private UruFacturaClient NuevaInstancia(IDgiSoapClient soap) => new(
        Config, new CfeXmlBuilder(), new CfeFirmante(TestData.CertificadoAutofirmado()), new CaeManager(),
        soap, null, RepoCae(), RepoCfe());

    /// <summary>Stub SOAP thread-safe que registra los números de envío recibidos.</summary>
    private sealed class SoapStub : IDgiSoapClient
    {
        private int _idReceptor;

        public ConcurrentBag<long> IdsEnvio { get; } = [];
        public ConcurrentBag<string> Reportes { get; } = [];
        public List<EstadoComprobante> EstadoEnvio { get; } = [];

        private static long Id(string xml, string elemento) =>
            long.Parse(Regex.Match(xml, $"<{elemento}>(\\d+)</{elemento}>").Groups[1].Value);

        public Task<RespuestaDgi> EnviarSobreAsync(string sobreXml, CancellationToken ct = default)
        {
            IdsEnvio.Add(Id(sobreXml, "Idemisor"));
            var r = RespuestaDgi.Exito("AS", "OK");
            r.IdReceptor = Interlocked.Increment(ref _idReceptor).ToString();
            r.Token = "tok";
            return Task.FromResult(r);
        }

        public Task<RespuestaDgi> EnviarReporteDiarioAsync(string reporteFirmadoXml, CancellationToken ct = default)
        {
            IdsEnvio.Add(Id(reporteFirmadoXml, "IDEmisor"));
            Reportes.Add(reporteFirmadoXml);
            return Task.FromResult(RespuestaDgi.Exito("AR", "OK"));
        }

        public Task<RespuestaDgi> ConsultarEstadoEnvioAsync(string idReceptor, string token, CancellationToken ct = default)
        {
            var r = RespuestaDgi.Exito("AE", "OK");
            r.Comprobantes = EstadoEnvio;
            return Task.FromResult(r);
        }

        public Task<RespuestaDgi> ConsultarEstadoCfeAsync(int tipoCfe, string serie, long numero, CancellationToken ct = default) =>
            Task.FromResult(RespuestaDgi.Exito("AE", "OK"));

        public IDgiSoapClient WithHttpClient(HttpClient httpClient) => this;
        public void Dispose() { }
    }

    // -----------------------------------------------------------------------
    // EfCaeRepository
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReservarProximoNumero_VariasInstanciasEnParalelo_NoRepitenNumeros()
    {
        await RepoCae().GuardarCaeAsync(NuevoCae());
        var instancias = Enumerable.Range(0, 8).Select(_ => RepoCae()).ToList();

        var numeros = await Task.WhenAll(instancias.SelectMany(repo => Enumerable.Range(0, 25).Select(_ =>
            Task.Run(async () => (await repo.ReservarProximoNumeroAsync(TipoCfe.ETicket, HoraUruguay.Hoy()))!.Value.Numero))));

        Assert.Equal(Enumerable.Range(1, 200).Select(n => (long)n), numeros.Order());
        Assert.Equal(200, (await RepoCae().CargarTodosAsync()).Single().UltimoNroUsado);
    }

    [Fact]
    public async Task ReservarProximoNumero_EmpiezaEnElInicioDelRango()
    {
        await RepoCae().GuardarCaeAsync(NuevoCae(desde: 1001, hasta: 2000));

        var reserva = await RepoCae().ReservarProximoNumeroAsync(TipoCfe.ETicket, HoraUruguay.Hoy());

        Assert.Equal(1001, reserva!.Value.Numero);
        Assert.Equal(1001, reserva.Value.Cae.UltimoNroUsado);
    }

    [Fact]
    public async Task ReservarProximoNumero_PrefiereElQueVenceMasTarde_EIgnoraVencidosYAgotados()
    {
        var hoy = HoraUruguay.Hoy();
        var repo = RepoCae();
        await repo.GuardarCaeAsync(NuevoCae("VENCIDO", vence: hoy.AddDays(-1)));
        await repo.GuardarCaeAsync(NuevoCae("AGOTADO", desde: 1, hasta: 10, vence: hoy.AddYears(3)));
        await repo.ActualizarUltimoNroUsadoAsync("AGOTADO", 10);
        await repo.GuardarCaeAsync(NuevoCae("CERCANO", desde: 1, hasta: 100, vence: hoy.AddDays(30)));
        await repo.GuardarCaeAsync(NuevoCae("LEJANO", desde: 101, hasta: 200, vence: hoy.AddYears(2)));

        var reserva = await repo.ReservarProximoNumeroAsync(TipoCfe.ETicket, hoy);

        Assert.Equal("LEJANO", reserva!.Value.Cae.NroSerie);
        Assert.Equal(101, reserva.Value.Numero);
    }

    [Fact]
    public async Task ReservarProximoNumero_SinCaeDisponible_DevuelveNull()
    {
        await RepoCae().GuardarCaeAsync(NuevoCae());
        Assert.Null(await RepoCae().ReservarProximoNumeroAsync(TipoCfe.EFactura, HoraUruguay.Hoy()));
    }

    [Fact]
    public async Task GuardarCae_ExistenteConContadorMenor_ActualizaDatosSinRetrocederElContador()
    {
        var repo = RepoCae();
        await repo.GuardarCaeAsync(NuevoCae());
        for (var i = 0; i < 5; i++)
            await repo.ReservarProximoNumeroAsync(TipoCfe.ETicket, HoraUruguay.Hoy());

        // Otra instancia vuelve a registrar el CAE con un contador viejo y nueva fecha de vencimiento.
        var vence = HoraUruguay.Hoy().AddYears(2);
        await RepoCae().GuardarCaeAsync(NuevoCae(vence: vence));

        var guardado = (await repo.CargarTodosAsync()).Single();
        Assert.Equal(5, guardado.UltimoNroUsado);
        Assert.Equal(vence, guardado.FechaVencimiento);
    }

    [Fact]
    public async Task ActualizarUltimoNroUsado_NoRetrocede_YLanzaSiNoExiste()
    {
        var repo = RepoCae();
        await repo.GuardarCaeAsync(NuevoCae());
        await repo.ActualizarUltimoNroUsadoAsync("CAE-1", 20);
        await repo.ActualizarUltimoNroUsadoAsync("CAE-1", 15);

        Assert.Equal(20, (await repo.CargarTodosAsync()).Single().UltimoNroUsado);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => repo.ActualizarUltimoNroUsadoAsync("NO-EXISTE", 1).AsTask());
    }

    [Fact]
    public async Task ReservarIdEnvio_VariasInstanciasEnParalelo_SonUnicosYDeHasta10Digitos()
    {
        var instancias = Enumerable.Range(0, 5).Select(_ => RepoCae()).ToList();

        var ids = await Task.WhenAll(instancias.SelectMany(repo => Enumerable.Range(0, 20).Select(_ =>
            Task.Run(async () => await repo.ReservarIdEnvioAsync()))));

        Assert.Equal(100, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange(id, 1, 9_999_999_999));
    }

    [Fact]
    public async Task ReservarProximoNumero_IgnoraCaeConRangoInvertido()
    {
        await RepoCae().GuardarCaeAsync(NuevoCae(desde: 500, hasta: 100));
        Assert.Null(await RepoCae().ReservarProximoNumeroAsync(TipoCfe.ETicket, HoraUruguay.Hoy()));
    }

    [Fact]
    public void MaxIntentos_MenorQueUno_LanzaArgumentOutOfRange() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EfCaeRepository<UruFacturaDbContext>(new Fabrica(_ruta)) { MaxIntentos = 0 });

    // -----------------------------------------------------------------------
    // Claves
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Claves_SonGuidV7OrdenadosPorTiempo()
    {
        var repo = RepoCae();
        await repo.GuardarCaeAsync(NuevoCae("CAE-1"));
        await Task.Delay(5);
        await repo.GuardarCaeAsync(NuevoCae("CAE-2"));

        using var db = Db();
        var ids = await db.Caes.OrderBy(c => c.NroSerie).Select(c => c.Id).ToListAsync();

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
    // EfCfeRepository
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GuardarCfe_FechaFirmaSeGuardaEnUtc()
    {
        var cfe = TestData.CfeValido(TipoCfe.ETicket, 4);
        cfe.CalcularTotales();
        cfe.FechaFirma = new DateTimeOffset(2026, 6, 15, 21, 30, 0, HoraUruguay.Offset);

        await RepoCfe().GuardarAsync([cfe]);

        using var db = Db();
        var fila = await db.Cfes.SingleAsync();
        Assert.Equal(TimeSpan.Zero, fila.FechaFirma!.Value.Offset); // Npgsql rechaza offsets distintos de 0
        Assert.Equal(cfe.FechaFirma, fila.FechaFirma);
    }

    [Fact]
    public async Task GuardarCfe_ActualizaPorTipoSerieNumero_SinBorrarElXmlConUnCfeReconstruido()
    {
        var cfe = TestData.CfeValido(TipoCfe.ETicket, 3);
        cfe.CalcularTotales();
        cfe.XmlFirmado = "<CFE>firmado</CFE>";
        await RepoCfe().GuardarAsync([cfe]);

        var reconstruido = (await RepoCfe().ObtenerPorFechaEmisionAsync(cfe.FechaEmision)).Single();
        reconstruido.XmlFirmado = null;
        reconstruido.CodigoRespuestaDgi = "AE";
        reconstruido.AceptadoPorDgi = true;
        await RepoCfe().GuardarAsync([reconstruido]);

        using var db = Db();
        var fila = await db.Cfes.SingleAsync();
        Assert.Equal("<CFE>firmado</CFE>", fila.XmlFirmado);
        Assert.Equal("AE", fila.CodigoRespuestaDgi);
        Assert.True(fila.AceptadoPorDgi);
        Assert.Equal(cfe.MontoTotal, fila.MontoTotal);
        Assert.Equal(244m, fila.MontoTotal);
    }

    [Fact]
    public async Task GuardarCfe_SinNumeroAsignado_LanzaArgumentException()
    {
        var cfe = new Models.Cfe { Tipo = TipoCfe.ETicket };
        await Assert.ThrowsAsync<ArgumentException>(() => RepoCfe().GuardarAsync([cfe]).AsTask());
    }

    // -----------------------------------------------------------------------
    // UruFacturaClient con varias instancias
    // -----------------------------------------------------------------------

    [Fact]
    public async Task VariasInstancias_EmitenEnParalelo_YElReporteDiarioIncluyeLosDeTodas()
    {
        var soap = new SoapStub();
        await RepoCae().GuardarCaeAsync(NuevoCae());
        var instancias = Enumerable.Range(0, 4).Select(_ => NuevaInstancia(soap)).ToList();

        await Task.WhenAll(instancias.SelectMany(cliente => Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var cfe = cliente.CrearETicket();
            cfe.Detalle.Add(new LineaDetalle { NombreItem = "Servicio", Cantidad = 2, PrecioUnitario = 100m });
            await cliente.AsignarCaeAsync(cfe);
            await cliente.EnviarCfeAsync(cfe);
        }))));

        // El reporte lo envía otra instancia (p. ej. un job nocturno) que no emitió ningún CFE.
        using var job = NuevaInstancia(soap);
        var r = await job.EnviarReporteDiarioAsync(HoraUruguay.Hoy());

        Assert.Equal(40, r.CantidadCfe);
        var reporte = soap.Reportes.Single();
        Assert.Empty(TestData.ValidarContraXsd(reporte));
        Assert.Contains("<CantDocsUtil>40</CantDocsUtil>", reporte);
        Assert.Contains("<NroDesde>1</NroDesde><NroHasta>40</NroHasta>", reporte);
        Assert.Contains("<TotMntTotal>9760.00</TotMntTotal>", reporte); // 40 × (200 + 22 % de IVA)
        Assert.Equal(41, soap.IdsEnvio.Distinct().Count()); // 40 sobres y el reporte, sin repetir Idemisor

        using var db = Db();
        var filas = await db.Cfes.ToListAsync();
        Assert.Equal(40, filas.Count);
        Assert.All(filas, f =>
        {
            Assert.False(string.IsNullOrEmpty(f.XmlFirmado));
            Assert.Equal("tok", f.TokenDgi);
        });

        foreach (var cliente in instancias)
            cliente.Dispose();
    }

    [Fact]
    public async Task ConsultarEstadoEnvio_ActualizaElEstadoEnLaBase()
    {
        var soap = new SoapStub();
        await RepoCae().GuardarCaeAsync(NuevoCae());
        using var cliente = NuevaInstancia(soap);
        var cfe = cliente.CrearETicket();
        cfe.Detalle.Add(new LineaDetalle { NombreItem = "Servicio", Cantidad = 1, PrecioUnitario = 100m });
        await cliente.AsignarCaeAsync(cfe);
        var envio = await cliente.EnviarCfeAsync(cfe);
        soap.EstadoEnvio.Add(new EstadoComprobante { TipoCfe = 101, Serie = "A", Numero = cfe.Numero, Estado = "AE" });

        // Otra instancia consulta el estado partiendo de lo guardado en la base.
        using var otra = NuevaInstancia(soap);
        var guardados = await RepoCfe().ObtenerPorFechaEmisionAsync(cfe.FechaEmision);
        await otra.ConsultarEstadoEnvioAsync(envio, guardados);

        using var db = Db();
        Assert.True((await db.Cfes.SingleAsync()).AceptadoPorDgi);
    }

    [Fact]
    public void AsignarCaeSincronico_ConRepositorioCompartido_LanzaInvalidOperation()
    {
        using var cliente = NuevaInstancia(new SoapStub());
        Assert.Throws<InvalidOperationException>(() => cliente.AsignarCae(cliente.CrearETicket()));
    }

    [Fact]
    public async Task AsignarCaeAsync_ConRepositorioCompartidoSinCae_LanzaCaeException()
    {
        using var cliente = NuevaInstancia(new SoapStub());
        await Assert.ThrowsAsync<CaeException>(() => cliente.AsignarCaeAsync(cliente.CrearETicket()));
    }
}
