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

UruFactura es una librería open-source **.NET 10** para emitir Comprobantes Fiscales Electrónicos (CFE) ante la DGI: arma el XML según los esquemas oficiales, lo firma, administra la numeración de los CAE, habla con los servicios SOAP de la DGI y envía el reporte diario. Vos describís el comprobante; UruFactura se ocupa del resto.

---

## 🚀 Características

| Característica | Detalle |
|---|---|
| **Todos los CFE** | e-Ticket, e-Factura, exportación, e-Remito, e-Resguardo, sus notas, venta por cuenta ajena y contingencia. XML validado contra los XSD oficiales (`xsds_fe` 1.44.2, formato CFE v25.2). |
| **Firma digital** | XML Signature *enveloped* del CFE y del reporte, y **WS-Security** en cada mensaje SOAP, con un certificado `.p12`/`.pfx` o un `X509Certificate2` (Key Vault, HSM). |
| **Numeración con CAE** | El número se asigna en la misma operación atómica que registra el CFE: no se repite (tampoco con varias instancias) ni queda consumido sin comprobante. Alertas de vencimiento y de rango. |
| **DGI** | Envío de sobres `EnvioCFE`, consulta del resultado, consulta por comprobante y reporte diario, con respuestas tipadas. |
| **Representación impresa** | PDF A4 y térmico (80 mm) con QR y pie normativo, en el paquete `UruFactura.Pdf`. |

---

## 📦 Paquetes

