using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace UruFactura.Tests;

/// <summary>
/// Prueba contra <see href="https://github.com/MathiasGonzalez/dgi-simulador">dgi-simulador</see>, que responde
/// como los servicios de la DGI. Se saltea si no está la variable <c>DGI_SIMULADOR_URL</c>
/// (por ejemplo <c>http://localhost:8080</c>); en CI la define <c>simulador-dgi.yml</c>.
/// </summary>
public sealed class SimuladorFactAttribute : FactAttribute
{
    public SimuladorFactAttribute()
    {
        if (SimuladorDgi.Url is null)
            Skip = "Sin DGI_SIMULADOR_URL: no hay simulador de la DGI.";
    }
}

/// <inheritdoc cref="SimuladorFactAttribute"/>
public sealed class SimuladorTheoryAttribute : TheoryAttribute
{
    public SimuladorTheoryAttribute()
    {
        if (SimuladorDgi.Url is null)
            Skip = "Sin DGI_SIMULADOR_URL: no hay simulador de la DGI.";
    }
}

/// <summary>
/// Un emisor dado de alta en el simulador, con su certificado de prueba y un CAE por tipo, y un
/// <see cref="UruFacturaClient"/> configurado con las URL del simulador.
/// </summary>
public sealed class SimuladorDgi : IAsyncLifetime
{
    internal static readonly string? Url = Environment.GetEnvironmentVariable("DGI_SIMULADOR_URL") is { Length: > 0 } url ? url.TrimEnd('/') : null;

    private static readonly int[] Tipos = [101, 102, 103, 111, 112, 113, 121, 151, 181, 182];

    internal HttpClient Admin { get; private set; } = null!;
    internal UruFacturaClient Cliente { get; private set; } = null!;
    internal string Rut { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (Url is null)
            return;
        Admin = new HttpClient { BaseAddress = new Uri(Url) };
        Rut = NuevoRut();
        var respuesta = await Admin.PostAsJsonAsync("/admin/emisores", new { rut = Rut, denominacion = "UruFactura CI S.A.", tipos = Tipos });
        respuesta.EnsureSuccessStatusCode();
        var alta = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        var certificado = X509CertificateLoader.LoadPkcs12(
            Convert.FromBase64String(alta.GetProperty("certificado").GetProperty("pfx").GetString()!), null, X509KeyStorageFlags.Exportable);

        Cliente = new UruFacturaClient(new UruFacturaConfig
        {
            Emisor = new Emisor
            {
                Rut = Rut, RazonSocial = "UruFactura CI S.A.", DomicilioFiscal = "Av. 18 de Julio 1234", Ciudad = "Montevideo", Departamento = "Montevideo",
            },
            Ambiente = Ambiente.Homologacion,
            Certificado = certificado,
            UrlEnvio = Url + "/ePrueba/ws_eprueba",
            UrlConsultas = Url + "/ePrueba/ws_consultasPrueba",
            UnidadIndexada = new Dictionary<int, decimal> { [HoraUruguay.Hoy().Year - 1] = 6.3m },
        });
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        foreach (var cae in alta.GetProperty("caes").EnumerateArray())
            await Cliente.RegistrarCaeAsync(cae.GetProperty("cae").Deserialize<Cae>(web)!);
    }

    public Task DisposeAsync()
    {
        Cliente?.Dispose();
        Admin?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Manda los CFE en un sobre y devuelve el resultado de la DGI, esperando hasta su <c>Fechahora</c>.</summary>
    internal async Task<EstadoEnvio> Enviar(params CfeEmitido[] cfes)
    {
        var acuse = await Cliente.EnviarAsync(cfes);
        Assert.True(acuse.Recibido, $"{acuse.Estado}: {string.Join("; ", acuse.Motivos)}");
        if (acuse.ConsultarDesde is { } desde && desde > DateTimeOffset.UtcNow)
            await Task.Delay(desde - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(200));
        var estado = await Cliente.ConsultarEnvioAsync(acuse);
        Assert.True(estado.Procesado, estado.Mensaje);
        return estado;
    }

    /// <summary>Los CFE aceptados, y sin advertencias del simulador: las reglas que la DGI no deja claras.</summary>
    internal async Task AssertAceptados(params CfeEmitido[] cfes)
    {
        var registrados = await Admin.GetFromJsonAsync<JsonElement>($"/admin/cfes?rut={Rut}");
        foreach (var cfe in cfes)
        {
            Assert.True(cfe.EstadoDgi == EstadoCfe.Aceptado, $"{cfe}: {cfe.EstadoDgi} {cfe.MensajeDgi}");
            var advertencias = registrados.EnumerateArray()
                .Where(r => r.GetProperty("tipo").GetInt32() == (int)cfe.Tipo && r.GetProperty("nro").GetInt64() == cfe.Numero)
                .SelectMany(r => r.GetProperty("advertencias").EnumerateArray().Select(a => a.GetString()))
                .ToList();
            Assert.True(advertencias.Count == 0, $"{cfe}: {string.Join("; ", advertencias)}");
        }
    }

    private static string NuevoRut()
    {
        while (true)
        {
            var rut = $"21{Random.Shared.Next(100_000, 1_000_000)}001";
            for (var dv = 0; dv <= 9; dv++)
                if (UruFactura.Rut.EsValido(rut + dv))
                    return rut + dv;
        }
    }
}

/// <summary>Cada tipo de CFE que emite UruFactura, aceptado por el simulador de la DGI.</summary>
[Trait("Categoria", "SimuladorDgi")]
public sealed class SimuladorDgiCfeTests(SimuladorDgi sim) : IClassFixture<SimuladorDgi>
{
    private static readonly Receptor Cliente = new()
    {
        Documento = "214844360018", RazonSocial = "Cliente S.A.", Direccion = "Av. Italia 1234", Ciudad = "Montevideo",
    };

