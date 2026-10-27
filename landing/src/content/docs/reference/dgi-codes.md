---
title: Códigos DGI
description: Estados de respuesta de los servicios web de la DGI y cómo manejarlos en el SDK
---

## Cómo responde la DGI

El envío de comprobantes es **asíncrono** y en dos pasos. Los estados provienen de los esquemas
oficiales (`ACKSobre`, `ACKCFE`, `ACKRepDiario`):

1. **Envío del sobre** (`EnviarAsync`): la DGI responde con un acuse del sobre (`AcuseSobre`).
2. **Consulta del resultado** (`ConsultarEnvioAsync(acuse)`): con el `IdReceptor` y el `Token` del acuse,
   la DGI informa el estado de cada comprobante (`EstadoEnvio`), y el SDK lo copia en cada `CfeEmitido`.
   Lo asocia por tipo, serie y número; si la DGI informa solo `Nro_ordinal` (lo único obligatorio en
   `ACKCFE`), por la posición del CFE en el sobre.

| Estado | Mensaje DGI | Significado | En el SDK |
|--------|-------------|-------------|-----------|
| `AS` | `ACKSobre` | Sobre recibido (aún no implica que los CFE hayan sido aceptados) | `EstadoSobre.Recibido` (`acuse.Recibido`) |
| `BS` | `ACKSobre` | Sobre rechazado — motivos `S01`…`S20` | `EstadoSobre.Rechazado`, `acuse.Motivos` |
| `BA` | `ACKSobre` / `ACKRepDiario` | Archivo rechazado | `EstadoSobre.ArchivoRechazado` / `EstadoReporte.ArchivoRechazado` |
| `AR` | `ACKRepDiario` | Reporte diario recibido | `EstadoReporte.Recibido` (`AcuseReporte.Recibido`) |
| `BR` | `ACKRepDiario` | Reporte diario rechazado | `EstadoReporte.Rechazado`, `AcuseReporte.Motivos` |
| `AE` | `ACKCFE` | Comprobante aceptado | `EstadoCfe.Aceptado` (`cfe.AceptadoPorDgi`) |
| `BE` | `ACKCFE` | Comprobante rechazado (CFE) | `EstadoCfe.Rechazado`, `cfe.MensajeDgi` |
| `CE` | `ACKCFE` | Comprobante observado (CFC) | `EstadoCfe.Observado` |
| `FE` | `ACKCFE` | Comprobante reliquidado | `EstadoCfe.Reliquidado` |

Un código fuera de los esquemas se informa como `DgiCommunicationException`, igual que los errores de comunicación, que **no** son estados.

| `CodigoRespuesta` | Significado |
|-------------------|-------------|
| `SOAP_FAULT` | La DGI respondió con un SOAP Fault (por ejemplo, firma WS-Security inválida) |
| `4xx` / `5xx` | Respuesta HTTP sin acuse (proxy, servicio caído) |
| `null` | Error de red o timeout |

---

## Cómo manejarlos en el SDK

```csharp
var acuse = await client.EnviarAsync(emitido);

if (!acuse.Recibido)
{
    // BS / BA: los motivos informados por la DGI
    logger.LogError("Sobre rechazado [{Estado}]: {Motivos}", acuse.Estado, string.Join("; ", acuse.Motivos));
    return;
}

// La DGI indica desde cuándo se puede consultar el resultado
if (acuse.ConsultarDesde is { } desde && desde > DateTimeOffset.Now)
    await Task.Delay(desde - DateTimeOffset.Now);

var estado = await client.ConsultarEnvioAsync(acuse);

if (!estado.Procesado)
    return;   // la DGI todavía no lo procesó: volver a consultar más tarde

foreach (var cfe in acuse.Comprobantes)
    Console.WriteLine($"{cfe}: {cfe.EstadoDgi} {cfe.MensajeDgi}");
```

---

## Consulta del estado de un comprobante

`ConsultarCfeAsync(emitido)` consulta por tipo, serie y número (servicio `ws_consultas`), devuelve el
estado del comprobante (`EstadoCfe`) y lo guarda en el `CfeEmitido` y en el almacenamiento. Es útil tras
un reinicio, cuando ya no se tiene el acuse del sobre.

