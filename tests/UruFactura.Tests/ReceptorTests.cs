using UruFactura.Enums;
using UruFactura.Models;
using Xunit;

namespace UruFactura.Tests;

public class ReceptorTests
{
    [Fact]
    public void TipoDocumentoReceptor_Rut_EsValorDos()
    {
        Assert.Equal(2, (int)TipoDocumentoReceptor.Rut);
    }

    [Fact]
    public void TipoDocumentoReceptor_CedulaIdentidad_EsValorTres()
    {
        Assert.Equal(3, (int)TipoDocumentoReceptor.CedulaIdentidad);
    }

    [Fact]
    public void TipoDocumentoReceptor_Otro_EsValorCuatro()
    {
        Assert.Equal(4, (int)TipoDocumentoReceptor.Otro);
    }

    [Fact]
    public void TipoDocumentoReceptor_Pasaporte_EsValorCinco()
    {
        Assert.Equal(5, (int)TipoDocumentoReceptor.Pasaporte);
    }

    [Fact]
    public void TipoDocumentoReceptor_DocumentoExterior_EsAliasDeOtro()
    {
        Assert.Equal((int)TipoDocumentoReceptor.Otro, (int)TipoDocumentoReceptor.DocumentoExterior);
    }

    [Fact]
    public void Receptor_TipoDocumento_PorDefecto_EsRut()
    {
        var receptor = new Receptor();
        Assert.Equal(TipoDocumentoReceptor.Rut, receptor.TipoDocumento);
    }

    [Fact]
    public void Receptor_TipoDocumento_PermiteAsignarEnum()
    {
        var receptor = new Receptor
        {
            Documento     = "12345678",
            TipoDocumento = TipoDocumentoReceptor.Pasaporte,
            RazonSocial   = "Juan Pérez",
        };
        Assert.Equal(TipoDocumentoReceptor.Pasaporte, receptor.TipoDocumento);
    }
}
