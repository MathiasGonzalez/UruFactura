# Facturación Electrónica en Uruguay — Guía Concisa

Referencia rápida sobre el sistema de Comprobantes Fiscales Electrónicos (CFE) regulado por la **DGI** (Dirección General Impositiva), con ejemplos prácticos por tipo de empresa y operación.

---

## Marco normativo

| Norma | Descripción |
|---|---|
| Resolución DGI 798/012 | Marco general de la facturación electrónica |
| Resolución DGI 2548/023 | Universalización — todos los contribuyentes IVA desde enero 2025 |
| Decreto 36/020 | Ampliación de obligados a emitir CFE |
| Decreto 230/021 | Calendario de adhesión obligatoria por facturación |
| e-CFE v25.2 | Documento de formato vigente (el XML usa `version="1.0"` y los XSD 1.44.2) |

La **adhesión es obligatoria** para todos los contribuyentes de IVA (incluido IVA mínimo) desde enero 2025 (Resolución 2548/023).

---

## Comprobantes disponibles (CFE)

| Código | Tipo | Uso |
|--------|------|-----|
| **101** | e-Ticket | Venta a consumidor final (sin RUT del receptor) |
| **102** | Nota de Crédito e-Ticket | Anulación / devolución de e-Ticket |
| **103** | Nota de Débito e-Ticket | Ajuste en más sobre e-Ticket |
| **111** | e-Factura | Venta a empresa o profesional con RUT |
| **112** | Nota de Crédito e-Factura | Anulación / devolución de e-Factura |
| **113** | Nota de Débito e-Factura | Ajuste en más sobre e-Factura |
| **121** | e-Factura de Exportación | Operaciones de exportación de bienes/servicios |
| **122** | Nota de Crédito e-Factura Exportación | Anulación / devolución de e-Factura de Exportación |
| **123** | Nota de Débito e-Factura Exportación | Ajuste en más sobre e-Factura de Exportación |
| **124** | e-Remito de Exportación | Traslado de mercadería para exportación |
| **181** | e-Remito | Traslado de mercadería sin transferencia de dominio |
| **182** | e-Resguardo | Retención/percepción de impuestos a proveedores |
| **131-133 / 141-143** | e-Ticket / e-Factura venta por cuenta ajena | Venta en nombre de un mandante (requiere `Mandante`) |
| **151-153** | e-Boleta de entrada y sus notas | La emite quien compra a un vendedor que no factura electrónicamente (no contribuyente, IVA mínimo, Monotributo o IMEBA) o en la compra de moneda extranjera para reventa |
| **2xx** | Comprobantes de contingencia | Misma estructura que el CFE que sustituyen |

> La diferencia principal entre **e-Ticket** y **e-Factura** es si el receptor tiene o no RUT. Las empresas siempre deben exigir e-Factura para poder descontar el IVA compras.

---

## Tasas de IVA vigentes

| Tasa | Aplicación |
|------|-----------|
| **22 %** (básica) | Bienes y servicios en general |
| **10 %** (mínima) | Alimentos de la canasta básica, medicamentos, servicios médicos |
| **0 %** (exento) | Arrendamientos de inmuebles, servicios educativos, exportaciones |
| **Suspendido** | IVA temporalmente suspendido por disposición legal (sin cálculo de impuesto) |

Cada línea lleva su indicador de facturación (`IndicadorFacturacion`): además de las tasas, el formato prevé
otra tasa o IVA sobre fictos (con el IVA informado en `Cfe.IvaOtraTasa`), entregas gratuitas, conceptos no
facturables (positivos o negativos: suman o restan del total a pagar), exportación y asimiladas, impuesto
percibido, las ventas de contribuyentes IVA mínimo o Monotributo (`IvaMinimoMonotributo`), que no discriminan
IVA y se numeran con un CAE especial (`Cae.Especial`), los ítems que rebajan un e-Remito (`RebajaRemito`) o
anulan un e-Resguardo (`AnulacionResguardo`) y los de la e-Boleta de entrada, que dicen quién vende
(`VendedorNoContribuyente`, `VendedorIvaMinimo`, `VendedorImeba`) o si es una compra de moneda extranjera para
reventa (`CompraMonedaExtranjera`).

