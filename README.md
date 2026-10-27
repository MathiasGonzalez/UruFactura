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

UruFactura es una librería open-source **.NET 10** para emitir Comprobantes Fiscales Electrónicos (CFE) ante la DGI: arma el XML según los esquemas oficiales, lo firma, administra la numeración de los CAE, habla con los servicios SOAP de la DGI, envía el reporte diario e intercambia los CFE con otros emisores electrónicos. Vos describís el comprobante; UruFactura se ocupa del resto.

---

## 🚀 Características

| Característica | Detalle |
|---|---|
| **Todos los CFE** | e-Ticket, e-Factura, exportación, e-Remito, e-Resguardo, e-Boleta de entrada, sus notas, venta por cuenta ajena y contingencia, con los 17 indicadores de facturación (exento, tasas, otra tasa/fictos, entrega gratuita, no facturables, ítems a rebajar o anular, exportación y asimiladas, impuesto percibido, IVA en suspenso, los de la e-Boleta, IVA mínimo/Monotributo) y todas las zonas del formato: retenciones y percepciones, subtotales informativos, descuentos y recargos, medios de pago, referencias y complemento fiscal. XML validado contra los XSD oficiales (`xsds_fe` 1.44.2, formato CFE v25.2). |
| **Reglas del formato** | Lo que la DGI controla además del XSD: identificación del receptor según el tipo y el tope de 5.000 UI, dígitos verificadores (RUT, C.I., GTIN), país de cada documento, montos con IVA o IMEBA incluido, secreto profesional, pagos por cuenta de terceros, cobranzas propias, bienes de terceros, compra de moneda extranjera y modalidades de venta. |
| **CAE especiales** | Contribuyentes exonerados, IVA mínimo, Monotributo y Monotributo Social MIDES: `CAEEspecial`, `MntBruto` 3 y las leyendas de la representación impresa. |
| **Firma digital** | XML Signature *enveloped* del CFE, del reporte y de los acuses, y **WS-Security** en cada mensaje SOAP, con un certificado `.p12`/`.pfx` o un `X509Certificate2` (Key Vault, HSM). |
| **Numeración con CAE** | El número se asigna en la misma operación atómica que registra el CFE: no se repite (tampoco con varias instancias) ni queda consumido sin comprobante. Alertas de vencimiento y de rango. |
| **DGI** | Envío de sobres `EnvioCFE`, consulta del resultado, consulta por comprobante, consulta del padrón de emisores electrónicos y reporte diario (formato v13.2: montos en pesos, anulados por rechazo, e-Tickets sobre el tope), con respuestas tipadas. |
| **Intercambio** | Sobre al receptor electrónico con adenda, recepción de sobres de proveedores con sus controles, acuses `ACKSobre` y `ACKCFE` firmados, comunicación de anulación y envío al servicio web de intercambio del otro emisor. |
| **Representación impresa** | PDF A4 y térmico (80 mm) con QR, recuadros "CONSUMO FINAL"/"RUC COMPRADOR", forma de pago, leyendas de los CAE especiales, adenda y pie normativo, en el paquete `UruFactura.Pdf`. |

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

    // UI al 31/12 de cada año (se agrega una por año): la del año anterior a cada comprobante fija el tope
    // de 5.000 UI de los e-Tickets. Obligatoria en Producción.
    UnidadIndexada = new Dictionary<int, decimal> { [2025] = 6.3m },

    // En Producción son obligatorias (la DGI las entrega al habilitar el ambiente).
    // UrlEnvio     = "https://…/ws_efactura",
    // UrlConsultas = "https://…/ws_consultas",
    // Otras opciones: AlgoritmoFirma (Sha256 por defecto), Timeout (30 s),
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
`EliminarCaeAsync`. Si la DGI lo entregó como CAE especial, se indica con `Especial`
(`CaeEspecial.Exonerado` con su `CausalEspecial`, `IvaMinimo`, `Monotributo` o `MonotributoSocialMides`):
los CFE lo informan y las reglas de esos contribuyentes se validan al emitir. La numeración la lleva el almacenamiento: el en memoria se pierde al reiniciar, por
eso en Producción el cliente exige [uno durable](#-producción-y-varias-instancias).

### 3. Emitir, enviar y consultar

```csharp
var cfe = new Cfe(TipoCfe.ETicket)
{
    Detalle =
    [
        new LineaDetalle { NombreItem = "Servicio de consultoría", Cantidad = 1, PrecioUnitario = 1000m, IndFact = IndicadorFacturacion.TasaBasica },
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
- Se envían a la DGI las e-Facturas, los e-Remitos, los e-Resguardos, las e-Boletas de entrada y sus notas, y los
  e-Tickets (y sus notas) con monto neto mayor a 5.000 UI o que documentan retenciones o percepciones. Los
  e-Tickets por debajo del tope no es obligatorio enviarlos uno a uno: se informan en el reporte diario
  (`EnviarAsync` igual los acepta).
- Un sobre lleva hasta 250 CFE y 2 MB (el tamaño máximo del formato), firmados con el mismo certificado: la
  carátula lleva el de sus firmas, aunque se haya renovado después.
- Un CFE que la DGI rechazó queda anulado (`AnuladoPorRechazo`): se emite otro y, si ya se le había enviado al
  receptor electrónico, se le comunica (ver [Intercambio](#7-intercambio-con-otros-emisores-electrónicos)).

### 4. Representación impresa (`UruFactura.Pdf`)

```csharp
byte[] a4      = CfePdf.GenerarA4(emitido);
byte[] ticket  = CfePdf.GenerarTermico(emitido);   // 80 mm
byte[] qrPng   = CfePdf.GenerarQr(emitido);
```

Incluye lo que pide el formato de la representación impresa: RUC, tipo (con el nombre de la tabla A del formato),
serie, número y forma de pago en la identificación del comprobante; el recuadro "CONSUMO FINAL" en los e-Tickets
(con el documento del receptor si se informó), "RUC COMPRADOR" en las operaciones entre contribuyentes y "RUC
VENDEDOR" en la e-Boleta de entrada; las leyendas de los CAE especiales ("Contribuyente IVA MINIMO",
"MONOTRIBUTO", "Contribuyente Exonerado" con `Emisor.NormaExoneracion`), "Bienes de Terceros" con el propietario,
"Pagos por cuenta de terceros", "Cobranza" y la fecha valor; y en el pie el QR, el código de seguridad, la fecha
del emisor, la constancia de IVA al día, el CAE con su rango y el recuadro del vencimiento. El detalle se adapta al
tipo: el e-Remito sin precios, la exportación sin IVA y con la posición arancelaria (NCM), los contribuyentes IVA
mínimo sin mención al IVA, el e-Resguardo con sus retenciones, los ítems a rebajar o anular y los no facturables
negativos con el signo, las retenciones y percepciones por código y las notas con el comprobante que corrigen; la
moneda extranjera se indica con su código y el tipo de cambio, y la adenda va al final en su zona. Para un diseño propio, `emitido.UrlQr` tiene el contenido del QR sin necesidad del paquete de PDF.

### 5. Reporte diario

```csharp
// Resumen firmado (montos, cantidades y rangos por tipo) de los CFE emitidos en la fecha,
// tomados del almacenamiento. Usá la fecha de Uruguay: el servidor puede estar en UTC.
AcuseReporte reporte = await client.EnviarReporteDiarioAsync(HoraUruguay.Hoy().AddDays(-1));
Console.WriteLine($"{reporte.CantidadCfe} CFE → {reporte.Estado} {reporte.Mensaje}");
```

El reporte sigue el formato v13.2: resume los CFE **firmados** en la fecha (en Uruguay; los montos van agrupados
por fecha de comprobante y sucursal, y aparte los pagos por cuenta de terceros), expresa los montos en **pesos**
(con el tipo de cambio de cada CFE, que por eso debe ser el fiscal: el interbancario comprador del BCU al cierre
del día anterior), informa lo retenido o percibido, como **anulados** los CFE que la DGI rechazó y, si se configuró
`UnidadIndexada`, cuántos e-Tickets superaron el tope (`CantDocsMay_topeUI`). Como un rechazo puede llegar después de enviado el reporte, en ese caso se
reenvía corregido.

El primer envío de una fecha usa la secuencia 1 (`secEnvio`); para corregir un reporte ya recibido se
reenvía completo con la siguiente (`secEnvio: 2`, …). Los CFE se leen de a uno, sin cargarlos todos en
memoria. Si el almacenamiento no tiene completos los de esa fecha (el en memoria, tras un reinicio), falla
en lugar de declarar cero comprobantes.

### 6. Otros comprobantes

```csharp
// e-Factura: receptor con RUT, nombre y domicilio
var factura = new Cfe(TipoCfe.EFactura)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A.", Direccion = "Av. Italia 1234", Ciudad = "Montevideo" },
    Detalle  = [new LineaDetalle { NombreItem = "Licencia", Cantidad = 1, PrecioUnitario = 5000m }],
};

