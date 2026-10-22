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

| Estado | Mensaje DGI | Significado | Dónde lo ves |
|--------|-------------|-------------|--------------|
| `AS` | `ACKSobre` | Sobre recibido (aún no implica que los CFE hayan sido aceptados) | `acuse.Recibido` |
| `BS` | `ACKSobre` | Sobre rechazado — motivos `S01`…`S20` | `acuse.Estado`, `acuse.Motivos` |
| `BA` | `ACKSobre` / `ACKRepDiario` | Archivo rechazado | `acuse.Estado` |
| `AR` | `ACKRepDiario` | Reporte diario recibido | `AcuseReporte.Recibido` |
| `BR` | `ACKRepDiario` | Reporte diario rechazado | `AcuseReporte.Motivos` |
| `AE` | `ACKCFE` | Comprobante aceptado | `cfe.AceptadoPorDgi` |
| `BE` | `ACKCFE` | Comprobante rechazado (CFE) | `cfe.EstadoDgi`, `cfe.MensajeDgi` |
| `CE` | `ACKCFE` | Comprobante observado (CFC) | `cfe.EstadoDgi` |
| `FE` | `ACKCFE` | Comprobante reliquidado | `cfe.EstadoDgi` |

Los errores de comunicación **no** son estados: se lanzan como `DgiCommunicationException`.

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
estado del comprobante (`AE`, `BE`, `CE`, `FE`) y lo guarda en el `CfeEmitido` y en el almacenamiento. Es
útil tras un reinicio, cuando ya no se tiene el acuse del sobre.