Si los precios incluyen el IVA, `Cfe.MontosBrutos = MontosBrutos.IvaIncluido` (MntBruto 1): el SDK obtiene los
netos de cada tasa dividiendo por (1 + tasa), como indica el formato. Los agentes de retención o percepción
informan las retenciones y percepciones de cada línea (`LineaDetalle.Retenciones`, con el código de la DGI y
`AgenteResponsable`); se suman al total a pagar, salvo los créditos fiscales (formulario 2181).

---

## Ejemplos por tipo de empresa

Todos los ejemplos usan un `client` ya configurado (ver [README](../README.md)) con un CAE registrado para
cada tipo de CFE. `EmitirAsync` valida el comprobante, le asigna el número, lo firma y lo registra;
`EnviarAsync` lo manda a la DGI. Los precios del detalle son **sin IVA**: el SDK calcula el impuesto.

### 1. Comercio minorista (venta a consumidor final)

**Escenario:** ferretería vende materiales a una persona física sin RUT.

```csharp
// e-Ticket → consumidor final, sin datos del receptor
var eticket = await client.EmitirAsync(new Cfe(TipoCfe.ETicket)
{
    Detalle =
    [
        new LineaDetalle { NombreItem = "Pintura látex 20L", Cantidad = 2, PrecioUnitario = 890m, IndFact = IndicadorFacturacion.TasaBasica },
        new LineaDetalle { NombreItem = "Rodillo 23cm", Cantidad = 1, PrecioUnitario = 150m, IndFact = IndicadorFacturacion.TasaBasica },
    ],
});

var acuse = await client.EnviarAsync(eticket);
```

---

### 2. Servicios profesionales (empresa a empresa)

**Escenario:** estudio contable factura honorarios mensuales a una S.A.

```csharp
// e-Factura → receptor con RUT, puede deducir IVA
var efactura = await client.EmitirAsync(new Cfe(TipoCfe.EFactura)
{
    Receptor = new Receptor
    {
        Documento   = "211234560019",           // RUT con dígito verificador válido
        RazonSocial = "Importadora Sur S.A.",   // nombre, dirección y ciudad: obligatorios en la e-Factura
        Direccion   = "Rambla República de Chile 4030",
        Ciudad      = "Montevideo",
    },
    Detalle = [new LineaDetalle { NombreItem = "Honorarios contables — Mayo 2025", Cantidad = 1, PrecioUnitario = 15000m }],
});
```

---

### 3. Supermercado / alimentos con IVA diferenciado

**Escenario:** minimarket vende productos con tasas mixtas (básica y mínima).

```csharp
var eticket = await client.EmitirAsync(new Cfe(TipoCfe.ETicket)
{
    Detalle =
    [
        new LineaDetalle { NombreItem = "Detergente 1L", Cantidad = 3, PrecioUnitario = 85m, IndFact = IndicadorFacturacion.TasaBasica },          // 22 %
        new LineaDetalle { NombreItem = "Aceite girasol 900ml", Cantidad = 2, PrecioUnitario = 120m, IndFact = IndicadorFacturacion.TasaMinima },  // 10 %
        new LineaDetalle { NombreItem = "Ibuprofeno 400mg x20", Cantidad = 1, PrecioUnitario = 210m, IndFact = IndicadorFacturacion.Exento },  // exento
    ],
});
```

---

### 4. Empresa exportadora

**Escenario:** software factory cobra a cliente del exterior por desarrollo a medida.

