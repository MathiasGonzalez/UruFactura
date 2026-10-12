---
title: Códigos DGI
description: Estados de respuesta de los servicios web de la DGI y cómo manejarlos en el SDK
---

## Cómo responde la DGI

El envío de comprobantes es **asíncrono** y en dos pasos. Los estados provienen de los esquemas
oficiales (`ACKSobre`, `ACKCFE`, `ACKRepDiario`):

1. **Envío del sobre** (`EnviarCfeAsync` / `EnviarCfesAsync`): la DGI responde con un acuse del sobre.
2. **Consulta del resultado** (`ConsultarEstadoEnvioAsync`): con el `IdReceptor` y el `Token` del acuse, la
   DGI informa el estado de cada comprobante.

| Estado | Mensaje DGI | Significado |
|--------|-------------|-------------|
| `AS` | `ACKSobre` | Sobre recibido (aún no implica que los CFE hayan sido aceptados) |
| `BS` | `ACKSobre` | Sobre rechazado — los motivos (`S01`…`S20`) vienen en `respuesta.Mensaje` |
| `BA` | `ACKSobre` / `ACKRepDiario` | Archivo rechazado |
| `AR` | `ACKRepDiario` | Reporte diario recibido |
| `BR` | `ACKRepDiario` | Reporte diario rechazado |
| `AE` | `ACKCFE` | Comprobante aceptado |
| `BE` | `ACKCFE` | Comprobante rechazado (CFE) |
| `CE` | `ACKCFE` | Comprobante observado (CFC) |
| `FE` | `ACKCFE` | Comprobante reliquidado |

Además, el SDK usa estos valores en `RespuestaDgi.Codigo` cuando no hay un acuse:

| Código | Significado |
|--------|-------------|
| `SOAP_FAULT` | La DGI respondió con un SOAP Fault (por ejemplo, firma WS-Security inválida) |
| `4xx` / `5xx` | Error HTTP sin cuerpo SOAP (proxy, servicio caído) |

`RespuestaDgi.Exitoso` es `true` para `AS` y `AR`, y para la consulta del envío cuando **todos** los
comprobantes están en `AE`.

---

## Cómo manejarlos en el SDK

```csharp
var envio = await client.EnviarCfeAsync(cfe);

if (!envio.Exitoso)
{
    // BS / BA / SOAP_FAULT / HTTP: el mensaje incluye los motivos informados por la DGI
    logger.LogError("Sobre rechazado [{Codigo}]: {Mensaje}", envio.Codigo, envio.Mensaje);
    return;
}

// La DGI indica desde cuándo se puede consultar el resultado
if (envio.ConsultarDesde is { } desde && desde > DateTimeOffset.Now)
    await Task.Delay(desde - DateTimeOffset.Now);

var estado = await client.ConsultarEstadoEnvioAsync(envio, [cfe]);

foreach (var c in estado.Comprobantes)
    Console.WriteLine($"{c.TipoCfe} {c.Serie}-{c.Numero}: {c.Estado} {string.Join("; ", c.Motivos)}");

if (cfe.AceptadoPorDgi)
    Console.WriteLine("CFE aceptado ✅");   // estado AE
```

---

## Consulta del estado de un comprobante

`ConsultarEstadoCfeAsync(cfe)` consulta por tipo, serie y número (servicio `ws_consultas`) y devuelve el
estado del comprobante (`AE`, `BE`, `CE`, `FE`) más el `IdReceptor` y el `Token` del primer sobre que lo
contenía.