// Precios con IVA incluido (MntBruto 1): los netos se obtienen dividiendo por (1 + tasa)
var conIva = factura with { MontosBrutos = MontosBrutos.IvaIncluido };

// Agente de percepción: retenciones o percepciones por línea (códigos de la DGI) con el indicador de agente
var conPercepcion = factura with
{
    Detalle = [new LineaDetalle
    {
        NombreItem = "Gasoil", Cantidad = 1000, PrecioUnitario = 50m, AgenteResponsable = AgenteResponsable.Producto,
        Retenciones = [new RetencionPercepcion { Codigo = "2183219", MontoSujeto = 50_000m, Valor = 1_500m }],
    }],
};

// Nota de crédito: referencia con monto y moneda del original (obligatorios desde CFE v25) y, si no es en
// pesos, su tipo de cambio (TipoCambioCfeRef). La fecha (FechaCfe) es opcional; si se informa, la DGI la controla.
var nc = new Cfe(TipoCfe.NotaCreditoETicket)
{
    Referencias = [new RefCfe
    {
        TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 42, FechaCfe = new DateOnly(2026, 6, 15),
        MontoCfeRef = 1220m, MonedaCfeRef = Moneda.PesoUruguayo, Razon = "Devolución",
    }],
    Detalle = [new LineaDetalle { NombreItem = "Devolución", Cantidad = 1, PrecioUnitario = 1000m }],
};