```csharp
// e-Factura de Exportación → sin IVA (el SDK informa IndFact 10), con datos del receptor extranjero
var exportacion = await client.EmitirAsync(new Cfe(TipoCfe.EFacturaExportacion)
{
    Moneda         = Moneda.DolarAmericano,
    TipoCambio     = 40.5m,
    ClausulaVenta  = "N/A",  // Incoterm de 1-3 letras, o "N/A" para servicios
    ModalidadVenta = 90,     // Régimen general - exportación de servicios
    ViaTransporte  = 9,      // Otro
    Receptor = new Receptor
    {
        RazonSocial   = "TechCorp LLC",               // en exportación: nombre, domicilio, estado y país
        Direccion     = "100 Congress Ave",
        Ciudad        = "Austin",
        Departamento  = "Texas",
        Pais          = "Estados Unidos",
        CodigoPais    = "US",
        Documento     = "TX-123456",                 // documento extranjero
        TipoDocumento = TipoDocumentoReceptor.Nife,  // número de identificación fiscal extranjero (7)
    },
    Detalle = [new LineaDetalle { NombreItem = "Desarrollo de software — Sprint 12", Cantidad = 1, PrecioUnitario = 8000m }],
});
```

---

### 5. Nota de crédito (devolución parcial)

**Escenario:** cliente devuelve una unidad de un e-Ticket anterior.

> ⚠️ **Desde formato CFE v25 (obligatorio 3/3/2026):** las referencias en notas de crédito/débito deben incluir `MontoCfeRef` y `MonedaCfeRef` del comprobante original, y `TipoCambioCfeRef` si no es en pesos; no puede haber dos referencias al mismo comprobante. La DGI aplica controles estrictos desde el 15/4/2026 y, desde v25.2, controla que `FechaCfe` (opcional) sea la del comprobante original. Si no se puede identificar el comprobante, se usa una referencia global (`Global = true` con el motivo en `Razon`).

```csharp
var nc = await client.EmitirAsync(new Cfe(TipoCfe.NotaCreditoETicket)
{
    Referencias =
    [
        new RefCfe
        {
            TipoCfe      = TipoCfe.ETicket,
            Serie        = "A",
            NroCfe       = 125,                       // número del comprobante original
            FechaCfe     = new DateOnly(2025, 5, 10),
            MontoCfeRef  = 1979.80m,                  // monto total del comprobante original (F-C8)
            MonedaCfeRef = Moneda.PesoUruguayo,       // moneda del comprobante original (F-C9)
            Razon        = "Devolución — producto dañado",
        },
    ],
    Detalle = [new LineaDetalle { NombreItem = "Pintura látex 20L (devolución)", Cantidad = 1, PrecioUnitario = 890m }],
});
```

---

### 6. Traslado de mercadería (e-Remito)

**Escenario:** distribuidora traslada productos entre depósitos propios.

```csharp
// El e-Remito no incluye precios ni IVA: describe el traslado físico. Siempre identifica al receptor (con
// cualquier documento): en un traslado entre depósitos propios, la misma empresa con el domicilio de destino.
var remito = await client.EmitirAsync(new Cfe(TipoCfe.ERemito)
{
    IndTraslado = IndTraslado.TrasladoPropio,  // obligatorio; se informa como TipoTraslado 2 (traslado interno)
                                               // (TrasladoPorVenta se informa como 1 = venta)
    Receptor = new Receptor { Documento = "219999820013", RazonSocial = "Mi Empresa S.A.", Direccion = "Ruta 101 km 25", Ciudad = "Canelones" },
    Detalle = [new LineaDetalle { NombreItem = "Cajas producto X", Cantidad = 50 }],
});
// Si la mercadería es de otro: Propietario = new Propietario { Documento = "…", Nombre = "…" } (leyenda "Bienes de Terceros").
```

---

## Reporte Diario

La DGI exige enviar un **Reporte Diario** con el resumen de los CFE firmados cada día, aunque sea uno solo. Los
montos van en pesos (con el tipo de cambio de cada CFE), los CFE que la DGI rechazó se informan como anulados y,
con `UnidadIndexada` configurado, se informa cuántos e-Tickets superaron el tope de 5.000 UI (los e-Tickets que
hay que enviar a la DGI uno a uno, junto con los que documentan retenciones o percepciones).

