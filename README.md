# UruFactura 🇺🇾

**La vía rápida hacia la Facturación Electrónica en Uruguay**

[![CI](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/ci.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/ci.yml)
[![Publish NuGet](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/publish.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/publish.yml)
[![Deploy Landing](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-pages.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-pages.yml)
[![Deploy Admin](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-admin.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-admin.yml)
[![Deploy Cloudflare](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-cloudflare.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-cloudflare.yml)
[![NuGet](https://img.shields.io/nuget/v/UruFactura.svg)](https://www.nuget.org/packages/UruFactura/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/UruFactura.svg)](https://www.nuget.org/packages/UruFactura/)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> 📖 **Documentación completa:** [mathiasgonzalez.github.io/UruFactura](https://mathiasgonzalez.github.io/UruFactura/)

UruFactura es una librería open-source **.NET 10 C#** de alto nivel diseñada para simplificar la integración de sistemas locales con el ecosistema de la DGI. Olvídate de lidiar con la complejidad manual de los sobres SOAP o la estructura rígida de los esquemas XML; esta herramienta actúa como un puente amigable entre tu lógica de negocio y los requisitos impositivos uruguayos.

---

## 🚀 Características Principales

| Característica | Detalle |
|---|---|
| **Generación de CFE** | Crea e-Tickets, e-Facturas, e-Remitos y sus notas de corrección con objetos simples, cumpliendo el formato XML **v25.2** de la DGI. |
| **Firma Digital XAdES-BES** | Módulo integrado para la firma electrónica usando certificados estándar `.p12` / `.pfx`. |
| **Comunicación SOAP** | Cliente SOAP optimizado para envío de comprobantes, consulta de estados y envío del Reporte Diario. |
| **Gestión de CAE** | Control inteligente de Constancias de Autorización de Emisión con alertas de vencimiento y rango. |
| **Representación Impresa** | Generador de PDFs (A4 y térmico 80mm) con código QR y sellos de seguridad según normativa. |

---

## 📦 Estructura del Proyecto

```
UruFactura/
├── src/
│   ├── UruFactura/                  # Paquete completo (con PDF)
│   │   ├── Configuration/              # UruFacturaConfig
│   │   ├── Enums/                      # TipoCfe, TipoIva, FormaPago, Moneda, Ambiente, IndTraslado, TipoDocumentoReceptor
│   │   ├── Exceptions/                 # CaeException, CfeValidationException, DgiCommunicationException,
│   │   │                               # FirmaDigitalException, PdfGenerationException, UruFacturaException
│   │   ├── Models/                     # Cfe, Cae, Receptor, LineaDetalle, RefCfe,
│   │   │                               # RespuestaDgi, RespuestaReporteDiario, TotalesIva
│   │   ├── Formatting/                 # CfeFormat (formateadores internos de fecha y moneda)
│   │   ├── Xml/                        # CfeXmlBuilder / ICfeXmlBuilder (generación XML DGI)
│   │   ├── Signature/                  # CfeFirmante / ICfeFirmante (XAdES-BES)
│   │   ├── Soap/                       # DgiSoapClient / IDgiSoapClient (comunicación con DGI)
│   │   ├── Cae/                        # CaeManager / ICaeManager (gestión de CAE)
│   │   │                               # InMemoryCaeRepository / ICaeRepository (persistencia)
│   │   ├── Pdf/                        # CfePdfGenerator / ICfePdfGenerator (PDF A4 y térmico)
│   │   │                               # CfeQrGenerator / ICfeQrGenerator (código QR)
│   │   ├── IUruFacturaClient.cs        # Interfaz pública (para mocking en tests)
│   │   ├── UruFacturaClient.cs         # Fachada principal
│   │   ├── UruFacturaClientBuilder.cs  # Builder fluido con WithDefaults()
│   │   └── UruFacturaClientBuilderPdf.cs # Extensión del builder para PDF (solo paquete completo)
│   └── UruFactura.Lite/             # Paquete Lite (sin FluentReport/SkiaSharp/ZXing)
│       └── UruFacturaClient.cs         # Constructores de conveniencia (sin PDF por defecto)
└── tests/
    └── UruFactura.Tests/            # Tests unitarios xUnit
```

---

## 🛠️ Uso Rápido

### 1. Configurar el cliente

```csharp
var config = new UruFacturaConfig
{
    // Obligatorios
    RutEmisor           = "210000000012",       // 12 dígitos, sin puntos ni guión
    RazonSocialEmisor   = "Mi Empresa S.A.",
    DomicilioFiscal     = "Av. 18 de Julio 1234",
    Ciudad              = "Montevideo",
    Departamento        = "Montevideo",
    Giro                = "Comercio al por mayor", // Actividad económica (opcional)
    Ambiente            = Ambiente.Homologacion,
    RutaCertificado     = "/ruta/al/certificado.p12",
    PasswordCertificado = Environment.GetEnvironmentVariable("CERT_PASSWORD")!,

    // Opcionales
    NombreComercialEmisor = "Mi Comercio",      // nombre de fantasía
    SoapTimeoutSegundos   = 60,                 // timeout SOAP (default: 30 s)
    OmitirValidacionSsl   = true,               // solo en Homologación con CA no confiable
};

// Constructor de conveniencia (paquete completo — incluye PDF)
using var client = new UruFacturaClient(config);

// O usando el builder fluido (recomendado para DI o tests):
using var client = UruFacturaClientBuilder.WithDefaults(config)
    .WithDefaultPdf()   // omitir si usás UruFactura.Lite
    .Build();
```

> ⚠️ Nunca almacenes `PasswordCertificado` en texto plano. Usá variables de entorno o un gestor de secretos.

### 2. Crear y enviar un e-Ticket

```csharp
using UruFactura.Models; // Cae, LineaDetalle, Receptor, RefCfe

// Registrar CAE
client.Cae.RegistrarCae(new Cae
{
    NroSerie        = "CAE2025001",
    TipoCfe         = TipoCfe.ETicket,
    RangoDesde      = 1,
    RangoHasta      = 1000,
    FechaVencimiento = new DateOnly(2026, 12, 31),
});

// Crear comprobante
var eticket = client.CrearETicket();
eticket.Numero = 1;
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Servicio de consultoría",
    Cantidad       = 1,
    PrecioUnitario = 1000m,
    IndFactIva     = TipoIva.Basico,
    // Opcional: descuentos y recargos
    DescuentoMonto    = 50m,   // monto fijo sin IVA
    // DescuentoPorcentaje = 5m, // alternativa: porcentaje
    RecargoMonto      = 0m,
});

// Validar antes de enviar
var errores = eticket.Validar();
if (errores.Count > 0)
{
    foreach (var error in errores)
        Console.WriteLine($"❌ {error}");
    return;
}

// Generar XML, firmar y enviar
var xmlFirmado = client.GenerarYFirmar(eticket);
var respuesta  = await client.EnviarCfeAsync(eticket);

if (respuesta.Exitoso)
    Console.WriteLine($"✅ Aceptado: {respuesta.Mensaje}");
```

### 3. Generar PDF

```csharp
// PDF A4
byte[] pdfA4 = client.GenerarPdfA4(eticket);
await File.WriteAllBytesAsync("eticket.pdf", pdfA4);

// PDF Térmico (ticket 80mm)
byte[] pdfTermico = client.GenerarPdfTermico(eticket);
await File.WriteAllBytesAsync("eticket_termico.pdf", pdfTermico);
```

### 4. Nota de crédito con referencia

```csharp
var nc = client.CrearNotaCreditoETicket();
nc.Numero = 1;
nc.Referencias.Add(new RefCfe
{
    TipoCfe  = TipoCfe.ETicket,
    Serie    = "A",
    NroCfe   = 42,
    FechaCfe = new DateTime(2025, 6, 15),
    Razon    = "Devolución de mercadería",
});
nc.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Devolución producto",
    Cantidad       = 1,
    PrecioUnitario = 500m,
    IndFactIva     = TipoIva.Basico,
});
```

### 5. Verificar estado de CAEs

```csharp
// Registrar múltiples CAEs a la vez
client.Cae.RegistrarCaes(new[]
{
    new Cae { NroSerie = "CAE2025001", TipoCfe = TipoCfe.ETicket,  RangoDesde = 1, RangoHasta = 1000, FechaVencimiento = new DateOnly(2026, 12, 31) },
    new Cae { NroSerie = "CAE2025002", TipoCfe = TipoCfe.EFactura, RangoDesde = 1, RangoHasta = 500,  FechaVencimiento = new DateOnly(2026, 12, 31) },
});

// Obtener próximo número disponible (thread-safe)
var (cae, numero) = client.Cae.ObtenerProximoNumero(TipoCfe.ETicket);

// Obtener CAE activo para un tipo
var caeActivo = client.Cae.ObtenerCaeActivo(TipoCfe.ETicket);

// Ver advertencias (vencimiento próximo, rango casi agotado)
var advertencias = client.Cae.ObtenerAdvertencias(diasAlertaVencimiento: 7, porcentajeAlertaUso: 80m);
foreach (var advertencia in advertencias)
    Console.WriteLine(advertencia);

Console.WriteLine(client.Cae.ResumenEstado());
```

---

## 📄 Tipos de CFE soportados

| Código | Tipo | Método del cliente |
|--------|------|-------------------|
| 101 | e-Ticket | `CrearETicket()` |
| 102 | Nota de Crédito e-Ticket | `CrearNotaCreditoETicket()` |
| 103 | Nota de Débito e-Ticket | `CrearNotaDebitoETicket()` |
| 111 | e-Factura | `CrearEFactura()` |
| 112 | Nota de Crédito e-Factura | `CrearNotaCreditoEFactura()` |
| 113 | Nota de Débito e-Factura | `CrearNotaDebitoEFactura()` |
| 121 | e-Factura de Exportación | `CrearEFacturaExportacion()` |
| 122 | Nota de Crédito e-Factura Exportación | `CrearNotaCreditoEFacturaExportacion()` |
| 123 | Nota de Débito e-Factura Exportación | `CrearNotaDebitoEFacturaExportacion()` |
| 131 | e-Remito Despachante | `CrearERemitoDespachante()` |
| 151 | e-Resguardo | `CrearEResguardo()` |
| 181 | e-Remito | `CrearERemito()` |
| 182 | Nota de Crédito e-Remito | `CrearNotaCreditoERemito()` |

---

## 💸 Tasas de IVA (`TipoIva`)

| Valor | Nombre | Tasa |
|-------|--------|------|
| `TipoIva.Exento` | Exento | 0 % |
| `TipoIva.Minimo` | Mínimo | 10 % |
| `TipoIva.Basico` | Básico | 22 % |
| `TipoIva.Suspendido` | Suspendido | — |

---

## 🧩 Interfaz `IUruFacturaClient`

UruFactura expone la interfaz `IUruFacturaClient` para facilitar el **mocking en tests** de aplicaciones consumidoras:

```csharp
// En tu aplicación
public class MiServicioFacturacion(IUruFacturaClient client) { ... }

// En tus tests
var mockClient = Substitute.For<IUruFacturaClient>(); // NSubstitute, Moq, etc.
```

### Interfaces de dependencias

Todas las dependencias internas del cliente también tienen interfaces, lo que permite reemplazar
cualquier componente sin subclasear `UruFacturaClient`:

| Interfaz | Implementación predeterminada | Descripción |
|---|---|---|
| `ICaeManager` | `CaeManager` | Gestión de CAEs en memoria (thread-safe) |
| `ICaeRepository` | `InMemoryCaeRepository` | Persistencia de CAEs (ver [sección de persistencia](#-persistencia-de-caes)) |
| `ICfePdfGenerator` | `CfePdfGenerator` | Generación de PDF (solo paquete completo) |
| `ICfeQrGenerator` | `CfeQrGenerator` | Generación del código QR |
| `ICfeFirmante` | `CfeFirmante` | Firma XAdES-BES |
| `ICfeXmlBuilder` | `CfeXmlBuilder` | Serialización XML DGI |
| `IDgiSoapClient` | `DgiSoapClient` | Transporte SOAP hacia DGI |

### `UruFacturaClientBuilder`

El builder fluido es la forma recomendada de construir el cliente, especialmente cuando se necesita
inyectar dependencias personalizadas (p.ej. mocks en tests o transporte SOAP propio):

```csharp
// Caso más habitual — todas las implementaciones predeterminadas:
using var client = UruFacturaClientBuilder.WithDefaults(config)
    .WithDefaultPdf()   // solo disponible en UruFactura (no en Lite)
    .Build();

// Reemplazar solo lo que necesitás:
using var client = UruFacturaClientBuilder.WithDefaults(config)
    .WithCaeManager(miCaeManager)
    .WithSoapClient(miSoapMock)
    .WithPdfGenerator(miGeneradorPdf)
    .WithSigner(miCfeFirmante)          // reemplaza la firma digital (ICfeFirmante)
    .WithXmlBuilder(miXmlBuilder)       // reemplaza el generador de XML (ICfeXmlBuilder)
    .Build();
```

> 🏭 **APIs de alto tráfico:** usá `.WithHttpClient(httpClient)` para inyectar un `HttpClient`
> gestionado por `IHttpClientFactory` y evitar el agotamiento de sockets (*socket exhaustion*)
> que ocurre cuando se crean y descartan instancias de `HttpClient` por cada request.
> Al hacerlo, UruFactura no configura el handler interno (certificado de cliente ni `OmitirValidacionSsl`),
> así que es responsabilidad del caller configurarlos en el handler del `HttpClient` inyectado.
>
> ```csharp
> // Ejemplo en Program.cs (ASP.NET Core)
> builder.Services.AddHttpClient("DGI");
> builder.Services.AddSingleton(sp =>
>     UruFacturaClientBuilder
>         .WithDefaults(config)
>         .WithHttpClient(sp.GetRequiredService<IHttpClientFactory>().CreateClient("DGI"))
>         .Build());
> ```

---

## 💾 Persistencia de CAEs

En producción, el estado de los CAEs —especialmente `UltimoNroUsado`— debe sobrevivir reinicios
de la aplicación. Si la app arranca con el contador en cero, volvería a emitir números ya usados,
lo que la DGI rechazaría por duplicado.

UruFactura separa la gestión en memoria (`ICaeManager`) de la persistencia (`ICaeRepository`):

```
Al iniciar la app:   repo.CargarTodosAsync() → manager.RegistrarCaes()
Al emitir un CFE:    manager.ObtenerProximoNumero() → repo.ActualizarUltimoNroUsadoAsync()
```

### Uso con la implementación en memoria (tests / desarrollo)

```csharp
// InMemoryCaeRepository: los datos se pierden al reiniciar (solo para tests/dev)
var repo = new InMemoryCaeRepository();
await repo.GuardarCaeAsync(new Cae
{
    NroSerie         = "CAE2025001",
    TipoCfe          = TipoCfe.ETicket,
    RangoDesde       = 1,
    RangoHasta       = 1000,
    FechaVencimiento = new DateOnly(2026, 12, 31),
});

// Al iniciar: cargar y registrar en el manager
client.Cae.RegistrarCaes(await repo.CargarTodosAsync());

// Al emitir: obtener número y persistir inmediatamente
var (cae, numero) = client.Cae.ObtenerProximoNumero(TipoCfe.ETicket);
await repo.ActualizarUltimoNroUsadoAsync(cae.NroSerie, cae.UltimoNroUsado);
```

### Implementar persistencia real (producción)

Implementá `ICaeRepository` con tu mecanismo de almacenamiento:

```csharp
public class MiCaeRepository : ICaeRepository
{
    public ValueTask<IEnumerable<Cae>> CargarTodosAsync()
    {
        // Leer de base de datos / archivo / Redis / etc.
        throw new NotImplementedException();
    }

    public ValueTask GuardarCaeAsync(Cae cae)
    {
        // INSERT o UPDATE en tu store
        throw new NotImplementedException();
    }

    public ValueTask ActualizarUltimoNroUsadoAsync(string nroSerie, long ultimoNroUsado)
    {
        // UPDATE ultimo_nro_usado WHERE nro_serie = nroSerie
        // ¡Llamar en cada emisión exitosa antes de considerar la operación completa!
        throw new NotImplementedException();
    }
}
```

> ⚠️ `ActualizarUltimoNroUsadoAsync` debe ejecutarse **antes** de considerar la emisión
> finalizada. Si la app cae entre la emisión y la persistencia, el siguiente arranque
> repetirá ese número. En escenarios críticos, usá transacciones de base de datos o
> idempotencia en el endpoint de la DGI.

---

## ⚠️ Excepciones tipadas

| Excepción | Cuándo se lanza |
|-----------|----------------|
| `UruFacturaException` | Base de todas las excepciones |
| `CaeException` | CAE vencido, sin rango disponible o no registrado |
| `CfeValidationException` | CFE inválido según reglas de negocio |
| `DgiCommunicationException` | Error de comunicación con el servicio SOAP de la DGI |
| `FirmaDigitalException` | Error al firmar el CFE con el certificado |
| `PdfGenerationException` | Error al generar el PDF |

---

## 🔧 Requisitos

- .NET 10 SDK
- Certificado digital DGI (`.p12` / `.pfx`)
- CAE vigente emitido por la DGI

---

## ☁️ Despliegue en Cloudflare

El ecosistema UruFactura incluye una solución completa para Cloudflare:

```
cloudflare/
├── worker.js              → Worker multi-tenant (router)
├── wrangler.toml          → Config de Containers + Secrets
└── email-worker/          → Worker de envío de emails
     ├── worker.js
     └── wrangler.toml

admin/                     → Web de administración (Cloudflare Pages)
├── src/                   → SPA React + Vite
├── functions/auth/        → Pages Functions (auth backend)
└── wrangler.toml          → Config KV + Secrets

src/UruFactura.CloudflareApi/ → API .NET (Container)
src/UruFactura.TestApi/       → API local con Scalar UI
```

### Workflows CI/CD

| Workflow | Qué despliega |
|----------|---------------|
| `deploy-cloudflare.yml` | Worker + Container .NET → Cloudflare |
| `deploy-admin.yml` | Admin SPA + Functions → Cloudflare Pages |
| `deploy-email-worker.yml` | Email Worker → Cloudflare Workers |

### Desarrollo local

```bash
# API (con Scalar UI)
cd src/UruFactura.TestApi && dotnet run

# Admin + auth (Pages Functions con KV local)
cd admin && npx wrangler pages dev --kv AUTH_CODES --kv AUTH_SESSIONS --kv TENANTS -- npm run dev
```

> 📖 Documentación completa: [docs/ARQUITECTURA_CLOUDFLARE.md](docs/ARQUITECTURA_CLOUDFLARE.md) y [admin/README.md](admin/README.md)

---

## 📚 Documentación adicional

| Documento | Descripción |
|-----------|-------------|
| [FACTURACION_URUGUAY.md](docs/FACTURACION_URUGUAY.md) | Marco normativo, ejemplos por tipo de empresa y consideraciones fiscales |
| [CERTIFICACION_DGI.md](docs/CERTIFICACION_DGI.md) | Proceso de homologación y puesta en producción paso a paso |
| [ARQUITECTURA_CLOUDFLARE.md](docs/ARQUITECTURA_CLOUDFLARE.md) | Arquitectura completa del despliegue en Cloudflare (Worker, Containers, Admin, Email) |
| [admin/README.md](admin/README.md) | Web de administración: setup local, deploy, auth flow, integración con API |

---

## 📦 Paquetes NuGet

| Paquete | Cuándo usarlo |
|---------|--------------|
| [`UruFactura`](https://www.nuget.org/packages/UruFactura/) | Uso general — incluye generación de PDF A4 y térmico (FluentReport + SkiaSharp + ZXing) |
| [`UruFactura.Lite`](https://www.nuget.org/packages/UruFactura.Lite/) | Entornos donde el peso de las dependencias de PDF no es deseable (microservicios, Azure Functions, etc.). Podés inyectar tu propio `ICfePdfGenerator` si lo necesitás. |

```bash
# Paquete completo
dotnet add package UruFactura

# Paquete Lite (sin FluentReport / SkiaSharp / ZXing)
dotnet add package UruFactura.Lite
```

---

## 🧪 Tests

```bash
dotnet test tests/UruFactura.Tests/
```

---

## 📝 Licencia

MIT — ver [LICENSE](LICENSE)
