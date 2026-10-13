---
title: Tipos de CFE
description: Referencia de todos los tipos de comprobantes fiscales electrónicos soportados
---

## Comprobantes soportados

| Código | Nombre | Enum `TipoCfe` | Método de creación | Requiere receptor |
|--------|--------|----------------|-------------------|------------------|
| 101 | e-Ticket | `ETicket` | `CrearETicket()` | No |
| 102 | Nota de Crédito e-Ticket | `NotaCreditoETicket` | `CrearNotaCreditoETicket()` | No |
| 103 | Nota de Débito e-Ticket | `NotaDebitoETicket` | `CrearNotaDebitoETicket()` | No |
| 111 | e-Factura | `EFactura` | `CrearEFactura()` | ✅ Sí |
| 112 | Nota de Crédito e-Factura | `NotaCreditoEFactura` | `CrearNotaCreditoEFactura()` | ✅ Sí |
| 113 | Nota de Débito e-Factura | `NotaDebitoEFactura` | `CrearNotaDebitoEFactura()` | ✅ Sí |
| 121 | e-Factura de Exportación | `EFacturaExportacion` | `CrearEFacturaExportacion()` | ✅ Sí |
| 122 | NC e-Factura de Exportación | `NotaCreditoEFacturaExportacion` | `CrearNotaCreditoEFacturaExportacion()` | ✅ Sí |
| 123 | ND e-Factura de Exportación | `NotaDebitoEFacturaExportacion` | `CrearNotaDebitoEFacturaExportacion()` | ✅ Sí |
| 124 | e-Remito de Exportación | `ERemitoExportacion` | `CrearERemitoExportacion()` | Condicional |
| 181 | e-Remito | `ERemito` | `CrearERemito()` | Condicional |
| 182 | e-Resguardo | `EResguardo` | `CrearEResguardo()` | ✅ Sí |
| 131-133 | e-Ticket y notas, cuenta ajena | `ETicketCuentaAjena`… | `Crear(TipoCfe.…)` | No (exige `Mandante`) |
| 141-143 | e-Factura y notas, cuenta ajena | `EFacturaCuentaAjena`… | `Crear(TipoCfe.…)` | ✅ Sí (exige `Mandante`) |
| 2xx | Contingencia (CFC) de cada tipo | `ETicketContingencia`… | `Crear(TipoCfe.…)` | Igual que el tipo que sustituye |

Los códigos son los del formato CFE de la DGI. **No están soportados:** la e-Boleta de entrada (151-153 y 251-253),
la adenda y los descuentos/recargos globales en porcentaje (solo en monto). Los e-Remitos no tienen notas de crédito/débito.

---

## Cuándo usar cada tipo

### e-Ticket (101) — el más común
Para ventas al consumidor final cuando **no se necesita identificar al comprador**. Es el equivalente electrónico del ticket de caja.

```csharp
var ticket = client.CrearETicket();
```

### e-Factura (111) — con identificación del receptor
Cuando el comprador es una **empresa o contribuyente** y necesita el documento para deducir IVA o gastos.

```csharp
var factura = client.CrearEFactura();
factura.Receptor = new Receptor { Documento = "...", TipoDocumento = TipoDocumentoReceptor.Rut, ... };
```

### e-Factura de Exportación (121)
Para **ventas al exterior**. Requiere información adicional del receptor extranjero.

```csharp
var exportacion = client.CrearEFacturaExportacion();
```

### Notas de Crédito (102, 112, 122)
Para **anular o ajustar a la baja** un CFE emitido previamente. Deben referenciar el comprobante original.

```csharp
var nc = client.CrearNotaCreditoETicket();
nc.Referencias.Add(new RefCfe
{
    TipoCfe  = TipoCfe.ETicket,
    NroCfe   = 42,
    FechaCfe = new DateTime(2025, 6, 1),
    Razon    = "Devolución total",
});
```

### Notas de Débito (103, 113, 123)
Para **ajustar al alza** o cobrar diferencias sobre un CFE emitido.

### e-Remito (181) y e-Remito de Exportación (124)
Para **documentar el traslado de mercadería** sin implicar una transacción de venta.
El campo `IndTraslado` es **obligatorio** para estos tipos e indica el motivo del traslado:

La DGI solo distingue dos valores (`TipoTraslado`): **1 = venta** y **2 = traslados internos**. El SDK
informa `1` para `TrasladoPorVenta` y `2` para todos los demás.

| Valor | Descripción |
|-------|-------------|
| `IndTraslado.TrasladoPropio` | Traslado entre depósitos propios |
| `IndTraslado.TrasladoEnComision` | Traslado en comisión |
| `IndTraslado.Devolucion` | Devolución al proveedor |
| `IndTraslado.TrasladoPorVenta` | Traslado por venta |
| `IndTraslado.TrasladoEnConsignacion` | Traslado en consignación |
| `IndTraslado.TrasladoPorExposicion` | Traslado por exposición / feria |

```csharp
var remito = client.CrearERemito();
remito.IndTraslado = IndTraslado.TrasladoPropio;
```

### e-Resguardo (182)
Para documentar **retenciones y percepciones** realizadas a proveedores. La línea no lleva nombre, cantidad ni precio:
solo de 1 a 5 `Retenciones` (código de la tabla de la DGI, tasa, monto sujeto y valor). El total retenido sale de la suma.

```csharp
var resguardo = client.CrearEResguardo();
resguardo.Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." };
resguardo.Detalle.Add(new LineaDetalle
{
    Retenciones = { new RetencionPercepcion { Codigo = "R1", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m } },
});
```

### Venta por cuenta ajena (131-133, 141-143)
Se emite en nombre de un tercero. Hay que informar el mandante:

```csharp
var cfe = client.Crear(TipoCfe.EFacturaCuentaAjena);
cfe.Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A.", CodigoPais = "UY" };
```

### Comprobantes de contingencia (2xx)
Tienen la misma estructura que el CFE que sustituyen (`client.Crear(TipoCfe.EFacturaContingencia)`) y se
resumen en el reporte diario en su propio bloque.

---

## Restricciones de numeración

Cada tipo de CFE tiene su propio rango de CAE independiente. Un CAE de e-Ticket **no puede usarse** para e-Facturas.

```csharp
// ✅ Correcto: CAE específico por tipo
client.Cae.RegistrarCae(new Cae { TipoCfe = TipoCfe.ETicket, ... });
client.Cae.RegistrarCae(new Cae { TipoCfe = TipoCfe.EFactura, ... });

// ❌ No se puede usar el mismo CAE para distintos tipos
```
