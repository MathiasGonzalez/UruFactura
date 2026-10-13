using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Schema;
using UruFactura.Enums;
using UruFactura.Models;

namespace UruFactura.Tests;

/// <summary>Datos y utilidades compartidas por los tests.</summary>
internal static class TestData
{
    internal static CaeData CaeValido() => new()
    {
        NumeroAutorizacion = "90110001945",
        RangoDesde = 1,
        RangoHasta = 100,
        FechaVencimiento = new DateOnly(2040, 12, 31),
    };

    /// <summary>Un CFE completo y válido del tipo indicado (con CAE, serie y datos de exportación/remito si aplica).</summary>
    internal static Cfe CfeValido(TipoCfe tipo = TipoCfe.ETicket, long numero = 1)
    {
        var cfe = new Cfe
        {
            Tipo = tipo,
            Serie = "A",
            Numero = numero,
            FechaEmision = new DateOnly(2026, 6, 15),
            RutEmisor = "219999820013",
            RazonSocialEmisor = "Empresa Test S.A.",
            DomicilioFiscalEmisor = "Av. 18 de Julio 1234",
            CiudadEmisor = "Montevideo",
            DepartamentoEmisor = "Montevideo",
            CodigoSucursal = 1,
            CaeData = CaeValido(),
            FormaPago = FormaPago.Contado,
            Moneda = Moneda.PesoUruguayo,
            Detalle =
            {
                new LineaDetalle
                {
                    NroLinea = 1,
                    NombreItem = "Servicio de consultoría",
                    UnidadMedida = "UN",
                    Cantidad = 2,
                    PrecioUnitario = 100m,
                    IndFactIva = TipoIva.Basico,
                },
            },
        };

        var familia = tipo.Familia();

        if (familia == FamiliaCfe.Factura)
            cfe.Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A.", TipoDocumento = TipoDocumentoReceptor.Rut };

        if (tipo.EsExportacion())
        {
            cfe.Receptor = new Receptor
            {
                Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte,
                RazonSocial = "Importer Inc.", CodigoPais = "US", Pais = "Estados Unidos",
            };
            cfe.ClausulaVenta = familia == FamiliaCfe.RemitoExportacion ? null : "FOB";
            cfe.ModalidadVenta = 1;
            cfe.ViaTransporte = 1;
            cfe.Moneda = Moneda.DolarAmericano;
            cfe.TipoCambio = 40.5m;
        }

        if (tipo.EsRemito())
            cfe.IndTraslado = IndTraslado.TrasladoPorVenta;

        if (familia == FamiliaCfe.Resguardo)
        {
            cfe.Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A.", TipoDocumento = TipoDocumentoReceptor.Rut };
            cfe.Detalle.Clear();
            cfe.Detalle.Add(new LineaDetalle
            {
                Retenciones =
                {
                    new RetencionPercepcion { Codigo = "R1", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m },
                    new RetencionPercepcion { Codigo = "R2", MontoSujeto = 500m, Valor = 25m, InfoAdicional = "detalle" },
                },
            });
        }

        if (tipo.EsCuentaAjena())
            cfe.Mandante = new Mandante { TipoDocumento = TipoDocumentoReceptor.Rut, CodigoPais = "UY", Documento = "214844360018", Nombre = "Mandante S.A." };

        if (tipo.EsNotaCorreccion())
        {
            var referenciado = familia switch
            {
                FamiliaCfe.Factura => TipoCfe.EFactura,
                FamiliaCfe.FacturaExportacion => TipoCfe.EFacturaExportacion,
                _ => TipoCfe.ETicket,
            };
            cfe.Referencias.Add(new RefCfe
            {
                TipoCfe = referenciado,
                Serie = "A",
                NroCfe = 10,
                FechaCfe = new DateTime(2026, 6, 1),
                MontoCfeRef = 500m,
                MonedaCfeRef = cfe.Moneda,
                TipoCambioCfeRef = cfe.Moneda == Moneda.PesoUruguayo ? null : cfe.TipoCambio,
                Razon = "Corrección",
            });
        }

        return cfe;
    }

    /// <summary>Certificado autofirmado RSA con clave privada (solo para tests).</summary>
    internal static X509Certificate2 CertificadoAutofirmado()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=UruFacturaTest", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

        // Exportar e importar para que HasPrivateKey sea true en todos los runtimes (macOS no admite EphemeralKeySet).
        return X509CertificateLoader.LoadPkcs12(
            cert.Export(X509ContentType.Pfx), null,
            OperatingSystem.IsMacOS()
                ? X509KeyStorageFlags.DefaultKeySet | X509KeyStorageFlags.Exportable
                : X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.Exportable);
    }

    private static readonly Lazy<XmlSchemaSet> Schemas = new(() => CargarSchemas("EnvioCFE.xsd", "ReporteDiarioCFE.xsd", "ACKSobre.xsd", "ACKCFE_DGI.xsd", "ACKRepDiario.xsd"));

    private static XmlSchemaSet CargarSchemas(params string[] archivos)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Schemas", "dgi");
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        foreach (var archivo in archivos)
        {
            // EnvioCFE/ReporteDiarioCFE/ACK* comparten el namespace cfe.dgi.gub.uy: se agregan
            // por separado (todos incluyen DGITypes.xsd) y se compilan juntos.
            using var reader = XmlReader.Create(Path.Combine(dir, archivo), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, XmlResolver = new XmlUrlResolver(),
            });
            set.Add(null, reader);
        }
        set.Compile();
        return set;
    }

    /// <summary>
    /// Valida <paramref name="xml"/> contra los XSD oficiales de la DGI (v1.44.2).
    /// Devuelve la lista de errores (vacía si es válido).
    /// </summary>
    internal static List<string> ValidarContraXsd(string xml)
    {
        var errores = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Schemas.Value,
            DtdProcessing = DtdProcessing.Prohibit,
        };
        settings.ValidationEventHandler += (_, e) => errores.Add($"{e.Severity}: {e.Message}");

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read()) { }
        return errores;
    }
}