| Paquete | Contenido |
|---|---|
| [`UruFactura`](https://www.nuget.org/packages/UruFactura/) | Todo menos el PDF. Sin dependencias de terceros. |
| [`UruFactura.Pdf`](https://www.nuget.org/packages/UruFactura.Pdf/) | PDF A4, térmico y QR (FluentReport + SkiaSharp + ZXing). |
| [`UruFactura.EntityFrameworkCore`](https://www.nuget.org/packages/UruFactura.EntityFrameworkCore/) | Almacenamiento en base de datos, para producción y varias instancias. |

```bash
dotnet add package UruFactura
dotnet add package UruFactura.Pdf                          # si imprimís
dotnet add package UruFactura.EntityFrameworkCore          # en producción
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL   # o el proveedor de tu base
```

---

## 🛠️ Uso

Todo está en un solo namespace: `using UruFactura;`.

### 1. Configurar el cliente

```csharp
var config = new UruFacturaConfig
{
    Emisor = new Emisor
    {
        Rut             = "219999820013",       // 12 dígitos, sin puntos ni guión
        RazonSocial     = "Mi Empresa S.A.",
        DomicilioFiscal = "Av. 18 de Julio 1234",
        Ciudad          = "Montevideo",
        Departamento    = "Montevideo",
        // Opcionales: NombreComercial, Giro, CodigoSucursal (default 1), Sucursal, Telefonos, Correo,
        // FechaIncorporacion y UrlVerificacion (leyendas de la representación impresa).
    },
    Ambiente            = Ambiente.Homologacion,
    RutaCertificado     = "/ruta/al/certificado.p12",       // o Certificado = miX509Certificate2
    PasswordCertificado = Environment.GetEnvironmentVariable("CERT_PASSWORD"),

    // En Producción son obligatorias (la DGI las entrega al habilitar el ambiente).
    // UrlEnvio     = "https://…/ws_efactura",
    // UrlConsultas = "https://…/ws_consultas",
    // Otras opciones: AlgoritmoFirma (Sha256 por defecto), Timeout (30 s), ValorUnidadIndexada,
    // OmitirValidacionSsl (solo en Homologación).
};

// Valida la configuración y carga el certificado. En Producción se indica además el almacenamiento
// (ver "Producción y varias instancias"); en Homologación, por defecto, es uno en memoria.
using var client = new UruFacturaClient(config);
```

> ⚠️ Nunca guardes `PasswordCertificado` en texto plano. Usá variables de entorno o un gestor de secretos.
> `config.ToString()` no la muestra, así que podés loguear la configuración.

### 2. Registrar los CAE

```csharp
await client.RegistrarCaeAsync(new Cae
{
    NumeroAutorizacion = "90110001945",   // CAE_ID entregado por la DGI
    TipoCfe            = TipoCfe.ETicket,
    Serie              = "A",
    RangoDesde         = 1,
    RangoHasta         = 1000,
    FechaVencimiento   = new DateOnly(2027, 12, 31),
});
```

Un CAE por tipo de CFE (y por serie). No se admiten dos CAE de la misma serie con números en común (se
emitirían números repetidos), y uno registrado por error que todavía no numeró nada se quita con
`EliminarCaeAsync`. La numeración la lleva el almacenamiento: el en memoria se pierde al reiniciar, por
eso en Producción el cliente exige [uno durable](#-producción-y-varias-instancias).

### 3. Emitir, enviar y consultar

```csharp
var cfe = new Cfe(TipoCfe.ETicket)
{
    Detalle =
    [
        new LineaDetalle { NombreItem = "Servicio de consultoría", Cantidad = 1, PrecioUnitario = 1000m, IndFactIva = TipoIva.Basico },
    ],
};

// Valida, toma el próximo número del CAE, arma el XML, lo firma y lo registra. El número se consume
// solo junto con el registro: si algo falla (validación, firma, base de datos, cancelación) no se pierde
// ninguno. client.Validar(cfe) devuelve los errores, con el campo de cada uno, sin emitir.
CfeEmitido emitido = await client.EmitirAsync(cfe);

// La DGI acusa recibo del sobre (AS); que lo reciba no significa que acepte el CFE.
AcuseSobre acuse = await client.EnviarAsync(emitido);
if (!acuse.Recibido)
    throw new Exception(acuse.Mensaje);   // "Sobre rechazado (BS): motivos"

// El resultado de cada comprobante se consulta después (la DGI indica desde cuándo: acuse.ConsultarDesde).
EstadoEnvio estado = await client.ConsultarEnvioAsync(acuse);
Console.WriteLine(emitido.AceptadoPorDgi ? "✅ Aceptado (AE)" : $"❌ {emitido.EstadoDgi}: {emitido.MensajeDgi}");
```

- `Cfe` es inmutable: se arma con inicializadores y `with`, y `EmitirAsync` copia sus listas, así que lo
  emitido es exactamente lo que queda firmado aunque después cambies las listas originales.
- `CfeEmitido` tiene el número, la serie, el CAE, los `Totales`, el `Xml` firmado (que hay que conservar), el
  `CodigoSeguridad`, la `UrlQr` y el estado ante la DGI (`EstadoDgi`: `EstadoCfe.Aceptado`, `Rechazado`,
  `Observado` o `Reliquidado`; `AceptadoPorDgi`). El estado lo actualiza el cliente: no se puede cambiar desde afuera.
- `EnviarAsync([cfe1, cfe2, …])` manda hasta 250 CFE en un sobre. `ConsultarCfeAsync(emitido)` consulta un
  comprobante suelto (útil tras un reinicio).
- Un CFE que la DGI ya recibió no se reenvía (`InvalidOperationException`): lo trataría como duplicado. Si
  el envío terminó en un error de comunicación (por ejemplo, un timeout), no se sabe si llegó: consultalo con
  `ConsultarCfeAsync` antes de reenviarlo. Si el sobre fue rechazado (`BS`), se reenvía el mismo `CfeEmitido`.
- Con `NroInterno` (por ejemplo, el id del pedido) un reintento no factura dos veces: si ya se emitió un CFE
  del mismo tipo con ese número interno, `EmitirAsync` lanza `CfeDuplicadoException` con el ya emitido en
  `Existente` (salvo que la DGI lo haya rechazado: entonces emite uno nuevo).
- Los rechazos de la DGI vienen en el acuse; los errores de comunicación (red, timeout, SOAP Fault, HTTP 5xx)
  se lanzan como `DgiCommunicationException`.

### 4. Representación impresa (`UruFactura.Pdf`)

```csharp
byte[] a4      = CfePdf.GenerarA4(emitido);
byte[] ticket  = CfePdf.GenerarTermico(emitido);   // 80 mm
byte[] qrPng   = CfePdf.GenerarQr(emitido);
```

Incluye el QR y el pie que exige la DGI (código de seguridad, CAE, rango y vencimiento). El detalle se adapta
al tipo: el e-Remito sin precios, la exportación sin IVA, el e-Resguardo con sus retenciones y las notas con
el comprobante que corrigen; la moneda extranjera se indica con su código y el tipo de cambio. Para un
diseño propio, `emitido.UrlQr` tiene el contenido del QR sin necesidad del paquete de PDF.

### 5. Reporte diario

```csharp
// Resumen firmado (montos, cantidades y rangos por tipo) de los CFE emitidos en la fecha,
// tomados del almacenamiento. Usá la fecha de Uruguay: el servidor puede estar en UTC.
AcuseReporte reporte = await client.EnviarReporteDiarioAsync(HoraUruguay.Hoy().AddDays(-1));
Console.WriteLine($"{reporte.CantidadCfe} CFE → {reporte.Estado} {reporte.Mensaje}");
```

El primer envío de una fecha usa la secuencia 1 (`secEnvio`); para corregir un reporte ya recibido se
reenvía completo con la siguiente (`secEnvio: 2`, …). Los CFE se leen de a uno, sin cargarlos todos en
memoria. Si el almacenamiento no tiene completos los de esa fecha (el en memoria, tras un reinicio), falla
en lugar de declarar cero comprobantes.

### 6. Otros comprobantes

```csharp
// e-Factura: receptor con RUT
var factura = new Cfe(TipoCfe.EFactura)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A." },
    Detalle  = [new LineaDetalle { NombreItem = "Licencia", Cantidad = 1, PrecioUnitario = 5000m }],
};

// Nota de crédito: referencia con monto y moneda del original (obligatorios desde CFE v25.01)
var nc = new Cfe(TipoCfe.NotaCreditoETicket)
{
    Referencias = [new RefCfe
    {
        TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 42, FechaCfe = new DateOnly(2026, 6, 15),
        MontoCfeRef = 1220m, MonedaCfeRef = Moneda.PesoUruguayo, Razon = "Devolución",
    }],
    Detalle = [new LineaDetalle { NombreItem = "Devolución", Cantidad = 1, PrecioUnitario = 1000m }],
};

// Exportación
var exportacion = new Cfe(TipoCfe.EFacturaExportacion)
{
    Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m,
    ClausulaVenta = "FOB", ModalidadVenta = 1, ViaTransporte = 1,
    Receptor = new Receptor { Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "US", RazonSocial = "Importer Inc." },
    Detalle = [new LineaDetalle { NombreItem = "Software", Cantidad = 1, PrecioUnitario = 8000m }],
};

// e-Remito (TrasladoPorVenta se informa como TipoTraslado 1; el resto como 2 = traslado interno)
var remito = new Cfe(TipoCfe.ERemito)
{
    IndTraslado = IndTraslado.TrasladoPropio,
    Detalle = [new LineaDetalle { NombreItem = "Cajas", Cantidad = 50 }],
};

// e-Resguardo: las retenciones van en las líneas
var resguardo = new Cfe(TipoCfe.EResguardo)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." },
    Detalle  = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "R1", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m }] }],
};

// Venta por cuenta ajena y contingencia: mismo modelo, otro tipo
var cuentaAjena = new Cfe(TipoCfe.ETicketCuentaAjena) { Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A." }, Detalle = [/* … */] };
var contingencia = new Cfe(TipoCfe.ETicketContingencia) { Detalle = [/* … */] };
```

Además: `DescuentosRecargos` (globales, en monto, cambian la base del IVA), `MediosPago`, `NroInterno`,
`FormaPago`, `FechaVencimiento`, `InfoAdicional`, datos opcionales del `Receptor` y de cada `LineaDetalle`
(`Descripcion`, `Codigos`, `DescuentoMonto`/`DescuentoPorcentaje`, `RecargoMonto`). Para emitir con otra
sucursal: `Emisor = config.Emisor with { CodigoSucursal = 3 }`.

### 7. Validaciones locales

`client.Validar(cfe)` (y `EmitirAsync`, antes de numerar) aplica las restricciones de los XSD de la DGI
(largos, rangos de montos, cantidades y fechas, series, códigos de país, caracteres de control) y las reglas
del formato: RUT del receptor con dígito verificador, receptor en e-Factura/e-Resguardo, datos de exportación
y de remito, mandante en cuenta ajena, referencias de las notas, descuentos que no dejen montos negativos y,
si configurás `ValorUnidadIndexada`, e-Tickets sobre 5.000 UI sin identificar al receptor. Todo lo que pasa la
validación produce un XML válido contra el XSD (los tests lo comprueban con miles de CFE aleatorios). Cada
error es un `ErrorValidacion` con el campo (`Detalle[0].PrecioUnitario`, `Receptor.Documento`, …) y el motivo,
para mostrarlo junto al dato. La configuración (emisor, certificado, URLs) y cada CAE se validan al construir
el cliente y al registrarlos.
Los montos se calculan con la misma precisión con que se escriben en el XML (cantidad 3 decimales, precio 6,
montos 2) y redondeo comercial, para que coincidan con lo que recalcula la DGI. Las reglas que dependen de
datos de la DGI (por ejemplo, si el emisor está habilitado) solo las valida la DGI.

---

## 🏭 Producción y varias instancias

El almacenamiento (`IUruFacturaStore`) guarda los CAE con su numeración, el número de envío y los CFE
emitidos (de ahí sale el reporte diario). En Homologación, si no indicás otro, el cliente usa un
`InMemoryStore`, que sirve para un solo proceso: si la aplicación se reinicia, volvería a usar números ya
emitidos. En Producción el almacenamiento es obligatorio: usá `UruFactura.EntityFrameworkCore`, que además
es seguro con varias instancias (réplicas, blue/green, una API y un worker):

| Tabla | Para qué |
|---|---|
| `UruFacturaCaes` | CAE con su `UltimoNroUsado`, que avanza con un `UPDATE` condicional en la misma transacción que el `INSERT` del CFE: si otra instancia numeró antes, no se guarda nada y se vuelve a numerar. |
| `UruFacturaCfes` | CFE emitidos: XML firmado, contenido (JSON), totales y estado ante la DGI (como texto: `Aceptado`, `Rechazado`, …). Tipo/serie/número únicos. |
| `UruFacturaSecuencias` | Número de envío del emisor (`Idemisor`), único entre instancias. |

```csharp
// Program.cs (ASP.NET Core): el proveedor lo elegís vos (SQL Server, PostgreSQL, SQLite, ...)
builder.Services.AddDbContextFactory<UruFacturaDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddSingleton<IUruFacturaClient>(sp => new UruFacturaClient(
    config,
    store: new EfUruFacturaStore<UruFacturaDbContext>(sp.GetRequiredService<IDbContextFactory<UruFacturaDbContext>>())));

// Reporte diario (una sola instancia, p. ej. un job después de medianoche)
await client.EnviarReporteDiarioAsync(HoraUruguay.Hoy().AddDays(-1));
```

- Si ya tenés tu propio `DbContext`, llamá a `modelBuilder.ApplyUruFacturaModel()` en `OnModelCreating` y usá
  `EfUruFacturaStore<MiContexto>`. Las migraciones se generan en tu aplicación (`dotnet ef migrations add UruFactura`).
- Las claves `Id` son `Guid` ordenados por tiempo (UUID v7; en SQL Server, el GUID secuencial de EF Core, porque
  SQL Server ordena `uniqueidentifier` por los últimos bytes) para que el índice primario no se fragmente.
- `FechaFirma` se guarda en UTC (PostgreSQL/Npgsql no admite otro offset en `timestamp with time zone`).
- `store.BuscarCfesAsync(nroInterno)` devuelve los CFE de un número interno. Para otras consultas (por
  ejemplo, los pendientes de resultado), las entidades son públicas: consultá `db.Cfes` y usá
  `entidad.ToCfeEmitido()` (por ejemplo, para `client.ConsultarCfeAsync`).
- Otro almacenamiento: implementá `IUruFacturaStore`; `RegistrarEmisionAsync` debe avanzar el CAE y guardar
  el CFE de forma atómica (las dos cosas o ninguna).

### Inyección de dependencias y `HttpClient`

`UruFacturaClient` es thread-safe: registralo como singleton (como `IUruFacturaClient`, que además permite
mockearlo en tests). En APIs de alto tráfico podés pasarle un `HttpClient` de `IHttpClientFactory`
(`new UruFacturaClient(config, httpClient: …)`); en ese caso su handler debe tener el certificado de cliente
TLS y la validación del servidor configurados. Para tests o un transporte propio, `dgiClient:` acepta un
`IDgiClient`, y `reloj:` un `TimeProvider` para las fechas fiscales.

---

## 📄 Tipos de CFE

| Código | Tipo |
|--------|------|
| 101 / 102 / 103 | e-Ticket, nota de crédito, nota de débito |
| 111 / 112 / 113 | e-Factura, nota de crédito, nota de débito |
| 121 / 122 / 123 | e-Factura de exportación y sus notas |
| 124 | e-Remito de exportación |
| 181 | e-Remito |
| 182 | e-Resguardo |
| 131-133, 141-143 | e-Ticket y e-Factura venta por cuenta ajena (exigen `Mandante`) |
| 201-203, 211-213, 221-224, 231-233, 241-243, 281, 282 | Comprobantes de contingencia (CFC) |

> **No soportados:** e-Boleta de entrada (151-153 y 251-253), la adenda y los descuentos/recargos globales en
> porcentaje (solo en monto). Los e-Remitos no tienen notas de crédito/débito.

### Tasas de IVA (`TipoIva`)

| Valor | Tasa |
|-------|------|
| `TipoIva.Exento` | 0 % |
| `TipoIva.Minimo` | 10 % |
| `TipoIva.Basico` | 22 % |
| `TipoIva.Suspendido` | IVA en suspenso |

---

## ⚠️ Excepciones

| Excepción | Cuándo |
|-----------|--------|
| `UruFacturaException` | Base de todas; configuración inválida, Producción sin almacenamiento, sobre con más de 250 CFE |
| `CfeValidationException` | El CFE no es válido (`Errores`: campo y motivo de cada uno); no se consumió ningún número |
| `CfeDuplicadoException` | Ya se emitió un CFE del mismo tipo con el mismo `NroInterno` (`Existente`) |
| `CaeException` | CAE inválido o superpuesto con otro al registrarlo, o no hay CAE vigente con números para el tipo |
| `FirmaDigitalException` | El certificado no se puede cargar (archivo, contraseña) o no tiene clave privada; falla de firma |
| `DgiCommunicationException` | Red, timeout, SOAP Fault o HTTP sin acuse (`CodigoRespuesta`: `SOAP_FAULT` o el código HTTP) |
| `PdfGenerationException` | Falla al generar el PDF (`UruFactura.Pdf`) |

---

## 🔄 Cambios respecto de 1.x

| 1.x | 2.0 |
|---|---|
| `UruFactura` (con PDF) / `UruFactura.Lite` | `UruFactura` (sin PDF) + `UruFactura.Pdf` |
| `UruFactura.Lite.EntityFrameworkCore` | `UruFactura.EntityFrameworkCore` (uno solo) |
| `using UruFactura.Models; .Enums; .Configuration; …` | `using UruFactura;` |
| `RutEmisor`, `RazonSocialEmisor`, … en la config | `config.Emisor` (`Emisor`) |
| `client.CrearETicket()` + `cfe.Detalle.Add(…)` | `new Cfe(TipoCfe.ETicket) { Detalle = [ … ] }` |
| `AsignarCae` / `AsignarCaeAsync` + `EnviarCfeAsync` | `EmitirAsync` (devuelve `CfeEmitido`) + `EnviarAsync` |
| `client.Cae.RegistrarCae`, `CargarCaesAsync`, `ICaeRepository`, `ICaeRepositoryCompartido`, `ICfeRepository` | `RegistrarCaeAsync` + `IUruFacturaStore` (`InMemoryStore`, `EfUruFacturaStore`) |
| `Cae.NroSerie` | se identifica por tipo, serie y rango |
| `RespuestaDgi.Exitoso` | `AcuseSobre.Recibido`, `EstadoEnvio.TodosAceptados`, `CfeEmitido.AceptadoPorDgi` |
| `SOAP_FAULT` / HTTP en `RespuestaDgi.Codigo` | `DgiCommunicationException` |
| `EnviarReporteDiarioAsync(DateTime, cfes)` | `EnviarReporteDiarioAsync(DateOnly)` (desde el almacenamiento) |
| `client.GenerarPdfA4(cfe)` | `CfePdf.GenerarA4(emitido)` |
| `UruFacturaClientBuilder` | `new UruFacturaClient(config, store:, httpClient:, dgiClient:, reloj:)` |
| `ICfeXmlBuilder`, `ICfeFirmante`, `ICfeQrGenerator`, `IDgiSoapClient` | internos; `IDgiClient` para un transporte propio; `Certificado` para Key Vault/HSM |
| `TipoDocumentoReceptor.DocumentoExterior` | `TipoDocumentoReceptor.Otro` |
| `CfeFirmante.VerificarFirma(xml)` (`bool`) | `FirmaDigital.VerificarFirma(xml)`: devuelve el certificado firmante, para comprobar que es del emisor |
| `CfeValidationException.Errors` (texto) | `CfeValidationException.Errores` (`ErrorValidacion`: campo y motivo) |
| Estados de la DGI como texto (`"AE"`, `"AS"`, …) | `EstadoCfe`, `EstadoSobre`, `EstadoReporte` |

---

## 🔧 Requisitos

- .NET 10
- Certificado digital (`.p12`/`.pfx`) de una CA reconocida por AGESIC, vinculado al RUT del emisor
- CAE vigentes emitidos por la DGI

## 📚 Documentación adicional

| Documento | Descripción |
|-----------|-------------|
| [FACTURACION_URUGUAY.md](docs/FACTURACION_URUGUAY.md) | Marco normativo, ejemplos por tipo de empresa y consideraciones fiscales |
| [CERTIFICACION_DGI.md](docs/CERTIFICACION_DGI.md) | Homologación y puesta en producción paso a paso |
| [NORMATIVA_DGI_UPDATE.md](docs/NORMATIVA_DGI_UPDATE.md) | Runbook para actualizar el SDK ante cambios de formato o normativa |

## 🧪 Tests

```bash
dotnet test UruFactura.slnx
```

Los tests validan el XML generado contra los XSD y WSDL oficiales de la DGI (`tests/UruFactura.Tests/Schemas/dgi`).

## 📝 Licencia

MIT — ver [LICENSE](LICENSE)
