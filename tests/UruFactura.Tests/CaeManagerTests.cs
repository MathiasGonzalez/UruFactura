using UruFactura.Enums;
using UruFactura.Exceptions;
using UruFactura.Models;
using Xunit;

namespace UruFactura.Tests;

public class CaeManagerTests
{
    private static Models.Cae CrearCaeVigente(
        TipoCfe tipo = TipoCfe.ETicket,
        long desde = 1,
        long hasta = 100,
        long ultimoUsado = 0,
        DateOnly? fechaVencimiento = null) =>
        new()
        {
            NroSerie = "CAE-001",
            TipoCfe = tipo,
            RangoDesde = desde,
            RangoHasta = hasta,
            FechaVencimiento = fechaVencimiento ?? HoraUruguay.Hoy().AddMonths(3),
            UltimoNroUsado = ultimoUsado,
        };

    private static Models.Cae CrearCaeVencido() =>
        new()
        {
            NroSerie = "CAE-OLD",
            TipoCfe = TipoCfe.ETicket,
            RangoDesde = 1,
            RangoHasta = 100,
            FechaVencimiento = HoraUruguay.Hoy().AddDays(-1),
            UltimoNroUsado = 0,
        };

    [Theory]
    [InlineData(0)]
    [InlineData(500)] // contador heredado por debajo del rango del CAE
    public void ObtenerProximoNumero_ContadorPorDebajoDelRango_EmpiezaEnRangoDesde(long ultimoUsado)
    {
        var cae = CrearCaeVigente(desde: 1001, hasta: 2000, ultimoUsado: ultimoUsado);
        Assert.Equal(1001, cae.ObtenerProximoNumero());
    }

    [Fact]
    public void RegistrarCae_YObtenerCaeActivo_RetornaCae()
    {
        var manager = new Cae.CaeManager();
        var cae = CrearCaeVigente();
        manager.RegistrarCae(cae);

        var activo = manager.ObtenerCaeActivo(TipoCfe.ETicket);
        Assert.NotNull(activo);
        Assert.Equal("CAE-001", activo.NroSerie);
    }

    [Fact]
    public void ObtenerCaeActivo_SinCaeRegistrado_RetornaNull()
    {
        var manager = new Cae.CaeManager();
        var activo = manager.ObtenerCaeActivo(TipoCfe.EFactura);
        Assert.Null(activo);
    }

