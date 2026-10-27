# Runbook: Actualización de normativa DGI

Guía estructurada para que agentes (Copilot, CI, desarrolladores) evalúen si hay cambios normativos de la DGI que requieran actualizar UruFactura.

---

## 1. Fuentes de cambios a monitorear

| Fuente | Qué buscar |
|--------|-----------|
| [DGI — Factura Electrónica](https://www.dgi.gub.uy/wdgi/page?2,factura-electronica,index,O,es,0,) | Nueva versión del formato CFE, XSDs, instructivos técnicos |
| [e-Factura — Documentos de interés](https://www.efactura.dgi.gub.uy/principal/ampliacion_de_contenido/documentos-de-interes?es) | Versiones vigentes (Publicado/Sustituido) del formato CFE, XSD, reporte, mensajes de respuesta, sobre, WSDL e intercambio |
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
- [ ] ¿Cambió alguno de los otros formatos? Implementados: formato del reporte **v13.2**, formato de los mensajes de respuesta **v19** (motivos S01-S20, E01-E60), formato del sobre **v05** (2 MB, 250 CFE) y estándar de intercambio por servicios web **v1.3**.
  - Si sí: actualizar `version="xx.xx"` en `src/UruFactura/Xml/CfeXml.cs` (línea con `WriteAttributeString("version", ...)`)
  - Actualizar el comentario del `summary` de la clase `CfeXml`

### 2.2 Nuevos campos o cambios en campos existentes

Para cada cambio de campo en el nuevo formato, evaluar:

| Zona | Pregunta |
|------|---------|
| **A — IdDoc** | ¿Nuevos campos en `EscribirIdDoc`? ¿Cambios en `FmaPago`, `MntBruto`, `TipoTraslado` (el `IndTraslado` del modelo)? |
| **B — Emisor** | ¿Nuevos campos obligatorios del emisor? |
| **C — Receptor** | ¿Nuevos campos obligatorios del receptor? |
| **D — Totales** | ¿Nuevas tasas de IVA? ¿Nuevos elementos de totales? |
| **E — Detalle** | ¿Nuevos campos en líneas de detalle (ej: código de producto GTIN)? |
| **F — Referencia** | ¿Cambios en campos de referencias (como ocurrió en v25.01 con F-C8/F-C9/F-C10)? |
| **G — CAE** | ¿Cambio en validación del rango (ej: G-C3 ≥ G-C2 en v25.01)? |

**Archivos a editar según cambio:**

| Cambio | Archivo |
|--------|---------|
| Nuevo campo en el XML | `src/UruFactura/Xml/CfeXml.cs` |
| Nuevo campo en modelo | `src/UruFactura/Models/Cfe.cs`, `RefCfe.cs`, `Receptor.cs`, `LineaDetalle.cs` (records inmutables) |
| Nuevo dato del emisor | `src/UruFactura/Emisor.cs` (incluye su validación) |
| Nuevo valor de enum | `src/UruFactura/Enums/*.cs` |
| Nueva validación de negocio | `src/UruFactura/Models/Cfe.cs` (método `Validar`) |
| Nuevo campo obligatorio de config | `src/UruFactura/UruFacturaConfig.cs` (método `Validar`) |
| Cambio en el reporte diario | `src/UruFactura/Xml/ReporteDiario.cs` |
| Cambio en la representación impresa | `src/UruFactura.Pdf/CfePdf.cs` y `CfeEmitido.UrlQr` (QR) |
| Nuevo indicador de facturación | `src/UruFactura/Enums/IndicadorFacturacion.cs`, `Cfe.CalcularTotales`/`IndicadorInformado`/`IndicadorEscrito`/`IndicadorAdmitido`/`Validar`, `Totales` y los totales de `CfeXml` y `ReporteDiario` |
| Nueva moneda | La tabla de `TipMonType` en `src/UruFactura/Enums/Moneda.cs` |
| Nuevo tipo de CAE especial | `src/UruFactura/Enums/CaeEspecial.cs`, `Cae.Validar`, `Cfe.ValidarConCae` y `CfePdf.LeyendaEmisor` |
| Cambio en el sobre, los acuses o el intercambio | `src/UruFactura/Xml/CfeSobre.cs` y `src/UruFactura/Xml/Intercambio.cs` (motivos de rechazo, controles del receptor, `ACKSobre`, `ACKCFE`, anulación) |
| Nuevo servicio o consulta de la DGI | `src/UruFactura/Soap/DgiSoapClient.cs`, `DgiRespuestaParser.cs` e `IDgiClient` |

### 2.3 Nuevos tipos de CFE

- [ ] ¿La DGI habilitó nuevos tipos de comprobante?
  - Si sí: agregar en `src/UruFactura/Enums/TipoCfe.cs`
  - Clasificarlo en `src/UruFactura/Enums/TipoCfeExtensions.cs`: `Familia()` (elemento raíz `eTck`, `eFact`, ... que usa `CfeXml`), `EsNotaCorreccion()`, `EsCuentaAjena()`, `RequiereReceptor()`, etc. `Cfe.Validar()` y `CfeXml` se basan en esas extensiones
  - Agregar su resumen en la tabla `Resumenes` de `src/UruFactura/Xml/ReporteDiario.cs` y su nombre impreso en `CfePdf.NombreCompleto`

### 2.4 Cambios en endpoints SOAP

- [ ] ¿Cambiaron las URLs de los servicios SOAP de la DGI (homologación o producción)?
  - Si sí: actualizar `UrlEnvioHomologacion` / `UrlConsultasHomologacion` en `src/UruFactura/UruFacturaConfig.cs` y los WSDL de `tests/UruFactura.Tests/Schemas/dgi`

### 2.5 Cambios en tasas de IVA

- [ ] ¿La DGI modificó alguna tasa de IVA?
  - Si sí: actualizar las tasas en `src/UruFactura/Models/Cfe.cs` (método `CalcularTotales`) y las que se informan en `Xml/CfeXml.cs` y `Xml/ReporteDiario.cs` (`IVATasaMin`, `IVATasaBasica`)
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
9. **Publicar**: subir `<Version>` en los tres `.csproj` de `src/` y, ya en `main`, crear el tag `v<versión>` (`git tag v2.0.1 && git push origin v2.0.1`). El workflow `publish.yml` publica en NuGet solo con el tag, y falla si el tag no coincide con las versiones de los `.csproj`.

---

### Actualizar los XSD/WSDL de referencia

Los tests validan el XML generado contra los XSD oficiales (`tests/UruFactura.Tests/Schemas/dgi`). Al publicarse una versión nueva:

1. Descargá el paquete `xsds_fe_<versión>` y los WSDL (`…/ePrueba/ws_eprueba?wsdl`, `…/ws_consultasPrueba?wsdl` y el de intercambio, `wsdl_aws_efactura_intercambio.xml`) desde [Documentos de interés](https://www.efactura.dgi.gub.uy/principal/ampliacion_de_contenido/documentos-de-interes?es).
2. Reemplazá los archivos de esa carpeta y corré `dotnet test`: las diferencias aparecen como errores de validación. Además de los del envío a la DGI (`EnvioCFE`, `ReporteDiarioCFE`, `ACK*`), los tests usan los del intercambio: `EnvioCFE_entreEmpresas.xsd` (con `CFE_Empresas_Type.xsd`), `ACKCFE_Partes.xsd` y `CFEs_Anulados_x_RechazoDGI.xsd` (estos dos se validan por separado: redefinen tipos de los otros).

## 4. Registro de actualizaciones de normativa

| Fecha | Versión CFE | Cambios implementados |
|-------|-------------|----------------------|
| 2026-10-26 | v25.2 / XSD 1.44.2 | Cobertura completa del formato: e-Boleta de entrada (151-153, 251-253: indicadores 13, 14, 15 y 17, `MntBruto` 2, compra de moneda extranjera `IndCompraMEReventa`, complemento fiscal, zonas `Rsmn_Bol*` del reporte); indicadores 8 (rebaja de e-Remito, resta en el de exportación) y 9 (anulación de e-Resguardo, resta por código); retenciones y percepciones en e-Tickets, e-Facturas y e-Boletas (`IndAgenteResp`, `MntTotRetenido`, `MntTotCredFisc`, `MntPagar` A-C130, `TotMntRetenido` en el reporte); `MntBruto` 1 (IVA incluido, A-C116/A-C117); `SecProf`, `IndPagCta3ros` (grupo propio en el reporte, B-C11.1), `IndCobPropia`, `PeriodoDesde`/`PeriodoHasta`, `FchValor`; bienes de terceros (`IndPropiedad` y propietario, A-C6.1 a A-C6.6); `SubTotInfo`; `RecargoPct`, `SubDescuento`/`SubRecargo`, `NCM`; cantidad negativa; `ModVenta` 80/90 en e-Tickets y e-Facturas y la validación por tipo (A-C14); vía de transporte 8; monedas de `TipMonType`; NIE y país de cada documento (A-C61, K-C3), C.I. con dígito verificador, país "99"; receptor obligatorio según el tipo (A-C60 a A-C66.1) y por retenciones en el e-Ticket; precio mayor a cero (B-C11); códigos de retención numéricos (B-C20); GTIN (B-C3); fecha hasta +60 días (A-C5, F-C7); hasta 200 líneas fuera del e-Ticket; UI por año (`UnidadIndexada`); sin `MntBruto` 3 en cuenta ajena (K-C1). Pendientes: el dígito verificador del NIE (el algoritmo no está publicado) y el redondeo de `MntBruto` 1 se confirman en homologación |
| 2026-10-25 | v25.2 / XSD 1.44.2 | Reporte diario según el formato v13.2 (CFE por fecha de firma, montos en pesos agrupados por fecha de comprobante, anulados por rechazo de la DGI salvo E02/E09, `CantDocsEmi`, `CantDocsAnulados`, `CantDocsMay_topeUI`); referencias v25 (tipo de cambio obligatorio si no es en pesos, sin repetidas, sin monto en el e-Remito, referencia global, fecha opcional); todos los indicadores de facturación de e-Ticket/e-Factura/exportación (4, 5, 6, 7, 10, 11, 12, 16) con `MontoNF`/`MntPagar` y el QR por `MntPagar`; CAE especiales (`CAEEspecial`, `CausalCAEEsp`, `MntBruto` 3); `IVAalDia`; créditos fiscales del e-Resguardo (`MntTotCredFisc`); descuentos globales en porcentaje; sobre de hasta 2 MB con el certificado de los CFE; adenda; intercambio entre emisores (sobre `EnvioCFE_entreEmpresas`, controles y acuses del receptor `ACKSobre`/`ACKCFE`, anulación, servicio web de intercambio) y consulta del padrón de emisores; representación impresa con forma de pago, "CONSUMO FINAL"/"RUC COMPRADOR", leyendas de CAE especial, constancia de IVA al día, recuadro del vencimiento y adenda; `ValorUnidadIndexada` obligatorio en Producción |
| 2026-10-13 | XSD 1.44.2 | Soporte de e-Resguardo (retenciones), venta por cuenta ajena, contingencia, descuentos/recargos globales en monto, medios de pago, datos adicionales; QR y pie de la representación impresa según el formato; validación del RUT (dígito verificador), tope de 5.000 UI para e-Tickets y límites de longitud del XSD; carga de CAE desde el repositorio |
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