    private static LineaDetalle Linea(string nombre, decimal cantidad, decimal precio, IndicadorFacturacion indicador = IndicadorFacturacion.TasaBasica) =>
        new() { NombreItem = nombre, Cantidad = cantidad, PrecioUnitario = precio, IndFact = indicador };

    private static readonly Dictionary<string, Cfe> Casos = new()
    {
        ["e-Ticket"] = new Cfe(TipoCfe.ETicket) { Detalle = [Linea("Servicio", 1, 1000m)] },
        ["e-Ticket con tasas mixtas"] = new Cfe(TipoCfe.ETicket)
        {
            Detalle = [Linea("Básica", 3, 120.5m), Linea("Exento", 1, 50m, IndicadorFacturacion.Exento), Linea("Mínima", 2, 10.33m, IndicadorFacturacion.TasaMinima)],
        },
        ["e-Ticket con entrega gratuita"] = new Cfe(TipoCfe.ETicket)
        {
            Detalle = [Linea("Servicio", 1, 100m), Linea("Regalo", 1, 0m, IndicadorFacturacion.EntregaGratuita)],
        },
        ["e-Factura"] = new Cfe(TipoCfe.EFactura) { Receptor = Cliente, Detalle = [Linea("Licencia", 1, 5000m)] },
        ["e-Factura en dólares"] = new Cfe(TipoCfe.EFactura)
        {
            Receptor = Cliente, Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m, Detalle = [Linea("Servicio", 2, 150m)],
        },
        ["e-Factura con IVA incluido"] = new Cfe(TipoCfe.EFactura)
        {
            Receptor = Cliente, MontosBrutos = MontosBrutos.IvaIncluido, Detalle = [Linea("Producto", 3, 122m), Linea("Otro", 7, 33.33m)],
        },
        ["e-Factura con percepción"] = new Cfe(TipoCfe.EFactura)
        {
            Receptor = Cliente,
            Detalle =
            [
                new LineaDetalle
                {
                    NombreItem = "Gasoil", Cantidad = 1000, PrecioUnitario = 50m, AgenteResponsable = AgenteResponsable.Producto,
                    Retenciones = [new RetencionPercepcion { Codigo = "2183219", MontoSujeto = 50_000m, Valor = 1_500m }],
                },
            ],
        },
        ["e-Factura con secreto profesional"] = new Cfe(TipoCfe.EFactura)
        {
            SecretoProfesional = true, Adenda = "Receptor reservado", Detalle = [Linea("Honorarios", 1, 3000m)],
        },
        ["e-Factura de Exportación"] = new Cfe(TipoCfe.EFacturaExportacion)
        {
            Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m, ClausulaVenta = "FOB", ModalidadVenta = 1, ViaTransporte = 1,
            Receptor = new Receptor
            {
                Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "US", RazonSocial = "Importer Inc.",
                Direccion = "5th Avenue 100", Ciudad = "New York", Departamento = "NY", Pais = "Estados Unidos",
            },
            Detalle =
            [
                new LineaDetalle
                {
                    NombreItem = "Carne", Ncm = "0201.30.00", Cantidad = 1, PrecioUnitario = 8000m, IndFact = IndicadorFacturacion.ExportacionYAsimiladas,
                },
            ],
        },
        ["e-Remito"] = new Cfe(TipoCfe.ERemito)
        {
            IndTraslado = IndTraslado.TrasladoPropio,
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Depósito S.A.", Direccion = "Ruta 1 km 20", Ciudad = "Ciudad del Plata" },
            Detalle = [new LineaDetalle { NombreItem = "Cajas", Cantidad = 50 }],
        },
        ["e-Resguardo"] = new Cfe(TipoCfe.EResguardo)
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." },
            Detalle = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "1146171", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m }] }],
        },
        ["e-Boleta"] = new Cfe(TipoCfe.EBoleta)
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Productor Rural", Direccion = "Ruta 5 km 30", Ciudad = "Canelones" },
            Detalle = [Linea("Lana", 100, 80m, IndicadorFacturacion.VendedorImeba)],
        },
    };

    public static TheoryData<string> NombresDeCasos() => [.. Casos.Keys];

    [SimuladorTheory]
    [MemberData(nameof(NombresDeCasos))]
    public async Task ElCfeEsAceptado(string caso)
    {
        var emitido = await sim.Cliente.EmitirAsync(Casos[caso]);
        await sim.Enviar(emitido);
        await sim.AssertAceptados(emitido);
    }

    [SimuladorFact]
    public async Task LasNotasQueReferencianCfeAceptadosSonAceptadas()
    {
        var factura = await sim.Cliente.EmitirAsync(new Cfe(TipoCfe.EFactura) { Receptor = Cliente, Detalle = [Linea("Licencia", 1, 5000m)] });
        var ticket = await sim.Cliente.EmitirAsync(new Cfe(TipoCfe.ETicket) { Detalle = [Linea("Servicio", 1, 1000m)] });
        await sim.Enviar(factura, ticket);

        var notaFactura = await sim.Cliente.EmitirAsync(new Cfe(TipoCfe.NotaCreditoEFactura)
        {
            Receptor = Cliente,
            Referencias =
            [
                new RefCfe
                {
                    TipoCfe = TipoCfe.EFactura, Serie = factura.Serie, NroCfe = factura.Numero, FechaCfe = factura.FechaEmision,
                    MontoCfeRef = 1000m, MonedaCfeRef = Moneda.PesoUruguayo, Razon = "Bonificación",
                },
            ],
            Detalle = [Linea("Bonificación", 1, 1000m)],
        });
        var notaTicket = await sim.Cliente.EmitirAsync(new Cfe(TipoCfe.NotaCreditoETicket)
        {
            Referencias =
            [
                new RefCfe
                {
                    TipoCfe = TipoCfe.ETicket, Serie = ticket.Serie, NroCfe = ticket.Numero, MontoCfeRef = 100m, MonedaCfeRef = Moneda.PesoUruguayo,
                    Razon = "Devolución",
                },
            ],
            Detalle = [Linea("Devolución", 1, 100m)],
        });
        await sim.Enviar(notaFactura, notaTicket);
        await sim.AssertAceptados(factura, ticket, notaFactura, notaTicket);
    }
}