    [Fact]
    public void ObtenerCaeActivo_CaeVencido_RetornaNull()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVencido());

        var activo = manager.ObtenerCaeActivo(TipoCfe.ETicket);
        Assert.Null(activo);
    }

    [Fact]
    public void ObtenerCaeActivo_PriorizaMayorFechaVencimiento()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVigente(
            desde: 1, hasta: 100, ultimoUsado: 10, fechaVencimiento: HoraUruguay.Hoy().AddDays(10)));
        manager.RegistrarCae(new Models.Cae
        {
            NroSerie = "CAE-MAS-LARGO",
            TipoCfe = TipoCfe.ETicket,
            RangoDesde = 1,
            RangoHasta = 20,
            UltimoNroUsado = 1,
            FechaVencimiento = HoraUruguay.Hoy().AddDays(30),
        });

        var activo = manager.ObtenerCaeActivo(TipoCfe.ETicket);
        Assert.NotNull(activo);
        Assert.Equal("CAE-MAS-LARGO", activo.NroSerie);
    }

    [Fact]
    public void ObtenerCaeActivo_MismaFechaPriorizaMasDisponibles()
    {
        var manager = new Cae.CaeManager();
        var mismaFecha = HoraUruguay.Hoy().AddDays(20);
        manager.RegistrarCae(new Models.Cae
        {
            NroSerie = "CAE-POCOS",
            TipoCfe = TipoCfe.ETicket,
            RangoDesde = 1,
            RangoHasta = 100,
            UltimoNroUsado = 95,
            FechaVencimiento = mismaFecha,
        });
        manager.RegistrarCae(new Models.Cae
        {
            NroSerie = "CAE-MUCHOS",
            TipoCfe = TipoCfe.ETicket,
            RangoDesde = 1,
            RangoHasta = 100,
            UltimoNroUsado = 10,
            FechaVencimiento = mismaFecha,
        });

        var activo = manager.ObtenerCaeActivo(TipoCfe.ETicket);
        Assert.NotNull(activo);
        Assert.Equal("CAE-MUCHOS", activo.NroSerie);
    }

    [Fact]
    public void ObtenerProximoNumero_PrimerUso_RetornaRangoDesde()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVigente(desde: 500));

        var (_, numero) = manager.ObtenerProximoNumero(TipoCfe.ETicket);
        Assert.Equal(500, numero);
    }

    [Fact]
    public void ObtenerProximoNumero_SegundoUso_Incrementa()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVigente(desde: 1, ultimoUsado: 5));

        var (_, numero) = manager.ObtenerProximoNumero(TipoCfe.ETicket);
        Assert.Equal(6, numero);
    }

    [Fact]
    public void ObtenerProximoNumero_SinCae_LanzaCaeException()
    {
        var manager = new Cae.CaeManager();
        Assert.Throws<CaeException>(() => manager.ObtenerProximoNumero(TipoCfe.EFactura));
    }

    [Fact]
    public void ObtenerProximoNumero_RangoAgotado_LanzaCaeException()
    {
        var manager = new Cae.CaeManager();
        // CAE con rango 1-5, ya usados todos
        manager.RegistrarCae(CrearCaeVigente(desde: 1, hasta: 5, ultimoUsado: 5));

        Assert.Throws<CaeException>(() => manager.ObtenerProximoNumero(TipoCfe.ETicket));
    }

    [Fact]
    public void ObtenerAdvertencias_CaePorVencer_RetornaAdvertencia()
    {
        var manager = new Cae.CaeManager();
        var cae = new Models.Cae
        {
            NroSerie = "CAE-VENCE",
            TipoCfe = TipoCfe.ETicket,
            RangoDesde = 1,
            RangoHasta = 100,
            FechaVencimiento = HoraUruguay.Hoy().AddDays(3), // vence en 3 días
            UltimoNroUsado = 0,
        };
        manager.RegistrarCae(cae);

        var advertencias = manager.ObtenerAdvertencias(diasAlertaVencimiento: 7);
        Assert.NotEmpty(advertencias);
        Assert.Contains(advertencias, a => a.Contains("vence"));
    }

    [Fact]
    public void ObtenerAdvertencias_CaeSinProblemas_RetornaVacio()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVigente()); // vence en 3 meses, sin uso

        var advertencias = manager.ObtenerAdvertencias(diasAlertaVencimiento: 7, porcentajeAlertaUso: 80);
        Assert.Empty(advertencias);
    }

    [Fact]
    public void Cae_EsVigente_VigenteRetornaTrue()
    {
        var cae = CrearCaeVigente();
        Assert.True(cae.EsVigente);
    }

    [Fact]
    public void Cae_EsVigente_VencidoRetornaFalse()
    {
        var cae = CrearCaeVencido();
        Assert.False(cae.EsVigente);
    }

    [Fact]
    public void Cae_TieneNumerosDisponibles_RetornaTrue()
    {
        var cae = CrearCaeVigente(desde: 1, hasta: 100, ultimoUsado: 50);
        Assert.True(cae.TieneNumerosDisponibles);
    }

    [Fact]
    public void Cae_TieneNumerosDisponibles_AgotadoRetornaFalse()
    {
        var cae = CrearCaeVigente(desde: 1, hasta: 5, ultimoUsado: 5);
        Assert.False(cae.TieneNumerosDisponibles);
    }

    [Fact]
    public void Cae_PorcentajeUso_CalculaCorrectamente()
    {
        var cae = CrearCaeVigente(desde: 1, hasta: 100, ultimoUsado: 50);
        Assert.Equal(50m, cae.PorcentajeUso);
    }

    [Fact]
    public void Cae_ObtenerProximoNumero_VencidoLanzaExcepcion()
    {
        var cae = CrearCaeVencido();
        Assert.Throws<CaeException>(() => cae.ObtenerProximoNumero());
    }

    [Fact]
    public void ObtenerTodosLosCaes_RetornaTodos()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVigente(TipoCfe.ETicket));
        manager.RegistrarCae(CrearCaeVigente(TipoCfe.EFactura));

        var todos = manager.ObtenerTodosLosCaes();
        Assert.Equal(2, todos.Count);
    }

    [Fact]
    public void ResumenEstado_RetornaStringNoVacio()
    {
        var manager = new Cae.CaeManager();
        manager.RegistrarCae(CrearCaeVigente());
        var resumen = manager.ResumenEstado();
        Assert.NotEmpty(resumen);
        Assert.Contains("CAE-001", resumen);
    }
}
