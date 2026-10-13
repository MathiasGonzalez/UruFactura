# UruFactura 🇺🇾

**La vía rápida hacia la Facturación Electrónica en Uruguay**

[![CI](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/ci.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/ci.yml)
[![Publish NuGet](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/publish.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/publish.yml)
[![Deploy Landing](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-pages.yml/badge.svg)](https://github.com/MathiasGonzalez/UruFactura/actions/workflows/deploy-pages.yml)
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
| **Generación de CFE** | Crea e-Tickets, e-Facturas, e-Remitos y sus notas de corrección con objetos simples, validados contra los XSD oficiales de la DGI (paquete `xsds_fe` 1.44.2, formato CFE v25.2). |
| **Firma Digital** | Firma XML Signature *enveloped* del CFE y firma **WS-Security** de cada mensaje SOAP, usando certificados estándar `.p12` / `.pfx`. |
| **Comunicación SOAP** | Cliente de los servicios web de la DGI (`ws_efactura` y `ws_consultas`): envío de sobres `EnvioCFE`, consulta del estado del envío y del comprobante, y Reporte Diario. |
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
│   │   ├── Signature/                  # CfeFirmante / ICfeFirmante (XML Signature enveloped)
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
    RutEmisor           = "219999820013",       // 12 dígitos, sin puntos ni guión
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
    CodigoSucursal        = 1,                  // CdgDGISucur asignado por la DGI (default: 1)
    AlgoritmoFirma        = AlgoritmoFirma.Sha256, // o Sha1 si la DGI rechaza SHA-256 en tu ambiente

    TelefonosEmisor       = ["29001234"],       // opcional, hasta 2
    CorreoEmisor          = "ventas@empresa.com.uy",
    ValorUnidadIndexada   = 6.2m,               // opcional: valida el tope de 5.000 UI para e-Tickets sin receptor
    FechaIncorporacionEmisor = "01/01/2020",    // leyenda "Fecha emisor" de la representación impresa (o "Res. N/año")
    UrlVerificacionEmisor = "www.miempresa.com.uy", // leyenda de verificación de e-Tickets (por defecto www.dgi.gub.uy)

    // En Producción son obligatorias (la DGI las entrega al habilitar el ambiente).
    // En Homologación se usan por defecto los servicios de prueba de la DGI.
    // UrlEnvio      = "https://…/ws_efactura",
    // UrlConsultas  = "https://…/ws_consultas",
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

// Registrar el CAE que entregó la DGI (con su serie y número de autorización)
client.Cae.RegistrarCae(new Cae
{
    NroSerie           = "CAE2025001",   // identificador interno (clave en el repositorio)
    NumeroAutorizacion = "90110001945",  // CAE_ID: va en el bloque CAEData de cada CFE
    Serie              = "A",            // serie de los comprobantes que autoriza
    TipoCfe            = TipoCfe.ETicket,
    RangoDesde         = 1,
    RangoHasta         = 1000,
    FechaVencimiento   = new DateOnly(2026, 12, 31),
});

// Crear comprobante y tomar el próximo número del CAE (completa Numero, Serie y CaeData)
var eticket = client.CrearETicket();
client.AsignarCae(eticket);   // o await client.AsignarCaeAsync(eticket) para persistirlo (ver abajo)
eticket.Detalle.Add(new LineaDetalle
{
    NombreItem     = "Servicio de consultoría",
    UnidadMedida   = "UN",              // hasta 4 caracteres; si se omite se informa "N/A"
    Cantidad       = 1,
    PrecioUnitario = 1000m,
    IndFactIva     = TipoIva.Basico,
    DescuentoMonto = 50m,               // opcional: monto fijo sin IVA
    RecargoMonto   = 0m,
});

// Validar antes de enviar
var errores = eticket.Validar();
if (errores.Count > 0)
{
    foreach (var error in errores)
        Console.WriteLine($"❌ {error}");
    return;
}

// 1) Genera el XML, lo firma y lo envía dentro de un sobre EnvioCFE.
var envio = await client.EnviarCfeAsync(eticket);
if (!envio.Exitoso)
{
    Console.WriteLine($"❌ Sobre rechazado ({envio.Codigo}): {envio.Mensaje}");
    return;
}
// La DGI recibió el sobre; el resultado de cada comprobante se consulta después.

// 2) Consultar el resultado del envío (la DGI indica desde cuándo: envio.ConsultarDesde)
var estado = await client.ConsultarEstadoEnvioAsync(envio, [eticket]);
Console.WriteLine(estado.Exitoso
    ? "✅ Aceptado (AE)"
    : $"❌ {estado.Codigo}: {estado.Mensaje}");   // eticket.AceptadoPorDgi queda actualizado
```

> ℹ️ Enviar un CFE **no** lo da por aceptado: la DGI responde primero con un acuse del sobre
> (`ACKSobre`, estado `AS` o `BS`) y luego, con el `IdReceptor` y el `Token` de ese acuse, informa el
> estado de cada comprobante (`AE` aceptado, `BE` rechazado, `CE` observado). También podés consultar
> un comprobante suelto con `ConsultarEstadoCfeAsync(cfe)`.

### 3. Generar PDF

El PDF incluye el QR y los datos del pie exigidos por la DGI (código de seguridad, CAE, rango y vencimiento). `GenerarPdfA4`/`GenerarPdfTermico` firman el CFE si todavía no lo está, porque el QR lleva el código de seguridad de la firma.

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
client.AsignarCae(nc);   // requiere un CAE registrado para el tipo 102
nc.Referencias.Add(new RefCfe
{
    TipoCfe    = TipoCfe.ETicket,
    Serie      = "A",
    NroCfe     = 42,
    FechaCfe   = new DateTime(2025, 6, 15),
    MontoCfeRef = 1220m,                  // total del CFE referenciado
    MonedaCfeRef = Moneda.PesoUruguayo,
    Razon      = "Devolución de mercadería",
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
    new Cae { NroSerie = "CAE2025001", NumeroAutorizacion = "90110001945", Serie = "A", TipoCfe = TipoCfe.ETicket,  RangoDesde = 1, RangoHasta = 1000, FechaVencimiento = new DateOnly(2026, 12, 31) },
    new Cae { NroSerie = "CAE2025002", NumeroAutorizacion = "90110001946", Serie = "A", TipoCfe = TipoCfe.EFactura, RangoDesde = 1, RangoHasta = 500,  FechaVencimiento = new DateOnly(2026, 12, 31) },
});

// Obtener próximo número disponible (thread-safe). Normalmente no hace falta llamarlo
// directamente: client.AsignarCae(cfe) lo usa y completa el CFE.
var (cae, numero) = client.Cae.ObtenerProximoNumero(TipoCfe.ETicket);

// Obtener CAE activo para un tipo
var caeActivo = client.Cae.ObtenerCaeActivo(TipoCfe.ETicket);

// Ver advertencias (vencimiento próximo, rango casi agotado)
var advertencias = client.Cae.ObtenerAdvertencias(diasAlertaVencimiento: 7, porcentajeAlertaUso: 80m);
foreach (var advertencia in advertencias)
    Console.WriteLine(advertencia);

Console.WriteLine(client.Cae.ResumenEstado());
```

### 6. Varios CFE en un sobre y Reporte Diario

```csharp
// Hasta 250 CFE por sobre
var envio = await client.EnviarCfesAsync([cfe1, cfe2, cfe3]);
var estado = await client.ConsultarEstadoEnvioAsync(envio, [cfe1, cfe2, cfe3]);

// Reporte Diario: resumen firmado (montos, cantidad y rangos de números por tipo) de los
// CFE emitidos en la fecha. No contiene los comprobantes, solo el resumen.
var reporte = await client.EnviarReporteDiarioAsync(DateTime.Today, cfesDelDia);
Console.WriteLine($"{reporte.CantidadCfe} CFE → {reporte.Respuesta.Codigo} {reporte.Respuesta.Mensaje}");
```

### 7. Exportación y remitos

```csharp
var exportacion = client.CrearEFacturaExportacion();
exportacion.Moneda        = Moneda.DolarAmericano;
exportacion.TipoCambio    = 40.5m;
exportacion.ClausulaVenta = "FOB";   // Incoterm (1-3 letras) o "N/A"
exportacion.ModalidadVenta = 1;      // 1 Régimen general, 2 Consignación, ...
exportacion.ViaTransporte = 1;       // 1 Marítimo, 2 Aéreo, 3 Terrestre, 9 Otro
exportacion.Receptor = new Receptor
{
    Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte,
    CodigoPais = "US", Pais = "Estados Unidos", RazonSocial = "Importer Inc.",
};

var remito = client.CrearERemito();
remito.IndTraslado = IndTraslado.TrasladoPorVenta; // se informa como TipoTraslado 1 (venta) o 2 (traslado interno)
```

### 8. Descuentos globales, medios de pago, retenciones y otros datos

```csharp
// Descuento o recargo global (en monto, sin IVA) sobre la base de un tipo de IVA: cambia el IVA y el total
cfe.DescuentosRecargos.Add(new DescuentoRecargoGlobal
{
    Tipo = TipoMovimientoGlobal.Descuento, Glosa = "Promoción", Monto = 50m, IndFact = TipoIva.Basico,
});

// Medios de pago (hasta 40)
cfe.MediosPago.Add(new MedioPago { Glosa = "Efectivo", Valor = 500m, Orden = 1 });

// Datos opcionales del comprobante, del receptor y de las líneas
cfe.NroInterno = "PED-1234";
cfe.FormaPago = FormaPago.Credito;
cfe.FechaVencimiento = new DateOnly(2026, 7, 31);
cfe.InfoAdicional = "Orden de compra 77";
cfe.Receptor.CodigoPostal = "11200";
cfe.Detalle[0].Codigos.Add(new CodigoItem { Tipo = "EAN13", Codigo = "7730000000001" });
cfe.Detalle[0].Descripcion = "Descripción ampliada";
cfe.Detalle[0].DescuentoPorcentaje = 10m;   // se calcula el monto si no se informa DescuentoMonto

// e-Resguardo: las retenciones van en las líneas
var resguardo = client.CrearEResguardo();
resguardo.Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." };
resguardo.Detalle.Add(new LineaDetalle
{
    Retenciones = { new RetencionPercepcion { Codigo = "R1", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m } },
});

// Venta por cuenta ajena: datos del mandante
var cuentaAjena = client.Crear(TipoCfe.ETicketCuentaAjena);
cuentaAjena.Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A." };
```

### 9. Persistencia de CAE al iniciar

```csharp
using var client = UruFacturaClientBuilder.WithDefaults(config)
    .WithCaeRepository(miRepositorio)
    .Build();

await client.CargarCaesAsync();            // al iniciar: carga los CAE con su último número usado
await client.RegistrarCaeAsync(nuevoCae);  // alta de un CAE: lo registra y lo guarda
await client.AsignarCaeAsync(cfe);         // cada emisión persiste el número usado
```

### 10. Validaciones locales

`cfe.Validar()` (que también ejecuta `GenerarXml`) detecta antes de enviar: RUT del emisor y del receptor con
dígito verificador inválido, serie y número fuera del rango del CAE, longitudes máximas del esquema, receptor
faltante en e-Factura/e-Resguardo, datos de exportación y de remito, mandante en cuenta ajena, referencias de las
notas, descuentos y medios de pago mal formados y, si configurás `ValorUnidadIndexada`, e-Tickets sobre 5.000 UI sin
identificar al receptor. Las reglas que dependen de datos de la DGI (por ejemplo, si el emisor está habilitado) solo
las valida la DGI al recibir el sobre.

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
| 124 | e-Remito de Exportación | `CrearERemitoExportacion()` |
| 181 | e-Remito | `CrearERemito()` |
| 182 | e-Resguardo | `CrearEResguardo()` |
| 131-133 | e-Ticket y sus notas, venta por cuenta ajena | `Crear(TipoCfe.ETicketCuentaAjena)` … |
| 141-143 | e-Factura y sus notas, venta por cuenta ajena | `Crear(TipoCfe.EFacturaCuentaAjena)` … |
| 201-203, 211-213, 221-224, 231-233, 241-243, 281, 282 | Comprobantes de contingencia (CFC) | `Crear(TipoCfe.ETicketContingencia)` … |

> ℹ️ Los códigos son los del formato CFE de la DGI. Los tipos sin método propio se crean con
> `client.Crear(TipoCfe.…)`. Las **ventas por cuenta ajena** exigen `cfe.Mandante`; el **e-Resguardo**
> informa las retenciones en `LineaDetalle.Retenciones` (la línea no lleva nombre ni precio). Los
> e-Remitos no tienen notas de crédito/débito. **No soportados:** e-Boleta de entrada (151-153 y 251-253),
> la adenda, y los descuentos/recargos globales en porcentaje (solo en monto).

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
| `ICfeFirmante` | `CfeFirmante` | Firma XML Signature enveloped (CFE y reporte) |
| `ICfeXmlBuilder` | `CfeXmlBuilder` | Serialización XML DGI |
| `IDgiSoapClient` | `DgiSoapClient` | Transporte SOAP hacia DGI |

### Personalizar el comportamiento

Las clases principales (`UruFacturaClient`, `CfeXmlBuilder`, `CfeFirmante`, `DgiSoapClient`, `CaeManager`,
`InMemoryCaeRepository`, `CfePdfGenerator`, `CfeQrGenerator` y los modelos `Cfe` y `Cae`) exponen sus
métodos públicos como `virtual`, así que podés heredar y sobreescribir solo lo que necesites:

```csharp
public class MiCliente(UruFacturaConfig config, ICfeXmlBuilder xml, ICfeFirmante firma,
                       ICaeManager cae, IDgiSoapClient soap)
    : UruFacturaClient(config, xml, firma, cae, soap, pdfGenerator: null)
{
    public override async Task<RespuestaDgi> EnviarCfeAsync(Cfe cfe, CancellationToken ct = default)
    {
        Auditoria.Registrar(cfe);                 // lógica propia antes/después del envío
        return await base.EnviarCfeAsync(cfe, ct);
    }

    protected override Cfe CrearCfe(TipoCfe tipo)  // valores por defecto propios
    {
        var cfe = base.CrearCfe(tipo);
        cfe.CodigoSucursal = 3;
        return cfe;
    }
}
```

También podés reemplazar componentes completos con el builder (`WithXmlBuilder`, `WithSigner`, `WithSoapClient`, …).

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
Al iniciar la app:   await client.CargarCaesAsync()   (repo.CargarTodosAsync() → manager.RegistrarCaes())
Al emitir un CFE:    await client.AsignarCaeAsync(cfe)   (toma el número y llama a repo.ActualizarUltimoNroUsadoAsync)
```

### Uso con la implementación en memoria (tests / desarrollo)

```csharp
// InMemoryCaeRepository: los datos se pierden al reiniciar (solo para tests/dev)
var repo = new InMemoryCaeRepository();
await repo.GuardarCaeAsync(new Cae
{
    NroSerie         = "CAE2025001",
    NumeroAutorizacion = "90110001945",
    Serie            = "A",
    TipoCfe          = TipoCfe.ETicket,
    RangoDesde       = 1,
    RangoHasta       = 1000,
    FechaVencimiento = new DateOnly(2026, 12, 31),
});

// Al iniciar: cargar y registrar en el manager
client.Cae.RegistrarCaes(await repo.CargarTodosAsync());

// Al emitir: el cliente toma el número y lo persiste en el repositorio configurado
// (UruFacturaClientBuilder.WithDefaults(config).WithCaeRepository(repo).Build())
await client.AsignarCaeAsync(cfe);
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

## 📚 Documentación adicional

| Documento | Descripción |
|-----------|-------------|
| [FACTURACION_URUGUAY.md](docs/FACTURACION_URUGUAY.md) | Marco normativo, ejemplos por tipo de empresa y consideraciones fiscales |
| [CERTIFICACION_DGI.md](docs/CERTIFICACION_DGI.md) | Proceso de homologación y puesta en producción paso a paso |

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
