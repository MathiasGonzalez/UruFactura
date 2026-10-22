namespace UruFactura.Tests;

public class ReceptorTests
{
    [Theory]
    [InlineData(TipoDocumentoReceptor.Rut, 2)]
    [InlineData(TipoDocumentoReceptor.CedulaIdentidad, 3)]
    [InlineData(TipoDocumentoReceptor.Otro, 4)]
    [InlineData(TipoDocumentoReceptor.Pasaporte, 5)]
    [InlineData(TipoDocumentoReceptor.Dni, 6)]
    [InlineData(TipoDocumentoReceptor.Nife, 7)]
    public void TipoDocumentoReceptor_TieneElCodigoDeLaDgi(TipoDocumentoReceptor tipo, int codigo) =>
        Assert.Equal(codigo, (int)tipo);

    [Fact]
    public void Receptor_PorDefecto_EsRutUruguayo()
    {
        var receptor = new Receptor();

        Assert.Equal(TipoDocumentoReceptor.Rut, receptor.TipoDocumento);
        Assert.Equal("UY", receptor.CodigoPais);
    }
}
