namespace UruFactura.Tests;

public class UruFacturaConfigTests
{
    [Fact]
    public void ConfigValida_NoTieneErrores() => Assert.Empty(TestData.Config().Validar());

    [Fact]
    public void ConRutaDeCertificado_EsValida() =>
        Assert.Empty((TestData.Config() with { Certificado = null, RutaCertificado = "/c.p12" }).Validar());

    [Fact]
    public void SinCertificado_TieneError() =>
        Assert.Contains((TestData.Config() with { Certificado = null }).Validar(), e => e.Contains("certificado"));

    [Theory]
    [InlineData("")]
    [InlineData("210000000012")] // dígito verificador inválido
    public void RutEmisorInvalido_TieneError(string rut) =>
        Assert.Contains((TestData.Config() with { Emisor = TestData.Emisor() with { Rut = rut } }).Validar(), e => e.Contains("Emisor.Rut"));

    [Theory]
    [InlineData(nameof(Emisor.RazonSocial))]
    [InlineData(nameof(Emisor.DomicilioFiscal))]
    [InlineData(nameof(Emisor.Ciudad))]
    [InlineData(nameof(Emisor.Departamento))]
    public void DatosObligatoriosDelEmisor(string campo)
    {
        var emisor = campo switch
        {
            nameof(Emisor.RazonSocial) => TestData.Emisor() with { RazonSocial = "" },
            nameof(Emisor.DomicilioFiscal) => TestData.Emisor() with { DomicilioFiscal = "" },
            nameof(Emisor.Ciudad) => TestData.Emisor() with { Ciudad = "" },
            _ => TestData.Emisor() with { Departamento = "" },
        };

        Assert.Contains((TestData.Config() with { Emisor = emisor }).Validar(), e => e.Contains(campo));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public void CodigoSucursalFueraDeRango_TieneError(int sucursal) =>
        Assert.Contains((TestData.Config() with { Emisor = TestData.Emisor() with { CodigoSucursal = sucursal } }).Validar(),
            e => e.Contains("CodigoSucursal"));

    [Fact]
    public void MasDeDosTelefonos_TieneError() =>
        Assert.Contains((TestData.Config() with { Emisor = TestData.Emisor() with { Telefonos = ["1", "2", "3"] } }).Validar(),
            e => e.Contains("Telefonos"));

    [Fact]
    public void NombreComercialMuyLargo_TieneError() =>
        Assert.Contains((TestData.Config() with { Emisor = TestData.Emisor() with { NombreComercial = new string('x', 31) } }).Validar(),
            e => e.Contains("NombreComercial"));

    [Fact]
    public void Produccion_SinUrls_TieneError() =>
        Assert.Contains((TestData.Config() with { Ambiente = Ambiente.Produccion }).Validar(), e => e.Contains("UrlEnvio"));

    [Fact]
    public void Produccion_SinValidarElCertificadoTls_TieneError() =>
        Assert.Contains((TestData.Config() with
        {
            Ambiente = Ambiente.Produccion, UrlEnvio = "https://e", UrlConsultas = "https://c", OmitirValidacionSsl = true,
        }).Validar(), e => e.Contains("OmitirValidacionSsl"));

    [Fact]
    public void Homologacion_SinValidarElCertificadoTls_EsValida() =>
        Assert.Empty((TestData.Config() with { OmitirValidacionSsl = true }).Validar());

    [Fact]
    public void EmisorConCaracterDeControl_TieneError() =>
        Assert.Contains((TestData.Config() with { Emisor = TestData.Emisor() with { RazonSocial = "Empresa\u0001" } }).Validar(),
            e => e.Contains("Emisor.RazonSocial"));

    [Fact]
    public void Produccion_ConUrls_EsValida() =>
        Assert.Empty((TestData.Config() with { Ambiente = Ambiente.Produccion, UrlEnvio = "https://e", UrlConsultas = "https://c" }).Validar());

    [Fact]
    public void Homologacion_SinUrls_UsaLosServiciosDePruebaDeLaDgi()
    {
        var config = TestData.Config();

        Assert.Equal("https://efactura.dgi.gub.uy:6443/ePrueba/ws_eprueba", config.UrlEnvioEfectiva);
        Assert.Equal("https://efactura.dgi.gub.uy:6460/ePrueba/ws_consultasPrueba", config.UrlConsultasEfectiva);
    }

    [Fact]
    public void UrlsConfiguradas_TienenPrioridad()
    {
        var config = TestData.Config() with { UrlEnvio = "https://e", UrlConsultas = "https://c" };

        Assert.Equal("https://e", config.UrlEnvioEfectiva);
        Assert.Equal("https://c", config.UrlConsultasEfectiva);
    }

    [Fact]
    public void ValoresPorDefecto()
    {
        var config = new UruFacturaConfig();

        Assert.Equal(Ambiente.Homologacion, config.Ambiente);
        Assert.Equal(TimeSpan.FromSeconds(30), config.Timeout);
        Assert.Equal(AlgoritmoFirma.Sha256, config.AlgoritmoFirma);
        Assert.False(config.OmitirValidacionSsl);
        Assert.Equal(1, config.Emisor.CodigoSucursal);
    }

    [Fact]
    public void ToString_NoMuestraLaContrasena()
    {
        var texto = (TestData.Config() with { Certificado = null, RutaCertificado = "/c.p12", PasswordCertificado = "s3cr3t" }).ToString();

        Assert.DoesNotContain("s3cr3t", texto);
        Assert.Contains("219999820013", texto);
    }

    [Fact]
    public void ValorUnidadIndexadaNoPositivo_TieneError() =>
        Assert.Contains((TestData.Config() with { ValorUnidadIndexada = 0 }).Validar(), e => e.Contains("ValorUnidadIndexada"));
}