/// <summary>El reporte diario de un emisor con CFE aceptados, recibido sin advertencias por el simulador de la DGI.</summary>
[Trait("Categoria", "SimuladorDgi")]
public sealed class SimuladorDgiReporteTests(SimuladorDgi sim) : IClassFixture<SimuladorDgi>
{
    [SimuladorFact]
    public async Task ElReporteDiarioEsRecibido()
    {
        var ticket = await sim.Cliente.EmitirAsync(new Cfe(TipoCfe.ETicket)
        {
            Detalle = [new LineaDetalle { NombreItem = "Servicio", Cantidad = 1, PrecioUnitario = 1000m }],
        });
        var factura = await sim.Cliente.EmitirAsync(new Cfe(TipoCfe.EFactura)
        {
            Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A.", Direccion = "Av. Italia 1234", Ciudad = "Montevideo" },
            Detalle = [new LineaDetalle { NombreItem = "Licencia", Cantidad = 2, PrecioUnitario = 2500m }],
        });
        await sim.Enviar(ticket, factura);
        await sim.AssertAceptados(ticket, factura);

        var reporte = await sim.Cliente.EnviarReporteDiarioAsync(HoraUruguay.Hoy());

        Assert.True(reporte.Recibido, $"{reporte.Estado}: {reporte.Mensaje}");
        var reportes = await sim.Admin.GetFromJsonAsync<JsonElement>($"/admin/reportes?rut={sim.Rut}");
        var advertencias = reportes.EnumerateArray().SelectMany(r => r.GetProperty("advertencias").EnumerateArray().Select(a => a.GetString())).ToList();
        Assert.True(advertencias.Count == 0, string.Join("; ", advertencias));
    }
}