```csharp
// Resumen firmado (montos, cantidades y rangos por tipo) de los CFE firmados ese día, tomados del
// almacenamiento. Usá la fecha de Uruguay (HoraUruguay), no DateTime.Today: el servidor puede estar en UTC.
var reporte = await client.EnviarReporteDiarioAsync(HoraUruguay.Hoy().AddDays(-1));

Console.WriteLine(reporte.Recibido     // estado AR = reporte recibido
    ? "✅ Reporte diario enviado"
    : $"❌ {reporte.Estado}: {reporte.Mensaje}");
```

> Automatizá este envío con un job nocturno (ej: Hangfire, un Worker Service de .NET, o una GitHub Action programada).

---

## Consideraciones fiscales clave

| Punto | Detalle |
|---|---|
| **Monotributistas e IVA mínimo** | Emiten con un CAE especial (`CaeEspecial.Monotributo`, `MonotributoSocialMides` o `IvaMinimo`) y líneas `IvaMinimoMonotributo`: sus CFE no discriminan IVA y la representación impresa lleva la leyenda "MONOTRIBUTO" o "Contribuyente IVA MINIMO". |
| **IRAE vs IRPF** | Las empresas que tributan IRAE son las principales obligadas; los profesionales independientes (IRPF Cat. II) también deben emitir CFE según su facturación. |
| **IVA incluido** | Los precios al público suelen publicarse con IVA incluido. Por defecto UruFactura trabaja con precios **sin IVA** y calcula el impuesto; con `MontosBrutos = MontosBrutos.IvaIncluido` (e-Tickets y e-Facturas) los precios incluyen el IVA y el SDK obtiene los netos dividiendo por (1 + tasa). |
| **Secreto profesional** | Las operaciones amparadas (`SecretoProfesional = true`) pueden omitir la identificación del receptor, que entonces va en la `Adenda`; en las ventas por cuenta ajena, tampoco es obligatorio el mandante. |
| **Reintegro de gastos y cobranzas** | Los pagos por cuenta de terceros (`PagoCuentaTerceros`) llevan la leyenda "Pagos por cuenta de terceros" y van aparte en el reporte diario. Una cobranza propia (`CobranzaPropia`) usa líneas no facturables y referencia los comprobantes que se cobran. |
| **Moneda extranjera** | Los CFE pueden emitirse en USD u otras monedas; el tipo de cambio fiscal (interbancario comprador del BCU al cierre del día anterior) se registra en el comprobante, y con él se convierten los montos del reporte diario. |
| **Conservación** | Los XML firmados deben conservarse **mínimo 5 años** según el Código Tributario. |
| **Anulación** | Los CFE emitidos no se anulan; se corrigen emitiendo una **Nota de Crédito** que referencia el comprobante original. Solo queda anulado el que la DGI rechaza: se informa así en el reporte diario y, si ya se había enviado al receptor electrónico, se le comunica. |
| **Receptores electrónicos** | Los CFE a otros emisores electrónicos se les envían (después de enviarlos a la DGI) y ellos acusan recibo y responden cada comprobante; ver el intercambio en el [README](../README.md#7-intercambio-con-otros-emisores-electrónicos). |

---

## Recursos oficiales

| Recurso | Enlace |
|---|---|
| DGI — Factura Electrónica | [dgi.gub.uy/factura-electronica](https://www.dgi.gub.uy/wdgi/page?2,factura-electronica,index,O,es,0,) |
| Esquemas XML CFE | Disponibles en el portal DGI en línea → Gestión CFE |
| Resolución 798/012 | [dgi.gub.uy/normativa](https://www.dgi.gub.uy/wdgi/page?2,principal,ampliacion-normativa-resoluciones,O,es,0,PAG;CONC;1175;1;D;resolucion-798-012;0;PAG) |
| AGESIC — CA habilitadas | [agesic.gub.uy](https://www.agesic.gub.uy/) |

---

> Ver también: [CERTIFICACION_DGI.md](CERTIFICACION_DGI.md) para el proceso completo de homologación y puesta en producción.
