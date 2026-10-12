# Certificación DGI y puesta en producción

Guía paso a paso para obtener la habilitación de la DGI y comenzar a emitir CFE con UruFactura.

---

## Resumen del proceso

```
1. Inscripción como emisor electrónico (DGI)
        ↓
2. Obtención del certificado digital
        ↓
3. Solicitud del CAE (Constancia de Autorización de Emisión)
        ↓
4. Homologación (ambiente de pruebas)
        ↓
5. Habilitación en producción
```

---

## 1. Inscripción como emisor electrónico

Antes de emitir CFE, la empresa debe estar habilitada por la DGI como **Emisor Electrónico**.

- Ingresar a [DGI en línea](https://www.dgi.gub.uy/) con usuario y contraseña del contribuyente.
- Ir a **Gestión CFE → Inscripción como Emisor Electrónico**.
- Completar los datos de la empresa y los tipos de CFE que se van a emitir (e-Ticket, e-Factura, etc.).
- La DGI analizará la solicitud y notificará por correo la aprobación (generalmente dentro de los días hábiles siguientes).

> 📖 Normativa de referencia: [Resolución 798/012 y sus modificativas](https://www.dgi.gub.uy/wdgi/page?2,principal,ampliacion-normativa-resoluciones,O,es,0,PAG;CONC;1175;1;D;resolucion-798-012;0;PAG)

---

## 2. Obtención del certificado digital

La DGI exige que cada CFE esté **firmado digitalmente** con un certificado de empresa habilitado.

### ¿Qué tipo de certificado se necesita?

- Certificado X.509 emitido por una **Autoridad Certificante reconocida por AGESIC** (Uruguay), en formato `.p12` / `.pfx`.
- Proveedores habilitados: [ABITAB](https://www.abitab.com.uy/), [Correo Uruguayo](https://www.correo.com.uy/), [CertiSur](https://www.certisur.com/) u otras CA reconocidas.
- El certificado debe estar vinculado al **RUT del emisor**.

### Configurar el certificado en UruFactura

```csharp
var config = new UruFacturaConfig
{
    RutEmisor           = "210000000012",   // 12 dígitos, sin puntos ni guión
    RazonSocialEmisor   = "Mi Empresa S.A.",
    DomicilioFiscal     = "Av. 18 de Julio 1234",
    Ciudad              = "Montevideo",
    Departamento        = "Montevideo",
    Giro                = "Comercio al por mayor",   // Actividad económica (opcional)
    RutaCertificado     = "/ruta/certificado.p12",
    PasswordCertificado = "contraseña_del_certificado",
    Ambiente            = Ambiente.Homologacion,   // ← Homologación durante las pruebas
};
```

> ⚠️ Nunca almacenes `PasswordCertificado` en texto plano en el código fuente. Usá variables de entorno o un gestor de secretos.

---

## 3. Solicitud del CAE

El **CAE (Constancia de Autorización de Emisión)** es el rango de números de comprobante autorizado por la DGI para cada tipo de CFE.

### Cómo solicitarlo

1. Ingresar a [DGI en línea](https://www.dgi.gub.uy/) → **Gestión CFE → Solicitud de CAE**.
2. Seleccionar el tipo de CFE (ej: e-Ticket 101, e-Factura 111).
3. Indicar la cantidad de comprobantes que se necesitan (ej: del 1 al 1000).
4. La DGI devuelve el CAE con número de serie, rango y fecha de vencimiento.

### Registrar el CAE en UruFactura

```csharp
client.Cae.RegistrarCae(new Cae
{
    NroSerie         = "CAE2025001",
    NumeroAutorizacion = "90110001945",   // CAE_ID entregado por la DGI
    Serie            = "A",
    TipoCfe          = TipoCfe.ETicket,
    RangoDesde       = 1,
    RangoHasta       = 1000,
    FechaVencimiento = new DateOnly(2026, 12, 31),
});
```

> 💡 **En producción, persistí el estado del CAE.** Usá `ICaeRepository` para cargar los CAEs
> al arrancar la app y actualizar `UltimoNroUsado` después de cada emisión. Si la app reinicia
> sin persistencia, el contador vuelve a cero y la DGI rechazará los comprobantes duplicados.
> Ver [Persistencia de CAEs en README](../README.md#-persistencia-de-caes) para el patrón completo.

### Monitorear el estado del CAE

```csharp
// Ver advertencias (vencimiento próximo, rango casi agotado)
foreach (var advertencia in client.Cae.ObtenerAdvertencias())
    Console.WriteLine(advertencia);

// Resumen general
Console.WriteLine(client.Cae.ResumenEstado());
```

> 🔁 Solicitá un nuevo CAE antes de que venza el actual o se agote el rango.

---

## 4. Homologación (ambiente de pruebas)

La DGI exige un proceso de **homologación** antes de habilitar la emisión en producción. Consiste en enviar comprobantes de prueba al ambiente de homologación y obtener la aprobación formal.

### Configurar el ambiente de homologación

Con `Ambiente = Ambiente.Homologacion` UruFactura apunta automáticamente a los servicios de prueba de la DGI:
```
Envío (ws_efactura):     https://efactura.dgi.gub.uy:6443/ePrueba/ws_eprueba
Consultas (ws_consultas): https://efactura.dgi.gub.uy:6460/ePrueba/ws_consultasPrueba
```
(los WSDL están publicados en esas URL con el sufijo `?wsdl`). Para otros ambientes configurá `UrlEnvio` y `UrlConsultas`.

### Pasos de homologación

1. **Emitir los CFE de prueba requeridos** por la DGI (tipos y cantidades definidos en el instructivo oficial de homologación).

```csharp
// Ejemplo: enviar un e-Ticket de prueba
var eticket = client.CrearETicket();
client.AsignarCae(eticket);   // toma el próximo número del CAE
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Servicio de prueba",
    Cantidad       = 1,
    PrecioUnitario = 100m,
    IndFactIva     = TipoIva.Basico,
});

var respuesta = await client.EnviarCfeAsync(eticket);
Console.WriteLine(respuesta.Exitoso
    ? $"✅ Aceptado: {respuesta.Mensaje}"
    : $"❌ Rechazado: {respuesta.Mensaje} (código {respuesta.Codigo})");
```

2. **Enviar el Reporte Diario de prueba** al finalizar cada jornada de pruebas.

```csharp
var resultado = await client.EnviarReporteDiarioAsync(DateTime.Today, new[] { eticket });
Console.WriteLine($"Reporte diario: {resultado.Respuesta.Mensaje}");
```

3. **Consultar el estado** de cada CFE para confirmar la aceptación.

```csharp
var estado = await client.ConsultarEstadoCfeAsync(eticket);
Console.WriteLine($"Estado DGI: {estado.Mensaje}");
```

4. **Notificar a DGI** que finalizaste las pruebas de homologación mediante el portal DGI en línea o vía el representante técnico asignado.

> 📖 Instructivo oficial de homologación (accesible desde el portal de DGI en línea → Gestión CFE → Homologación).

---

## 5. Habilitación en producción

Una vez que la DGI aprueba la homologación:

1. Cambiar el ambiente en la configuración:

```csharp
Ambiente = Ambiente.Produccion
```

UruFactura usará automáticamente:
```
https://efactura.dgi.gub.uy/ePresentacionSoap/service
```

2. Verificar que el certificado digital **de producción** esté configurado (puede ser el mismo que el de homologación si es un certificado real de empresa).

3. Solicitar un CAE de producción para cada tipo de CFE que se vaya a emitir.

4. Ejecutar un envío real de prueba con un comprobante de bajo monto para validar el flujo completo.

---

## 6. Flujo completo de emisión en producción

```csharp
// 1. Configurar
var config = new UruFacturaConfig
{
    RutEmisor           = "210000000012",
    RazonSocialEmisor   = "Mi Empresa S.A.",
    DomicilioFiscal     = "Av. 18 de Julio 1234",
    Ciudad              = "Montevideo",
    Departamento        = "Montevideo",
    Ambiente            = Ambiente.Produccion,
    RutaCertificado     = "/ruta/certificado.p12",
    PasswordCertificado = Environment.GetEnvironmentVariable("CERT_PASSWORD")!,
};

using var client = new UruFacturaClient(config);

// 2. Registrar CAE
client.Cae.RegistrarCae(new Cae
{
    NroSerie         = "CAE2025001",
    NumeroAutorizacion = "90110001945",   // CAE_ID entregado por la DGI
    Serie            = "A",
    TipoCfe          = TipoCfe.ETicket,
    RangoDesde       = 1,
    RangoHasta       = 1000,
    FechaVencimiento = new DateOnly(2026, 12, 31),
});

// 3. Crear comprobante
var eticket = client.CrearETicket();
client.AsignarCae(eticket);   // toma el próximo número del CAE
eticket.Detalle.Add(new LineaDetalle
{
    NroLinea       = 1,
    NombreItem     = "Servicio de consultoría",
    Cantidad       = 1,
    PrecioUnitario = 5000m,
    IndFactIva     = TipoIva.Basico,
});

// 4. Generar, firmar y enviar
var respuesta = await client.EnviarCfeAsync(eticket);   // acuse del sobre (AS / BS)

if (!respuesta.Exitoso)
    throw new Exception($"DGI rechazó el sobre: {respuesta.Mensaje}");

// El resultado del comprobante se consulta con el IdReceptor/Token del acuse
var estado = await client.ConsultarEstadoEnvioAsync(respuesta, [eticket]);
if (!estado.Exitoso)
    throw new Exception($"DGI rechazó el CFE ({estado.Codigo}): {estado.Mensaje}");

// 5. Generar representación impresa
byte[] pdf = client.GenerarPdfA4(eticket);
await File.WriteAllBytesAsync("eticket_001.pdf", pdf);
```

---

## 7. Obligaciones operativas continuas

| Obligación | Detalle |
|---|---|
| **Reporte Diario** | Enviar a DGI el reporte con todos los CFE del día, incluso si fue un solo comprobante. |
| **Resguardo de XML** | Conservar los XML firmados por el plazo legal (mínimo 5 años). |
| **Renovar CAE** | Solicitar nuevo CAE antes del vencimiento o agotamiento del rango. |
| **Renovar certificado** | Los certificados digitales vencen (generalmente cada 1-3 años). |
| **Notas de corrección** | Usar Notas de Crédito/Débito para anular o corregir CFE emitidos. |

---

## 8. Recursos oficiales DGI

| Recurso | Enlace |
|---|---|
| Portal DGI en línea | [https://www.dgi.gub.uy/](https://www.dgi.gub.uy/) |
| XSD, WSDL y formatos (CFE v25.2, XSD 1.44.2) | [https://www.dgi.gub.uy/wdgi/page?2,factura-electronica,index,O,es,0,](https://www.dgi.gub.uy/wdgi/page?2,factura-electronica,index,O,es,0,) |
| Instructivo técnico de integración | Disponible en el portal DGI en línea → Gestión CFE |
| AGESIC (autoridades certificantes) | [https://www.agesic.gub.uy/](https://www.agesic.gub.uy/) |
| Soporte DGI | [https://www.dgi.gub.uy/wdgi/page?2,contacto,index,O,es,0,](https://www.dgi.gub.uy/wdgi/page?2,contacto,index,O,es,0,) |

> 📋 Para mantener UruFactura actualizado ante nuevas versiones del formato CFE, ver [NORMATIVA_DGI_UPDATE.md](NORMATIVA_DGI_UPDATE.md).

---

## Códigos de respuesta DGI comunes

| Estado | Mensaje | Significado |
|--------|---------|-------------|
| `AS` | `ACKSobre` | Sobre recibido (no implica que los CFE hayan sido aceptados) |
| `BS` | `ACKSobre` | Sobre rechazado: ver los motivos (`S01`…`S20`) en `respuesta.Mensaje` |
| `BA` | `ACKSobre` / `ACKRepDiario` | Archivo rechazado |
| `AR` | `ACKRepDiario` | Reporte diario recibido |
| `BR` | `ACKRepDiario` | Reporte diario rechazado |
| `AE` | `ACKCFE` | Comprobante aceptado |
| `BE` | `ACKCFE` | Comprobante rechazado (CFE): ver motivos en `Comprobantes[i].Motivos` |
| `CE` | `ACKCFE` | Comprobante observado (CFC) |
| `FE` | `ACKCFE` | Comprobante reliquidado |
| `SOAP_FAULT` | — | La DGI respondió con un SOAP Fault (p. ej. firma WS-Security inválida) |
| `4xx` / `5xx` | — | Error HTTP sin cuerpo SOAP (proxy, servicio caído) |

> En caso de rechazo, revisar `respuesta.Mensaje` (incluye los códigos y glosas de los motivos) y, para comprobantes, `respuesta.Comprobantes`.
