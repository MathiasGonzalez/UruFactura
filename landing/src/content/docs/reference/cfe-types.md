---
title: Tipos de CFE
description: Referencia de todos los tipos de comprobantes fiscales electrónicos soportados
---

## Comprobantes soportados

Todos se crean igual: `new Cfe(TipoCfe.X) { … }`, y se emiten con `client.EmitirAsync(cfe)`.

| Código | Nombre | `TipoCfe` | Requiere receptor |
|--------|--------|-----------|-------------------|
| 101 | e-Ticket | `ETicket` | No (sí sobre 5.000 UI netas, con `UnidadIndexada`, o con retenciones: documento) |
| 102 | Nota de Crédito de e-Ticket | `NotaCreditoETicket` | No |
| 103 | Nota de Débito de e-Ticket | `NotaDebitoETicket` | No |
| 111 | e-Factura | `EFactura` | ✅ Sí (RUT, nombre, dirección y ciudad) |
| 112 | Nota de Crédito de e-Factura | `NotaCreditoEFactura` | ✅ Sí |
| 113 | Nota de Débito de e-Factura | `NotaDebitoEFactura` | ✅ Sí |
| 121 | e-Factura Exportación | `EFacturaExportacion` | ✅ Sí (nombre, dirección, ciudad, departamento o estado y país) |
| 122 | Nota de Crédito de e-Factura Exportación | `NotaCreditoEFacturaExportacion` | ✅ Sí |
| 123 | Nota de Débito de e-Factura Exportación | `NotaDebitoEFacturaExportacion` | ✅ Sí |
| 124 | e-Remito de Exportación | `ERemitoExportacion` | ✅ Sí (como la exportación) |
| 181 | e-Remito | `ERemito` | ✅ Sí (cualquier documento, nombre, dirección y ciudad) |
| 182 | e-Resguardo | `EResguardo` | ✅ Sí (cualquier documento y nombre) |
| 131-133 | e-Ticket y notas, venta por cuenta ajena | `ETicketCuentaAjena`… | No (exige `Mandante`; sí sobre 5.000 UI) |
| 141-143 | e-Factura y notas, venta por cuenta ajena | `EFacturaCuentaAjena`… | ✅ Sí (exige `Mandante`) |
| 151-153 | e-Boleta de entrada y notas | `EBoleta`… | ✅ Sí: el vendedor (documento, nombre, dirección y ciudad), salvo la compra de moneda extranjera bajo el tope |
| 2xx | Contingencia (CFC) de cada tipo | `ETicketContingencia`… | Igual que el tipo que sustituye |

Los códigos son los del formato CFE de la DGI. Con secreto profesional (`SecretoProfesional = true`) ningún tipo
exige el receptor: su identificación va en la adenda. Los e-Remitos no tienen notas de crédito/débito: un remito
rebaja ítems de otro con el indicador `RebajaRemito` (8), y un e-Resguardo anula ítems de otro con
`AnulacionResguardo` (9), en ambos casos con la referencia al comprobante que ajustan.