// Referencia global: cuando no se puede identificar el CFE (un documento no codificado, una liquidación)
var global = new RefCfe { Global = true, Razon = "Descuento por volumen de mayo" };

// Contribuyente IVA mínimo o Monotributo: CAE especial y líneas IvaMinimoMonotributo (sin IVA)
// await client.RegistrarCaeAsync(cae with { Especial = CaeEspecial.IvaMinimo });
var ticketIvaMinimo = new Cfe(TipoCfe.ETicket)
{
    Detalle = [new LineaDetalle { NombreItem = "Almuerzo", Cantidad = 1, PrecioUnitario = 450m, IndFact = IndicadorFacturacion.IvaMinimoMonotributo }],
};

// Exportación: el receptor con nombre, domicilio, departamento o estado y país; cualquier moneda ISO 4217
var exportacion = new Cfe(TipoCfe.EFacturaExportacion)
{
    Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m,
    ClausulaVenta = "FOB", ModalidadVenta = 1, ViaTransporte = 1,
    Receptor = new Receptor
    {
        Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "US", RazonSocial = "Importer Inc.",
        Direccion = "5th Avenue 100", Ciudad = "New York", Departamento = "NY", Pais = "Estados Unidos",
    },
    Detalle = [new LineaDetalle { NombreItem = "Carne", Ncm = "0201.30.00", Cantidad = 1, PrecioUnitario = 8000m }],
};

// e-Remito: identifica al receptor (con cualquier documento). TrasladoPorVenta se informa como TipoTraslado 1;
// el resto como 2 = traslado interno. Bienes de terceros: Propietario.
var remito = new Cfe(TipoCfe.ERemito)
{
    IndTraslado = IndTraslado.TrasladoPropio,
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Depósito S.A.", Direccion = "Ruta 1 km 20", Ciudad = "Ciudad del Plata" },
    Detalle = [new LineaDetalle { NombreItem = "Cajas", Cantidad = 50 }],
};

