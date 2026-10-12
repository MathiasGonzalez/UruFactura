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
| **182** | e-Resguardo | Retención de impuestos (aún no emitible con el SDK: falta soporte de retenciones) |

> La diferencia principal entre **e-Ticket** y **e-Factura** es si el receptor tiene o no RUT. Las empresas siempre deben exigir e-Factura para poder descontar el IVA compras.

---

## Tasas de IVA vigentes

| Tasa | Aplicación |
|------|-----------|
| **22 %** (básica) | Bienes y servicios en general |
| **10 %** (mínima) | Alimentos de la canasta básica, medicamentos, servicios médicos |
| **0 %** (exento) | Arrendamientos de inmuebles, servicios educativos, exportaciones |
| **Suspendido** | IVA temporalmente suspendido por disposición legal (sin cálculo de impuesto) |

---

## Ejemplos por tipo de empresa

### 1. Comercio minorista (venta a consumidor final)

**Escenario:** ferretería vende materiales a una persona física sin RUT.

```csharp
// e-Ticket → consumidor final, sin datos del receptor
var eticket = client.CrearETicket();
client.AsignarCae(eticket);   // toma el próximo número del CAE
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Pintura látex 20L",
    Cantidad       = 2,
    PrecioUnitario = 890m,           // precio sin IVA
    IndFactIva     = TipoIva.Basico, // 22 %
});
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 2,
    NombreItem     = "Rodillo 23cm",
    Cantidad       = 1,
    PrecioUnitario = 150m,
    IndFactIva     = TipoIva.Basico,
});

var respuesta = await client.EnviarCfeAsync(eticket);
```

---

### 2. Servicios profesionales (empresa a empresa)

**Escenario:** estudio contable factura honorarios mensuales a una S.A.

```csharp
// e-Factura → receptor con RUT, puede deducir IVA
var efactura = client.CrearEFactura();
client.AsignarCae(efactura);   // toma el próximo número del CAE
efactura.Receptor = new Receptor
{
    Documento       = "210012345678",
    TipoDocumento   = TipoDocumentoReceptor.Rut,
    RazonSocial     = "Importadora Sur S.A.",
    Direccion       = "Rambla República de Chile 4030",
};
efactura.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Honorarios contables — Mayo 2025",
    Cantidad       = 1,
    PrecioUnitario = 15000m,
    IndFactIva     = TipoIva.Basico, // 22 %
});

var respuesta = await client.EnviarCfeAsync(efactura);
```

---

### 3. Supermercado / alimentos con IVA diferenciado

**Escenario:** minimarket vende productos con tasas mixtas (básica y mínima).

```csharp
var eticket = client.CrearETicket();
client.AsignarCae(eticket);   // toma el próximo número del CAE

// Producto con IVA básico (22 %)
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Detergente 1L",
    Cantidad       = 3,
    PrecioUnitario = 85m,
    IndFactIva     = TipoIva.Basico,
});

// Producto con IVA mínimo (10 %) — canasta básica
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 2,
    NombreItem     = "Aceite girasol 900ml",
    Cantidad       = 2,
    PrecioUnitario = 120m,
    IndFactIva     = TipoIva.Minimo,
});

// Producto exento — medicamento
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 3,
    NombreItem     = "Ibuprofeno 400mg x20",
    Cantidad       = 1,
    PrecioUnitario = 210m,
    IndFactIva     = TipoIva.Exento,
});
```

---

### 4. Empresa exportadora

**Escenario:** software factory cobra a cliente del exterior por desarrollo a medida.

```csharp
// e-Factura de Exportación → IVA 0 %, requiere datos del receptor extranjero
var exportacion = client.CrearEFacturaExportacion();
client.AsignarCae(exportacion);   // toma el próximo número del CAE
exportacion.Moneda         = Moneda.DolarAmericano;
exportacion.TipoCambio     = 40.5m;
exportacion.ClausulaVenta  = "N/A";  // Incoterm de 1-3 letras, o "N/A" para servicios
exportacion.ModalidadVenta = 90;     // Régimen general - exportación de servicios
exportacion.ViaTransporte  = 9;      // Otro
exportacion.Receptor = new Receptor
{
    RazonSocial   = "TechCorp LLC",
    Pais          = "Estados Unidos",
    CodigoPais    = "US",
    Documento     = "TX-123456",                       // documento extranjero (opcional)
    TipoDocumento = TipoDocumentoReceptor.DocumentoExterior,
};
exportacion.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Desarrollo de software — Sprint 12",
    Cantidad       = 1,
    PrecioUnitario = 8000m,          // USD, moneda configurada aparte
    // En exportación el IVA no aplica: el SDK informa IndFact 10 (exportación y asimiladas)
});
```

