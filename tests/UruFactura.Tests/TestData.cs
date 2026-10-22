using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Schema;
using UruFactura.Xml;

namespace UruFactura.Tests;

/// <summary>Datos y utilidades compartidas por los tests.</summary>
internal static class TestData
{
    internal static readonly DateOnly Dia = new(2026, 6, 15);

    /// <summary>Certificado de prueba compartido (no se libera: vive lo que el proceso de tests).</summary>
    internal static readonly X509Certificate2 Certificado = CertificadoAutofirmado();

    internal static Emisor Emisor() => new()
    {
        Rut = "219999820013",
        RazonSocial = "Empresa Test S.A.",
        DomicilioFiscal = "Av. 18 de Julio 1234",
        Ciudad = "Montevideo",
        Departamento = "Montevideo",
    };

    internal static UruFacturaConfig Config() => new() { Emisor = Emisor(), Certificado = Certificado };

    internal static Cae Cae(
        TipoCfe tipo = TipoCfe.ETicket, string serie = "A", long desde = 1, long hasta = 100,
        DateOnly? vence = null, long ultimo = 0) => new()
    {
        NumeroAutorizacion = "90110001945",
        TipoCfe = tipo,
        Serie = serie,
        RangoDesde = desde,
        RangoHasta = hasta,
        FechaVencimiento = vence ?? new DateOnly(2040, 12, 31),
        UltimoNroUsado = ultimo,
    };

    internal static LineaDetalle Linea(decimal cantidad = 2, decimal precio = 100m, TipoIva iva = TipoIva.Basico) => new()
    {
        NombreItem = "Servicio de consultoría",
        UnidadMedida = "UN",
        Cantidad = cantidad,
        PrecioUnitario = precio,
        IndFactIva = iva,
    };

    /// <summary>Un CFE válido del tipo indicado (con receptor, datos de exportación, remito, mandante o referencias si aplica).</summary>
    internal static Cfe CfeValido(TipoCfe tipo = TipoCfe.ETicket)
    {
        var cfe = new Cfe(tipo) { FechaEmision = Dia, Detalle = [Linea()] };
        var familia = tipo.Familia();

        if (familia == FamiliaCfe.Factura)
            cfe = cfe with { Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A." } };

        if (tipo.EsExportacion())
            cfe = cfe with
            {
                Receptor = new Receptor
                {
                    Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte,
                    RazonSocial = "Importer Inc.", CodigoPais = "US", Pais = "Estados Unidos",
                },
                ClausulaVenta = familia == FamiliaCfe.RemitoExportacion ? null : "FOB",
                ModalidadVenta = 1,
                ViaTransporte = 1,
                Moneda = Moneda.DolarAmericano,
                TipoCambio = 40.5m,
            };

        if (tipo.EsRemito())
            cfe = cfe with { IndTraslado = IndTraslado.TrasladoPorVenta };

        if (familia == FamiliaCfe.Resguardo)
            cfe = cfe with
            {
                Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." },
                Detalle =
                [
                    new LineaDetalle
                    {
                        Retenciones =
                        [
                            new RetencionPercepcion { Codigo = "R1", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m },
                            new RetencionPercepcion { Codigo = "R2", MontoSujeto = 500m, Valor = 25m, InfoAdicional = "detalle" },
                        ],
                    },
                ],
            };

        if (tipo.EsCuentaAjena())
            cfe = cfe with { Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A." } };

        if (tipo.EsNotaCorreccion())
            cfe = cfe with
            {
                Referencias =
                [
                    new RefCfe
                    {
                        TipoCfe = familia switch
                        {
                            FamiliaCfe.Factura => TipoCfe.EFactura,
                            FamiliaCfe.FacturaExportacion => TipoCfe.EFacturaExportacion,
                            _ => TipoCfe.ETicket,
                        },
                        Serie = "A",
                        NroCfe = 10,
                        FechaCfe = new DateOnly(2026, 6, 1),
                        MontoCfeRef = 500m,
                        MonedaCfeRef = cfe.Moneda,
                        TipoCambioCfeRef = cfe.Moneda == Moneda.PesoUruguayo ? null : cfe.TipoCambio,
                        Razon = "Corrección",
                    },
                ],
            };

        return cfe;
    }

    /// <summary>Errores de validación del contenido (sin tope de UI).</summary>
    internal static List<string> Validar(Cfe cfe, decimal? tope = null) => cfe.Validar(cfe.FechaEmision ?? Dia, tope);

    /// <summary>
    /// Emite el CFE con el mismo proceso que el cliente (XML y firma reales), con el número y el CAE
    /// indicados. Para tests que necesitan un <see cref="CfeEmitido"/> sin pasar por el almacenamiento.
    /// </summary>
    internal static CfeEmitido Emitido(Cfe? cfe = null, long numero = 1, Cae? cae = null, DateTimeOffset? firma = null)
    {
        cfe ??= CfeValido();
        var documento = cfe with { FechaEmision = cfe.FechaEmision ?? Dia, Emisor = cfe.Emisor ?? Emisor() };
        var errores = Validar(documento);
        if (errores.Count > 0)
            throw new CfeValidationException(errores);

        var reservado = (cae ?? Cae(cfe.Tipo)) with { UltimoNroUsado = numero };
        var totales = documento.CalcularTotales();
        var tmst = firma ?? new DateTimeOffset(2026, 6, 15, 12, 0, 0, HoraUruguay.Offset);
        var (xml, digest) = FirmaDigital.Firmar(CfeXml.Generar(documento, reservado, numero, totales, tmst), Certificado, AlgoritmoFirma.Sha256);
        return new CfeEmitido(documento, reservado, numero, totales, tmst, digest, xml);
    }

    /// <summary>Envuelve CFE emitidos en un sobre (para validarlos contra EnvioCFE.xsd, que exige carátula y firma).</summary>
    internal static string Sobre(params CfeEmitido[] cfes) =>
        CfeSobre.Generar("219999820013", 1, DateTimeOffset.UtcNow, Certificado.RawData, [.. cfes.Select(c => c.Xml)]);

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

    /// <summary>Valida <paramref name="xml"/> contra los XSD oficiales de la DGI (v1.44.2). Devuelve los errores.</summary>
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

    internal static void AssertValidoContraXsd(string xml)
    {
        var errores = ValidarContraXsd(xml);
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }
}

/// <summary>Reloj fijo para simular la hora del servidor.</summary>
internal sealed class RelojFijo(DateTimeOffset utc) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utc;
}