// Rebaja de un e-Remito ya emitido: ítems con RebajaRemito (8) y la referencia al remito que ajustan
var rebaja = remito with
{
    Detalle = [new LineaDetalle { NombreItem = "Cajas", Cantidad = 5, IndFact = IndicadorFacturacion.RebajaRemito }],
    Referencias = [new RefCfe { TipoCfe = TipoCfe.ERemito, Serie = "A", NroCfe = 12 }],
};

// e-Resguardo: las retenciones van en las líneas (códigos numéricos de la DGI: formulario y línea). Para anular
// ítems de otro resguardo: IndFact = AnulacionResguardo (9) y la referencia; FechaValor para organismos del Estado.
var resguardo = new Cfe(TipoCfe.EResguardo)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." },
    Detalle  = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "1146171", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m }] }],
};

// e-Boleta de entrada: la emite quien compra; el Receptor es el vendedor. El indicador dice quién vende:
// VendedorNoContribuyente (13), VendedorIvaMinimo (14) o VendedorImeba (15); la compra de moneda extranjera
// para reventa usa CompraMonedaExtranjera (17) con Cfe.CompraMonedaExtranjera.
var boleta = new Cfe(TipoCfe.EBoleta)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Productor Rural", Direccion = "Ruta 5 km 30", Ciudad = "Canelones" },
    Detalle  = [new LineaDetalle { NombreItem = "Lana", Cantidad = 100, PrecioUnitario = 80m, IndFact = IndicadorFacturacion.VendedorImeba }],
};

// Secreto profesional: el receptor puede omitirse; su identificación va en la adenda
var reservado = new Cfe(TipoCfe.EFactura)
{
    SecretoProfesional = true,
    Adenda = "Receptor: Juan Pérez, C.I. 1.234.567-2",
    Detalle = [new LineaDetalle { NombreItem = "Honorarios", Cantidad = 1, PrecioUnitario = 3000m }],
};