---

## Reintentos

- **Sobre rechazado (`BS`/`BA`)**: la DGI no procesó los CFE; se reenvía el mismo `CfeEmitido`.
- **Comprobante rechazado (`BE`)**: no se reenvía; se corrige y se emite uno nuevo (con otro número). El
  rechazado queda anulado (`cfe.AnuladoPorRechazo`): el reporte diario lo informa así y, si ya se había enviado al
  receptor electrónico, se le comunica con `GenerarNotificacionAnulacionAsync`. No se anula el rechazado por `E02`
  (ya existe en los registros de la DGI: era un reenvío) ni por `E09` (no corresponde al CAE).
- **Error de comunicación al enviar** (timeout, red): no se sabe si la DGI recibió el sobre. Consultá el
  comprobante con `ConsultarCfeAsync` y reenviálo solo si la DGI no lo tiene; un reenvío de algo ya
  recibido la DGI lo trata como duplicado.
- `EnviarAsync` no reenvía un CFE que ya fue recibido o tiene resultado (`InvalidOperationException`).
- **Reporte diario**: el primer envío de una fecha usa `secEnvio` 1; para corregir uno ya recibido se
  reenvía completo con la secuencia anterior + 1.

---

## Motivos de rechazo en el intercambio entre emisores

El receptor electrónico usa los mismos mensajes (`ACKSobre` y `ACKCFE`) y estos motivos (formato del mensaje de
respuesta v19). `RecibirSobreAsync` aplica los controles automáticos y `ResponderCfesAsync` agrega los comerciales.

| Código | Motivo | Control en `RecibirSobreAsync` |
|--------|--------|--------------------------------|
| `S01` | Formato del archivo no es el indicado | No es un `EnvioCFE_entreEmpresas` |
| `S02` | No coincide RUC de Sobre, Certificado, envío o CFE | El sobre o un CFE no es para este RUT, o un CFE es de otro emisor |
| `S03` | Certificado electrónico no es válido | El certificado de la carátula no se lee o no estaba vigente en la fecha del sobre |
| `S04` | No cumple validaciones según Formato de sobre | Versión, fecha o cantidad de CFE inválidas |
| `S05` | No coinciden cantidad CFE de carátula y contenido | `CantCFE` distinta de los CFE del sobre |
| `S06` | No coinciden certificado de sobre y comprobantes | Un CFE firmado con otro certificado |
| `S07` | Sobre enviado supera el tamaño máximo admitido | Más de 2 MB |
| `S20` | Sobre enviado ya existe en los registros del receptor | Lo detecta la aplicación (RUT emisor e `IdEmisor` ya recibidos) |
| `E02` | Tipo y Nº de CFE ya existe en los registros | Repetido en el sobre (los ya recibidos antes, la aplicación) |
| `E03` | Tipo y Nº de CFE no se corresponden con el CAE | Número fuera del rango del CAE |
| `E04` | Firma electrónica no es válida | Firma del CFE |
| `E05` | No cumple validaciones de Formato comprobantes | Faltan tipo, serie, número o fechas |
| `E07` | Fecha Firma de CFE no se corresponde con fecha CAE | Firmado después del vencimiento del CAE |
| `E06`, `E10`-`E12` | (ver formato) | — (los puede indicar la aplicación) |
| `E20`-`E26` | Orden de compra vencida, mercadería en mal estado, proveedor inhabilitado, contraprestación no recibida, diferencia de precios, error de cálculos, diferencia de plazos | Comerciales: `MotivoRechazo.OrdenDeCompraVencida()`… |
| `E27`-`E60` | A definir entre las partes | `new MotivoRechazo("E30", "…")` |

La respuesta del servicio web de intercambio del otro emisor (`EnviarIntercambioAsync`) es un código: `1000`,
`2000`, `3000` o `4000` recibido (sobre, acuse, respuesta o anulación), `x001` rechazado y `x002` error al recibir
(reintentar más tarde).
