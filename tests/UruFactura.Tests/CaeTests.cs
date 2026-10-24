namespace UruFactura.Tests;

/// <summary>Modelo del CAE: datos, vigencia, uso, elección y advertencias.</summary>
public class CaeTests
{
    private static readonly DateOnly Hoy = new(2026, 6, 15);

    [Fact]
    public void Valido_NoTieneErrores() => Assert.Empty(TestData.Cae().Validar());

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("ABC")]
    [InlineData("0A")]
    [InlineData("A1")]
    public void SerieInvalida_TieneError(string serie) =>
        Assert.Contains(TestData.Cae(serie: serie).Validar(), e => e.Contains("serie"));

    [Theory]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("1A")]
    public void SerieValida(string serie) => Assert.Empty(TestData.Cae(serie: serie).Validar());

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 5)]
    [InlineData(1, 10_000_000)]
    public void RangoInvalido_TieneError(long desde, long hasta) =>
        Assert.Contains(TestData.Cae(desde: desde, hasta: hasta).Validar(), e => e.Contains("rango"));

    [Fact]
    public void NumeroAutorizacionInvalido_TieneError() =>
        Assert.Contains((TestData.Cae() with { NumeroAutorizacion = "123456789012" }).Validar(), e => e.Contains("NumeroAutorizacion"));

    [Fact]
    public void SinFechaDeVencimiento_TieneError() =>
        Assert.Contains((TestData.Cae() with { FechaVencimiento = default }).Validar(), e => e.Contains("FechaVencimiento"));

    [Theory]
    [InlineData(0, 1001)]
    [InlineData(500, 1001)] // contador heredado por debajo del rango
    [InlineData(1001, 1002)]
    public void ProximoNumero_ArrancaEnRangoDesde(long ultimo, long esperado) =>
        Assert.Equal(esperado, TestData.Cae(desde: 1001, hasta: 2000, ultimo: ultimo).ProximoNumero);

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1500, 500)]
    [InlineData(2000, 0)]
    public void Disponibles(long ultimo, long esperado) =>
        Assert.Equal(esperado, TestData.Cae(desde: 1001, hasta: 2000, ultimo: ultimo).Disponibles);

    [Fact]
    public void PorcentajeUso() => Assert.Equal(50m, TestData.Cae(ultimo: 50).PorcentajeUso);

    [Fact]
    public void EsVigenteEn_HastaElDiaDeVencimiento()
    {
        var cae = TestData.Cae(vence: Hoy);

        Assert.True(cae.EsVigenteEn(Hoy));
        Assert.False(cae.EsVigenteEn(Hoy.AddDays(1)));
    }

    [Theory]
    [InlineData("A", 1, 10, true)]
    [InlineData("A", 10, 20, true)]
    [InlineData("A", 11, 20, false)]
    [InlineData("B", 1, 10, false)]
    public void SeSuperponeCon_MismoTipoYSerieConNumerosEnComun(string serie, long desde, long hasta, bool esperado) =>
        Assert.Equal(esperado, TestData.Cae(desde: 1, hasta: 10).SeSuperponeCon(TestData.Cae(serie: serie, desde: desde, hasta: hasta)));

    [Fact]
    public void SeSuperponeCon_OtroTipo_NoSeSuperpone() =>
        Assert.False(TestData.Cae().SeSuperponeCon(TestData.Cae(TipoCfe.EFactura)));

    [Fact]
    public void Elegir_PrefiereElQueVenceMasTarde_EIgnoraVencidosAgotadosYOtrosTipos()
    {
        var elegido = Cae.Elegir(
        [
            TestData.Cae(serie: "V", vence: Hoy.AddDays(-1)),
            TestData.Cae(serie: "G", hasta: 10, ultimo: 10, vence: Hoy.AddYears(3)),
            TestData.Cae(TipoCfe.EFactura, serie: "F", vence: Hoy.AddYears(5)),
            TestData.Cae(serie: "C", vence: Hoy.AddDays(30)),
            TestData.Cae(serie: "L", vence: Hoy.AddYears(2)),
        ], TipoCfe.ETicket, Hoy);

        Assert.Equal("L", elegido!.Serie);
    }

    [Fact]
    public void Elegir_AIgualVencimiento_PrefiereElDeMasNumerosLibres()
    {
        var elegido = Cae.Elegir(
        [
            TestData.Cae(serie: "P", ultimo: 95, vence: Hoy.AddDays(20)),
            TestData.Cae(serie: "M", ultimo: 10, vence: Hoy.AddDays(20)),
        ], TipoCfe.ETicket, Hoy);

        Assert.Equal("M", elegido!.Serie);
    }

    [Fact]
    public void Elegir_VenceHoy_SigueVigente() =>
        Assert.NotNull(Cae.Elegir([TestData.Cae(vence: Hoy)], TipoCfe.ETicket, Hoy));

    [Fact]
    public void Elegir_SinCae_DevuelveNull() => Assert.Null(Cae.Elegir([], TipoCfe.ETicket, Hoy));

    [Fact]
    public void Advertencia_PorVencer() =>
        Assert.Contains("vence en 3", TestData.Cae(vence: Hoy.AddDays(3)).ObtenerAdvertencia(Hoy, 7, 80m));

    [Fact]
    public void Advertencia_Vencido() =>
        Assert.Contains("venció", TestData.Cae(vence: Hoy.AddDays(-1)).ObtenerAdvertencia(Hoy, 7, 80m));

    [Fact]
    public void Advertencia_Agotado() =>
        Assert.Contains("no tiene números", TestData.Cae(hasta: 10, ultimo: 10).ObtenerAdvertencia(Hoy, 7, 80m));

    [Fact]
    public void Advertencia_UsoAlto() =>
        Assert.Contains("90", TestData.Cae(ultimo: 90).ObtenerAdvertencia(Hoy, 7, 80m));

    [Fact]
    public void Advertencia_SinProblemas_EsNull() => Assert.Null(TestData.Cae().ObtenerAdvertencia(Hoy, 7, 80m));
}