// Venta por cuenta ajena y contingencia: mismo modelo, otro tipo
var cuentaAjena = new Cfe(TipoCfe.ETicketCuentaAjena) { Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A." }, Detalle = [/* … */] };
var contingencia = new Cfe(TipoCfe.ETicketContingencia) { Detalle = [/* … */] };
```

Además: `DescuentosRecargos` (globales, en monto o en `Porcentaje` de la base de su indicador; se informan
como monto y cambian la base del IVA), `SubtotalesInformativos` (no cambian los totales), `MediosPago`,
`NroInterno`, `FormaPago`, `FechaVencimiento`, `PeriodoDesde`/`PeriodoHasta`, `InfoAdicional`, `Adenda`
(información comercial para el receptor: no se firma ni se envía a la DGI), `IvaOtraTasa` (el IVA de las líneas
`OtraTasa`, que el formato no calcula), `PagoCuentaTerceros` (reintegros de gastos), `CobranzaPropia` (líneas
no facturables que cobran otros comprobantes), `ModalidadVenta` (90 exportación de servicios en e-Facturas y
e-Tickets, 80 Tax Free en e-Tickets), datos opcionales del `Receptor` y de cada `LineaDetalle` (`Descripcion`,
`Codigos`, `DescuentoMonto`/`DescuentoPorcentaje`, `RecargoMonto`/`RecargoPorcentaje`, `Subdescuentos`/
`Subrecargos`, una `Cantidad` negativa para eliminar un ítem del propio comprobante). Si el emisor o el mandante
no tienen la constancia de IVA al día: `Emisor.IvaAlDia = false` / `Mandante.IvaAlDia = false`. Para emitir con
otra sucursal: `Emisor = config.Emisor with { CodigoSucursal = 3 }`. `Moneda` admite cualquier código de la tabla
de la DGI (`new Moneda("CLP")`, `Moneda.UnidadIndexada`, …).

### 7. Intercambio con otros emisores electrónicos

Todo emisor electrónico es también receptor electrónico: envía sus CFE a los clientes que son emisores (después
de enviarlos a la DGI) y responde los que recibe. El medio lo acuerdan las partes (como mínimo, correo con los
archivos adjuntos con el nombre que indica la DGI; los proveedores habilitados deben usar el servicio web).

```csharp
// ¿El cliente es receptor electrónico? (padrón de la DGI: correo y servicio web de intercambio)
var cliente = await client.ConsultarEmisorElectronicoAsync("214844360018");

// Emisor: sobre con los CFE del cliente y su adenda (sobre.NombreArchivo: Sob_RUC_AAAAMMDD_Id.xml)
SobreReceptor sobre = await client.GenerarSobreReceptorAsync([factura]);
if (cliente?.UrlServicioWeb is { } url)
    await client.EnviarIntercambioAsync(url, OperacionIntercambio.Sobre, sobre.Xml);   // o por correo

// Emisor: lee el acuse del sobre y la respuesta a cada comprobante (aceptación o rechazo comercial)
AcuseSobre acuseReceptor = client.LeerAcuseReceptor(xmlAckSobre);       // acuseReceptor.IdEmisor == sobre.IdEmisor
EstadoEnvio respuesta = client.LeerRespuestaReceptor(xmlAckCfe);

// Emisor: si la DGI rechaza un CFE ya enviado al receptor, se le comunica la anulación
NotificacionAnulacion aviso = await client.GenerarNotificacionAnulacionAsync([rechazado]);

// Receptor: recibe el sobre de un proveedor, lo controla (S01-S07; firma, CAE y formato de cada CFE)
// y devuelve el acuse firmado (recibido.XmlAcuse, recibido.NombreArchivoAcuse)
SobreRecibido recibido = await client.RecibirSobreAsync(xmlSobre, nombreArchivoAdjunto);
// Receptor: responde cada comprobante; los motivos comerciales son E20 a E60
RespuestaCfes respuestaCfes = await client.ResponderCfesAsync(recibido, new Dictionary<int, IReadOnlyList<MotivoRechazo>>
{
    [2] = [MotivoRechazo.DiferenciaDePrecios("No es el precio de lista")],
});
```

Los acuses y la comunicación de anulación van firmados, y al leer los de otro emisor se verifica su firma y
que sean para el propio RUT (quien recibe debe además comprobar, si lo necesita, que el certificado firmante
sea del otro emisor). Detectar sobres repetidos (mismo RUT emisor y número) le corresponde a la aplicación, que
es la que los conserva.

### 8. Validaciones locales

`client.Validar(cfe)` (y `EmitirAsync`, antes de numerar) aplica las restricciones de los XSD de la DGI
(largos, rangos de montos, cantidades y fechas, series, códigos de país y de moneda, caracteres de control) y las
reglas del formato v25.2: documentos con su país y dígito verificador (RUT, C.I.; el NIE lo controla la DGI),
identificación del receptor según el tipo (RUT, nombre y domicilio en la e-Factura; documento en el e-Remito, el
e-Resguardo y la e-Boleta; domicilio, departamento y país en exportación; documento en el e-Ticket sobre el tope
o con retenciones; nada con secreto profesional, que lleva la identificación en la adenda), datos de exportación
y de remito, mandante en cuenta ajena (distinto del emisor), referencias de las notas, de las cobranzas propias y
de las rebajas y anulaciones (monto, moneda y tipo de cambio del comprobante referenciado, sin repetidas,
referencia global con su motivo), indicadores de facturación según el tipo (entregas gratuitas con precio cero y
el resto con precio mayor a cero, IVA a otra tasa informado, IVA mínimo sin líneas gravadas, los de la e-Boleta
según el vendedor), retenciones con código numérico y su indicador de agente, códigos GTIN, fechas hasta 60 días
posteriores a hoy, descuentos que no dejen montos negativos y, si configurás `UnidadIndexada`, e-Tickets sobre
5.000 UI sin identificar al receptor. Las reglas del CAE especial (exonerado, IVA mínimo, Monotributo) se validan al emitir,
con el CAE que numera. Todo lo que pasa la
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
| `UruFacturaCfes` | CFE emitidos: XML firmado, contenido (JSON), totales, fecha del reporte (la de la firma) y estado ante la DGI (como texto: `Aceptado`, `Rechazado`, …). Tipo/serie/número únicos. |
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
| 131-133, 141-143 | e-Ticket y e-Factura venta por cuenta ajena (exigen `Mandante`, salvo con secreto profesional) |
| 151 / 152 / 153 | e-Boleta de entrada y sus notas (el `Receptor` es el vendedor) |
| 201-203, 211-213, 221-224, 231-233, 241-243, 251-253, 281, 282 | Comprobantes de contingencia (CFC) |

> Los e-Remitos no tienen notas de crédito/débito: un remito rebaja ítems de otro con el indicador 8, y un
> e-Resguardo anula ítems de otro con el 9. La e-Boleta no se envía al vendedor (no es emisor electrónico), salvo
> en la compra de moneda extranjera. El contenido cifrado del complemento fiscal (`xenc`) no se genera: el
> mandante se informa en claro, como admite el formato.

### Indicadores de facturación (`IndicadorFacturacion`)

| Valor | Código | Total en el que se informa |
|-------|--------|----------------------------|
| `Exento` | 1 | No gravado |
| `TasaMinima` | 2 | Neto e IVA a la tasa mínima (10 %) |
| `TasaBasica` | 3 | Neto e IVA a la tasa básica (22 %) |
| `OtraTasa` | 4 | Neto a otra tasa/IVA sobre fictos; el IVA se informa en `Cfe.IvaOtraTasa` |
| `EntregaGratuita` | 5 | Ninguno (ítem con precio cero; en el e-Remito de exportación, ítem de valor cero) |
| `NoFacturable` / `NoFacturableNegativo` | 6 / 7 | Monto no facturable (suma o resta del total a pagar) |
| `RebajaRemito` | 8 | e-Remitos: ítem a rebajar de otro remito (resta en el de exportación) |
| `AnulacionResguardo` | 9 | e-Resguardo: ítem a anular de otro resguardo (sus retenciones restan) |
| `ExportacionYAsimiladas` | 10 | Exportación y asimiladas (todas las líneas de los tipos de exportación) |
| `ImpuestoPercibido` | 11 | Impuesto percibido |
| `IvaEnSuspenso` | 12 | IVA en suspenso (en la e-Boleta, con montos que incluyen IMEBA) |
| `VendedorNoContribuyente` / `VendedorIvaMinimo` / `VendedorImeba` | 13 / 14 / 15 | e-Boleta: no gravado, según quién vende (14 y 15 con RUT) |
| `IvaMinimoMonotributo` | 16 | No gravado (contribuyentes IVA mínimo y Monotributo, o mandante IVA mínimo) |
| `CompraMonedaExtranjera` | 17 | e-Boleta: compra de moneda extranjera para reventa (no gravado) |

---

## ⚠️ Excepciones

| Excepción | Cuándo |
|-----------|--------|
| `UruFacturaException` | Base de todas; configuración inválida, Producción sin almacenamiento, sobre con más de 250 CFE o de 2 MB, CFE que no se pueden enviar juntos a un receptor, mensaje de intercambio con firma inválida o que no es para este RUT |
| `CfeValidationException` | El CFE no es válido (`Errores`: campo y motivo de cada uno); no se consumió ningún número |
| `CfeDuplicadoException` | Ya se emitió un CFE del mismo tipo con el mismo `NroInterno` (`Existente`) |
| `CaeException` | CAE inválido o superpuesto con otro al registrarlo, o no hay CAE vigente con números para el tipo |
| `FirmaDigitalException` | El certificado no se puede cargar (archivo, contraseña) o no tiene clave privada; falla de firma |
| `DgiCommunicationException` | Red, timeout, SOAP Fault o HTTP sin acuse (`CodigoRespuesta`: `SOAP_FAULT` o el código HTTP) |
| `PdfGenerationException` | Falla al generar el PDF (`UruFactura.Pdf`) |

---

## 🔄 Cambios respecto de 2.0.0

| 2.0.0 | Ahora |
|---|---|
| `TipoIva` (`Exento`, `Minimo`, `Basico`, `Suspendido`) y `LineaDetalle.IndFactIva` | `IndicadorFacturacion` (`Exento`, `TasaMinima`, `TasaBasica`, `IvaEnSuspenso`, …, con el código de la DGI) y `LineaDetalle.IndFact` |
| `Totales.MontoNetoExento` | `Totales.MontoNoGravado` (incluye el indicador 16); nuevos `MontoNoFacturable`, `MontoPagar`, `MontoImpuestoPercibido`, `MontoNetoOtraTasa`, `IvaOtraTasa`, `MontoCreditoFiscal` |
| `RefCfe.TipoCfe`, `Serie`, `NroCfe` y `FechaCfe` obligatorios | opcionales (`Global` para la referencia global; la fecha, opcional) |
| El reporte diario resumía por fecha de emisión, en la moneda de cada CFE | Por fecha de firma, en pesos, con anulados y e-Tickets sobre el tope; `IUruFacturaStore.ListarCfesAsync` lista por `CfeEmitido.FechaReporte` |
| `ValorUnidadIndexada` opcional | `UnidadIndexada` (UI al 31/12 de cada año, obligatoria en Producción): el tope sale del año anterior a cada comprobante |
| `Moneda` enum (UYU, USD, EUR) | `Moneda` struct con cualquier código ISO 4217 de la tabla de la DGI (`new Moneda("BRL")`); se guarda como el código |
| `Totales.MontoPagar` calculado (total + no facturable) | Lo informa `CalcularTotales`: total + retenido/percibido + no facturable |
| Fecha de emisión hasta hoy | Hasta 60 días posteriores (como la DGI) |
| e-Factura con receptor solo con RUT; e-Remito sin receptor | El receptor según el formato: nombre y domicilio en la e-Factura, documento en el e-Remito, domicilio, departamento y país en exportación |
| `MntBruto` 3 en ventas por cuenta ajena de un mandante IVA mínimo | Sin `MntBruto` (la DGI rechaza el 3 por cuenta ajena, K-C1) |
| Códigos de retención de texto libre | Numéricos de hasta 8 dígitos (los de la DGI) |
| `IDgiClient` con 4 operaciones | Además `ConsultarEmisoresAsync` y `EnviarIntercambioAsync` |
| La firma incluía solo el certificado en `KeyInfo` | Además `X509IssuerSerial` (emisor y número de serie, zona I del formato) |
| `FirmaDigital.VerificarFirma(xml)` exigía el certificado en la firma | `VerificarFirma(xml, certificadoDelSobre)`: una firma con solo `X509IssuerSerial` se verifica con el certificado de la carátula, y los sobres recibidos por intercambio lo usan |
| EF Core: tablas de 2.0.0 | Columnas nuevas `UruFacturaCfes.FechaReporte` (con índice, en lugar del de `FechaEmision`) y `UruFacturaCaes.Especial`/`CausalEspecial`: generá una migración |

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
| [CERTIFICACION_DGI.md](docs/CERTIFICACION_DGI.md) | Homologación (incluidas las pruebas con adenda y de intercambio) y puesta en producción paso a paso |
| [NORMATIVA_DGI_UPDATE.md](docs/NORMATIVA_DGI_UPDATE.md) | Runbook para actualizar el SDK ante cambios de formato o normativa |

## 🧪 Tests

```bash
dotnet test UruFactura.slnx
```

Los tests validan el XML generado contra los XSD y WSDL oficiales de la DGI (`tests/UruFactura.Tests/Schemas/dgi`).

Las pruebas de `SimuladorDgiTests.cs` emiten, envían y consultan cada tipo de CFE, notas con referencia y el reporte diario contra [dgi-simulador](https://github.com/MathiasGonzalez/dgi-simulador), que responde como los servicios de la DGI. Fallan si un CFE no es aceptado o si el simulador deja una advertencia. Sin la variable `DGI_SIMULADOR_URL` se saltean:

```bash
docker run -d -p 8080:8080 ghcr.io/mathiasgonzalez/dgi-simulador
DGI_SIMULADOR_URL=http://localhost:8080 dotnet test tests/UruFactura.Tests --filter Categoria=SimuladorDgi
```

En CI las corre [simulador-dgi.yml](.github/workflows/simulador-dgi.yml), que deja en el resumen del job lo que recibió el simulador. No es un check obligatorio: usa la última versión del simulador, que puede sumar controles.

## 📝 Licencia

MIT — ver [LICENSE](LICENSE)