---

### 5. Nota de crédito (devolución parcial)

**Escenario:** cliente devuelve una unidad de un e-Ticket anterior.

> ⚠️ **Desde formato CFE v25.01 (obligatorio 3/3/2026):** las referencias en notas de crédito/débito deben incluir `MontoCfeRef` y `MonedaCfeRef` del comprobante original. La DGI aplica controles estrictos desde el 15/4/2026.

```csharp
var nc = client.CrearNotaCreditoETicket();
client.AsignarCae(nc);   // toma el próximo número del CAE
nc.Referencias.Add(new RefCfe
{
    TipoCfe      = TipoCfe.ETicket,
    Serie        = "A",
    NroCfe       = 125,                          // número del comprobante original
    FechaCfe     = new DateTime(2025, 5, 10),
    MontoCfeRef  = 1979.80m,                     // monto total del comprobante original (F-C8)
    MonedaCfeRef = Moneda.PesoUruguayo,          // moneda del comprobante original (F-C9)
    Razon        = "Devolución — producto dañado",
});
nc.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Pintura látex 20L (devolución)",
    Cantidad       = 1,
    PrecioUnitario = 890m,
    IndFactIva     = TipoIva.Basico,
});

var respuesta = await client.EnviarCfeAsync(nc);
```

---

### 6. Traslado de mercadería (e-Remito)

**Escenario:** distribuidora traslada productos entre depósitos propios.

```csharp
var remito = client.CrearERemito();
client.AsignarCae(remito);   // toma el próximo número del CAE
remito.IndTraslado = IndTraslado.TrasladoPropio; // obligatorio; se informa como TipoTraslado 2 (traslado interno)
                                                 // (TrasladoPorVenta se informa como 1 = venta)
// El e-Remito no incluye precios; describe el traslado físico
remito.Detalle.Add(new LineaDetalle
{
    NroLinea    = 1,
    NombreItem  = "Cajas producto X",
    Cantidad    = 50,
    IndFactIva  = TipoIva.Exento,
});
```

---

## Reporte Diario

La DGI exige enviar un **Reporte Diario** al finalizar cada jornada con el resumen de los CFE emitidos, aunque sea uno solo.

```csharp
// Envía un resumen firmado (montos, cantidades y rangos por tipo) de los CFE emitidos ese día.
var resultado = await client.EnviarReporteDiarioAsync(
    DateTime.Today,
    new[] { eticket1, eticket2, efactura1 }
);

Console.WriteLine(resultado.Respuesta.Exitoso     // estado AR = reporte recibido
    ? "✅ Reporte diario enviado"
    : $"❌ {resultado.Respuesta.Codigo}: {resultado.Respuesta.Mensaje}");
```

> Automatizá este envío con un job nocturno (ej: Hangfire, un Worker Service de .NET, o una GitHub Action programada).

---

## Consideraciones fiscales clave

| Punto | Detalle |
|---|---|
| **Monotributistas** | No están obligados a emitir CFE; pueden emitir factura papel o adherirse voluntariamente. |
| **IRAE vs IRPF** | Las empresas que tributan IRAE son las principales obligadas; los profesionales independientes (IRPF Cat. II) también deben emitir CFE según su facturación. |
| **IVA incluido** | Los precios al público suelen publicarse con IVA incluido. UruFactura trabaja con precios **sin IVA**; el sistema calcula el monto del impuesto automáticamente. |
| **Moneda extranjera** | Los CFE pueden emitirse en USD u otras monedas; el tipo de cambio del BCU debe registrarse en el comprobante. |
| **Conservación** | Los XML firmados deben conservarse **mínimo 5 años** según el Código Tributario. |
| **Anulación** | Los CFE no se anulan; se corrigen emitiendo una **Nota de Crédito** que referencia el comprobante original. |

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
