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
- **Comprobante rechazado (`BE`)**: no se reenvía; se corrige y se emite uno nuevo (con otro número).
- **Error de comunicación al enviar** (timeout, red): no se sabe si la DGI recibió el sobre. Consultá el
  comprobante con `ConsultarCfeAsync` y reenviálo solo si la DGI no lo tiene; un reenvío de algo ya
  recibido la DGI lo trata como duplicado.
- `EnviarAsync` no reenvía un CFE que ya fue recibido o tiene resultado (`InvalidOperationException`).
- **Reporte diario**: el primer envío de una fecha usa `secEnvio` 1; para corregir uno ya recibido se
  reenvía completo con la secuencia anterior + 1.
