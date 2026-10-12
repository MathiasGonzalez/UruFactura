# Runbook: Actualización de normativa DGI

Guía estructurada para que agentes (Copilot, CI, desarrolladores) evalúen si hay cambios normativos de la DGI que requieran actualizar UruFactura.

---

## 1. Fuentes de cambios a monitorear

| Fuente | Qué buscar |
|--------|-----------|
| [DGI — Factura Electrónica](https://www.dgi.gub.uy/wdgi/page?2,factura-electronica,index,O,es,0,) | Nueva versión del formato CFE, XSDs, instructivos técnicos |
| [Resoluciones DGI](https://www.dgi.gub.uy/wdgi/page?2,principal,ampliacion-normativa-resoluciones) | Cambios en obligados, plazos, nuevos tipos de CFE |
| [gosocket.net — Centro de recursos](https://gosocket.net/centro-de-recursos/) | Resúmenes y análisis de cambios técnicos DGI |
| [datalogic.com.uy](https://datalogic.com.uy/) | Análisis de cambios de formato CFE |

**Palabras clave para búsqueda web:**
```
DGI Uruguay CFE formato versión <AÑO> cambios
DGI Uruguay factura electrónica nueva normativa <AÑO>
DGI Uruguay XSD CFE actualización <AÑO>
```

---

## 2. Checklist de evaluación

Al ejecutar este runbook, el agente debe responder cada punto y actuar si es necesario.

### 2.1 Versión del formato CFE

- [ ] ¿La DGI publicó una nueva versión del esquema CFE? (actualmente implementada: documento de formato **v25.2**, paquete XSD **1.44.2**; el atributo `version` del XML es `1.0`)
  - Si sí: actualizar `version="xx.xx"` en `src/UruFactura/Xml/CfeXmlBuilder.cs` (línea con `WriteAttributeString("version", ...)`)
  - Actualizar el comentario del `summary` de la clase `CfeXmlBuilder`

### 2.2 Nuevos campos o cambios en campos existentes

Para cada cambio de campo en el nuevo formato, evaluar:

| Zona | Pregunta |
|------|---------|
| **A — IdDoc** | ¿Nuevos campos en `EscribirIdDoc`? ¿Cambios en `FmaPago`, `MntBruto`, `IndTraslado`? |
| **B — Emisor** | ¿Nuevos campos obligatorios del emisor? |
| **C — Receptor** | ¿Nuevos campos obligatorios del receptor? |
| **D — Totales** | ¿Nuevas tasas de IVA? ¿Nuevos elementos de totales? |
| **E — Detalle** | ¿Nuevos campos en líneas de detalle (ej: código de producto GTIN)? |
| **F — Referencia** | ¿Cambios en campos de referencias (como ocurrió en v25.01 con F-C8/F-C9/F-C10)? |
| **G — CAE** | ¿Cambio en validación del rango (ej: G-C3 ≥ G-C2 en v25.01)? |

**Archivos a editar según cambio:**

| Cambio | Archivo |
|--------|---------|
| Nuevo campo en el XML | `src/UruFactura/Xml/CfeXmlBuilder.cs` |
| Nuevo campo en modelo | `src/UruFactura/Models/Cfe.cs`, `RefCfe.cs`, `Receptor.cs`, `LineaDetalle.cs` |
| Nuevo valor de enum | `src/UruFactura/Enums/*.cs` |
| Nueva validación de negocio | `src/UruFactura/Models/Cfe.cs` (método `Validar`) |
| Nuevo campo obligatorio de config | `src/UruFactura/Configuration/UruFacturaConfig.cs` |

### 2.3 Nuevos tipos de CFE

- [ ] ¿La DGI habilitó nuevos tipos de comprobante?
  - Si sí: agregar en `src/UruFactura/Enums/TipoCfe.cs`
  - Agregar método de creación en `src/UruFactura/UruFacturaClient.cs`
  - Actualizar arrays `TiposCorreccion`, `TiposFactura`, `TiposRemito`, `TiposExportacion` en `Cfe.cs` y la familia (`eTck`, `eFact`, ...) en `CfeXmlBuilder`

### 2.4 Cambios en endpoints SOAP

- [ ] ¿Cambiaron las URLs de los servicios SOAP de la DGI (homologación o producción)?
  - Si sí: actualizar `DgiEnvioUrl` / `DgiConsultasUrl` en `src/UruFactura/Configuration/UruFacturaConfig.cs` y los WSDL de `tests/UruFactura.Tests/Schemas/dgi`

### 2.5 Cambios en tasas de IVA

- [ ] ¿La DGI modificó alguna tasa de IVA?
  - Si sí: actualizar constantes en `src/UruFactura/Models/Cfe.cs` (método `CalcularTotales`)
  - Actualizar tabla en `docs/FACTURACION_URUGUAY.md`

---

## 3. Pasos de implementación

Cuando se detecta un cambio que requiere actualización:

1. **Explorar el código afectado** con `grep` para encontrar todas las ocurrencias del campo/valor a cambiar.
2. **Editar el modelo** si se agregan campos (nullables si son opcionales, con documentación XML `<summary>`).
3. **Editar el builder XML** para emitir o no emitir los nuevos campos según el modelo.
4. **Agregar validaciones de negocio** en `Cfe.Validar()` si el campo es obligatorio bajo ciertas condiciones.
5. **Escribir tests** en `tests/UruFactura.Tests/`:
   - Test que verifica que el XML emite el nuevo campo cuando está seteado.
   - Test que verifica que **no** se emite cuando no corresponde.
   - Test de validación si se agrega lógica en `Validar()`.
6. **Correr los tests**: `dotnet test UruFactura.slnx`
7. **Actualizar docs**:
   - `docs/FACTURACION_URUGUAY.md`: normativa, ejemplos de código
   - `docs/CERTIFICACION_DGI.md`: si cambian procesos de homologación
   - `README.md`: si cambia la característica principal (ej: versión del formato)
8. **Actualizar este runbook**: actualizar la versión implementada en la sección 2.1.

---

### Actualizar los XSD/WSDL de referencia

Los tests validan el XML generado contra los XSD oficiales (`tests/UruFactura.Tests/Schemas/dgi`). Al publicarse una versión nueva:

1. Descargá el paquete `xsds_fe_<versión>` y los WSDL (`…/ePrueba/ws_eprueba?wsdl`, `…/ws_consultasPrueba?wsdl`) desde [Documentos de interés](https://www.efactura.dgi.gub.uy/principal/ampliacion_de_contenido/documentos-de-interes?es).
2. Reemplazá los archivos de esa carpeta y corré `dotnet test`: las diferencias aparecen como errores de validación.

## 4. Registro de actualizaciones de normativa

| Fecha | Versión CFE | Cambios implementados |
|-------|-------------|----------------------|
| 2026-10-12 | XSD 1.44.2 | Alineación con los XSD y WSDL oficiales de la DGI: `version="1.0"`, estructura `eTck`/`eFact`/..., `CAEData`, `TmstFirma`, sobre `EnvioCFE`, SOAP `Datain/xmlData` con WS-Security, reporte diario agregado; corrección de códigos de tipo (182 = e-Resguardo, 124 = e-Remito de exportación) |
| 2026-06-08 | v25.2 | Versión XML 25.01→25.2 tras revisión normativa DGI de junio 2026 |
| 2026-05-21 | v25.01 | Versión XML 23.01→25.01; campos F-C8 (MntCFERef), F-C9 (MonedaCFERef), F-C10 (TpoCambioCFERef) en RefCfe; validación de Ciudad y Departamento del emisor; validación de MontoCfeRef/MonedaCfeRef en notas correctivas |
| — | v23.01 | Versión base inicial |

---

## 5. Notas para agentes

- Al correr este runbook, reportar primero si **hay o no hay cambios** que requieran acción.
- Si no hay cambios: no modificar código ni documentación.
- Si hay cambios: abrir un PR con los cambios y marcar los ítems completados del checklist.
- Siempre correr `dotnet test UruFactura.slnx` antes de dar por terminada la tarea.
- El workflow `.github/workflows/normativa-dgi-check.yml` crea automáticamente un issue mensual de recordatorio para ejecutar este runbook.
