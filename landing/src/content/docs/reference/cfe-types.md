---
title: Tipos de CFE
description: Referencia de todos los tipos de comprobantes fiscales electrónicos soportados
---

## Comprobantes soportados

Todos se crean igual: `new Cfe(TipoCfe.X) { … }`, y se emiten con `client.EmitirAsync(cfe)`.

| Código | Nombre | `TipoCfe` | Requiere receptor |
|--------|--------|-----------|-------------------|
| 101 | e-Ticket | `ETicket` | No (sí sobre 5.000 UI con `ValorUnidadIndexada`) |
| 102 | Nota de Crédito e-Ticket | `NotaCreditoETicket` | No |
| 103 | Nota de Débito e-Ticket | `NotaDebitoETicket` | No |
| 111 | e-Factura | `EFactura` | ✅ Sí (RUT) |
| 112 | Nota de Crédito e-Factura | `NotaCreditoEFactura` | ✅ Sí |
| 113 | Nota de Débito e-Factura | `NotaDebitoEFactura` | ✅ Sí |
| 121 | e-Factura de Exportación | `EFacturaExportacion` | ✅ Sí |
| 122 | NC e-Factura de Exportación | `NotaCreditoEFacturaExportacion` | ✅ Sí |
| 123 | ND e-Factura de Exportación | `NotaDebitoEFacturaExportacion` | ✅ Sí |
| 124 | e-Remito de Exportación | `ERemitoExportacion` | Condicional |
| 181 | e-Remito | `ERemito` | Condicional (si se informa: razón social, dirección y ciudad) |
| 182 | e-Resguardo | `EResguardo` | ✅ Sí |
| 131-133 | e-Ticket y notas, cuenta ajena | `ETicketCuentaAjena`… | No (exige `Mandante`) |
| 141-143 | e-Factura y notas, cuenta ajena | `EFacturaCuentaAjena`… | ✅ Sí (exige `Mandante`) |
| 2xx | Contingencia (CFC) de cada tipo | `ETicketContingencia`… | Igual que el tipo que sustituye |

Los códigos son los del formato CFE de la DGI. **No están soportados:** la e-Boleta de entrada (151-153 y 251-253),
la adenda y los descuentos/recargos globales en porcentaje (solo en monto). Los e-Remitos no tienen notas de crédito/débito.

`TipoCfeExtensions` clasifica cada tipo: `Familia()`, `EsNotaCorreccion()`, `EsNotaCredito()`, `EsContingencia()`,
`EsCuentaAjena()`, `EsExportacion()`, `EsRemito()` y `RequiereReceptor()`.

---

## Cuándo usar cada tipo

### e-Ticket (101) — el más común
Para ventas al consumidor final cuando **no se necesita identificar al comprador**.

```csharp
var ticket = new Cfe(TipoCfe.ETicket) { Detalle = [new LineaDetalle { NombreItem = "Café", Cantidad = 2, PrecioUnitario = 90m }] };
```

### e-Factura (111) — con identificación del receptor
Cuando el comprador es una **empresa o contribuyente** y necesita el documento para deducir IVA o gastos.

```csharp
var factura = new Cfe(TipoCfe.EFactura)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A." },
    Detalle  = [new LineaDetalle { NombreItem = "Consultoría", Cantidad = 1, PrecioUnitario = 10000m }],
};
```

### e-Factura de Exportación (121)
Para **ventas al exterior**. Sin IVA (se informa `IndFact` 10); exige cláusula de venta, modalidad y vía de transporte.

```csharp
var exportacion = new Cfe(TipoCfe.EFacturaExportacion)
{
    Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m,
    ClausulaVenta = "FOB", ModalidadVenta = 1, ViaTransporte = 1,
    Receptor = new Receptor { Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "US", RazonSocial = "Importer Inc." },
    Detalle  = [new LineaDetalle { NombreItem = "Mercadería", Cantidad = 10, PrecioUnitario = 100m }],
};
```

### Notas de Crédito (102, 112, 122) y de Débito (103, 113, 123)
Para **ajustar a la baja o al alza** un CFE emitido. Deben referenciarlo con su monto y moneda (obligatorios desde el formato CFE v25.01).

```csharp
var nc = new Cfe(TipoCfe.NotaCreditoETicket)
{
    Referencias =
    [
        new RefCfe
        {
            TipoCfe = TipoCfe.ETicket, Serie = "A", NroCfe = 42, FechaCfe = new DateOnly(2026, 6, 1),
            MontoCfeRef = 1220m, MonedaCfeRef = Moneda.PesoUruguayo, Razon = "Devolución total",
        },
    ],
    Detalle = [new LineaDetalle { NombreItem = "Devolución", Cantidad = 1, PrecioUnitario = 1000m }],
};
```

### e-Remito (181) y e-Remito de Exportación (124)
Para **documentar el traslado de mercadería**, sin precios. `IndTraslado` es **obligatorio**. La DGI solo distingue
dos valores (`TipoTraslado`): **1 = venta** y **2 = traslados internos**; el SDK informa `1` para `TrasladoPorVenta`
y `2` para todos los demás.

| Valor | Descripción |
|-------|-------------|
| `IndTraslado.TrasladoPropio` | Traslado entre depósitos propios |
| `IndTraslado.TrasladoEnComision` | Traslado en comisión |
| `IndTraslado.Devolucion` | Devolución al proveedor |
| `IndTraslado.TrasladoPorVenta` | Traslado por venta |
| `IndTraslado.TrasladoEnConsignacion` | Traslado en consignación |
| `IndTraslado.TrasladoPorExposicion` | Traslado por exposición / feria |

```csharp
var remito = new Cfe(TipoCfe.ERemito)
{
    IndTraslado = IndTraslado.TrasladoPropio,
    Detalle = [new LineaDetalle { NombreItem = "Cajas", Cantidad = 50 }],
};
```

### e-Resguardo (182)
Para documentar **retenciones y percepciones** realizadas a proveedores. La línea no lleva nombre, cantidad ni precio:
solo de 1 a 5 `Retenciones` (código de la tabla de la DGI, tasa, monto sujeto y valor). El total retenido sale de la suma.

```csharp
var resguardo = new Cfe(TipoCfe.EResguardo)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." },
    Detalle  = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "R1", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m }] }],
};
```

### Venta por cuenta ajena (131-133, 141-143)
Se emite en nombre de un tercero. Hay que informar el mandante:

```csharp
var cfe = new Cfe(TipoCfe.EFacturaCuentaAjena)
{
    Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A." },
    Receptor = new Receptor { Documento = "211234560019", RazonSocial = "Cliente S.A." },
    Detalle  = [new LineaDetalle { NombreItem = "Producto", Cantidad = 1, PrecioUnitario = 500m }],
};
```

### Comprobantes de contingencia (2xx)
Tienen la misma estructura que el CFE que sustituyen (`new Cfe(TipoCfe.EFacturaContingencia)`), se numeran con su propio
CAE y se resumen en el reporte diario en su propio bloque.

---

## Numeración

Cada tipo de CFE tiene su propio CAE: un CAE de e-Ticket **no puede usarse** para e-Facturas. Registrá uno por tipo
(y por serie) con `client.RegistrarCaeAsync`; `EmitirAsync` toma el número del CAE vigente del tipo del comprobante.