Se envían a la DGI todos los tipos salvo los e-Tickets (y sus notas) de hasta 5.000 UI netas sin retenciones, que se
informan en el reporte diario. Los de contingencia (2xx) se envían cuando se resuelve la contingencia, y no al receptor electrónico:
el comprobante válido es el de papel.

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
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Cliente S.A.", Direccion = "Av. Italia 1234", Ciudad = "Montevideo" },
    Detalle  = [new LineaDetalle { NombreItem = "Consultoría", Cantidad = 1, PrecioUnitario = 10000m }],
};
```

### e-Factura de Exportación (121)
Para **ventas al exterior**. Sin IVA (se informa `IndFact` 10); exige cláusula de venta, modalidad (1, 2, 3, 4, 90,
91 o 99) y vía de transporte (1, 2, 3, 8 = no aplica o 9). Cada línea puede llevar su posición arancelaria (`Ncm`).

```csharp
var exportacion = new Cfe(TipoCfe.EFacturaExportacion)
{
    Moneda = Moneda.DolarAmericano, TipoCambio = 40.5m,
    ClausulaVenta = "FOB", ModalidadVenta = 1, ViaTransporte = 1,
    Receptor = new Receptor
    {
        Documento = "X1234567", TipoDocumento = TipoDocumentoReceptor.Pasaporte, CodigoPais = "US", RazonSocial = "Importer Inc.",
        Direccion = "5th Avenue 100", Ciudad = "New York", Departamento = "NY", Pais = "Estados Unidos",
    },
    Detalle  = [new LineaDetalle { NombreItem = "Mercadería", Ncm = "0201.30.00", Cantidad = 10, PrecioUnitario = 100m }],
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

El receptor se identifica siempre, con cualquier documento. Si la mercadería es de otro, `Propietario` (leyenda
"Bienes de Terceros"):

```csharp
var remito = new Cfe(TipoCfe.ERemito)
{
    IndTraslado = IndTraslado.TrasladoPropio,
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Depósito S.A.", Direccion = "Ruta 1 km 20", Ciudad = "Ciudad del Plata" },
    Propietario = new Propietario { Documento = "211234560019", Nombre = "Dueño S.A." },
    Detalle = [new LineaDetalle { NombreItem = "Cajas", Cantidad = 50 }],
};
```

### e-Resguardo (182)
Para documentar **retenciones y percepciones** realizadas a proveedores. La línea no lleva nombre, cantidad ni precio:
solo de 1 a 5 `Retenciones` (código numérico de la tabla de la DGI —formulario y línea—, tasa, monto sujeto y valor).
El total retenido sale de la suma por código; los créditos fiscales (formulario 2181) van aparte. Los organismos del
Estado informan la `FechaValor` si no coincide con la del comprobante.

```csharp
var resguardo = new Cfe(TipoCfe.EResguardo)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Proveedor S.A." },
    Detalle  = [new LineaDetalle { Retenciones = [new RetencionPercepcion { Codigo = "1146171", Tasa = 12m, MontoSujeto = 1000m, Valor = 120m }] }],
};
```

### e-Boleta de entrada (151-153)
La emite **quien compra** a un vendedor que no factura electrónicamente. El `Receptor` es el vendedor ("RUC
VENDEDOR" en la representación impresa) y el indicador de cada línea dice quién es: `VendedorNoContribuyente` (13,
sin RUT), `VendedorIvaMinimo` (14) o `VendedorImeba` (15), con RUT. Admite IVA en suspenso (12, con
`MontosBrutos = MontosBrutos.ImebaYAdicionalesIncluidos`), no facturables, retenciones y descuentos globales. La
compra de moneda extranjera para reventa usa `CompraMonedaExtranjera` y el indicador 17 (por cuenta ajena, con el
`Mandante`). No se envía al vendedor.

```csharp
var boleta = new Cfe(TipoCfe.EBoleta)
{
    Receptor = new Receptor { Documento = "214844360018", RazonSocial = "Productor Rural", Direccion = "Ruta 5 km 30", Ciudad = "Canelones" },
    Detalle  = [new LineaDetalle { NombreItem = "Lana", Cantidad = 100, PrecioUnitario = 80m, IndFact = IndicadorFacturacion.VendedorImeba }],
};
```

### Venta por cuenta ajena (131-133, 141-143)
Se emite en nombre de un tercero. Hay que informar el mandante:

```csharp
var cfe = new Cfe(TipoCfe.EFacturaCuentaAjena)
{
    Mandante = new Mandante { Documento = "214844360018", Nombre = "Mandante S.A." },
    Receptor = new Receptor { Documento = "211234560019", RazonSocial = "Cliente S.A.", Direccion = "Av. Brasil 2500", Ciudad = "Montevideo" },
    Detalle  = [new LineaDetalle { NombreItem = "Producto", Cantidad = 1, PrecioUnitario = 500m }],
};
```

### Comprobantes de contingencia (2xx)
Tienen la misma estructura que el CFE que sustituyen (`new Cfe(TipoCfe.EFacturaContingencia)`), se numeran con su propio
CAE y se resumen en el reporte diario en su propio bloque.

---

## Numeración

Cada tipo de CFE tiene su propio CAE: un CAE de e-Ticket **no puede usarse** para e-Facturas. Registrá uno por tipo
(y por serie) con `client.RegistrarCaeAsync`, sin rangos superpuestos dentro de una misma serie; `EmitirAsync` toma el
número del CAE vigente del tipo del comprobante.
