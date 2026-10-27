# Instrucciones para GitHub Copilot — UruFactura

## Contexto del proyecto

UruFactura es una librería .NET 10 C# para integrar sistemas con la facturación electrónica de la DGI (Dirección General Impositiva) de Uruguay. Implementa el formato CFE (Comprobante Fiscal Electrónico) v25.2 (XSD 1.44.2; el atributo `version` del XML es `1.0`). Los XSD/WSDL oficiales están en `tests/UruFactura.Tests/Schemas/dgi` y los tests validan el XML generado contra ellos.

---

## Cuando te pidan revisar normativa DGI

Sigue el runbook en [`docs/NORMATIVA_DGI_UPDATE.md`](docs/NORMATIVA_DGI_UPDATE.md).

**Pasos mínimos:**
1. Busca en la web cambios recientes en el formato CFE o normativas DGI Uruguay.
2. Compara con la versión implementada (paquete XSD en `tests/UruFactura.Tests/Schemas/dgi/version.txt`).
3. Si no hay cambios: responde "Sin cambios detectados" y no modifiques código.
4. Si hay cambios: implementa según el runbook, actualiza los docs, y corre `dotnet test UruFactura.slnx`.

---

## Convenciones de este proyecto

- **Idioma del código:** español para nombres de propiedades, modelos y métodos de dominio. Inglés para nombres técnicos estándar (.NET, SOAP, XML).
- **Modelo CFE:** `Cfe` (`src/UruFactura/Models/Cfe.cs`) es un record inmutable con el contenido del documento; `UruFacturaClient.EmitirAsync` lo valida, numera, firma y devuelve un `CfeEmitido`. Toda restricción del XSD o del formato va en `Cfe.Validar()` (las del emisor en `Emisor.Validar()`, las del CAE en `Cae.Validar()`, y las que dependen del CAE con que se numera —CAE especial— en `Cfe.ValidarConCae()`), con el campo afectado en cada `ErrorValidacion`: lo que pasa la validación debe producir un XML válido (`ValidacionContraXsdTests` lo comprueba con CFE aleatorios, que deben cubrir todos los tipos, indicadores de facturación y CAE especiales). Los límites comunes (montos, fechas, textos, series, países) están en `Validacion`.
- **Indicadores de facturación:** `IndicadorFacturacion` (el valor es el código de la DGI) decide en qué total va cada línea y cada descuento/recargo (`Cfe.CalcularTotales`, `Cfe.IndicadorInformado`). El total a pagar (`MntPagar`) suma el monto no facturable y es el monto del QR.
- **Reporte diario:** resume los CFE por fecha de firma en Uruguay (`CfeEmitido.FechaReporte`), en pesos, con los rechazados por la DGI como anulados (`CfeEmitido.AnuladoPorRechazo`). Formatos: reporte v13.2, mensajes de respuesta v19, sobre v05.
- **Intercambio entre emisores:** `Xml/Intercambio.cs` (controles y acuses del receptor, anulación) y `Xml/CfeSobre.cs` (sobres); la adenda nunca va a la DGI.
- **API pública mínima:** todo lo público va en el namespace `UruFactura`. El XML, la firma, el sobre, el reporte y el cliente SOAP son internos; las costuras públicas son `IUruFacturaClient`, `IUruFacturaStore` e `IDgiClient`. Clases `sealed`, sin métodos `virtual`.
- **XML builder:** `CfeXml.cs` usa `XmlWriter` (no interpolación de strings) para evitar vulnerabilidades XSS.
- **Serialización XML:** usar `MemoryStream` + `UTF8Encoding(false)` (sin BOM). No usar `StringBuilder` para la salida XML.
- **Fechas fiscales:** usar `HoraUruguay.Hoy()` / `HoraUruguay.Ahora()` (con el `TimeProvider` del cliente), nunca `DateTime.Today` ni `DateTime.Now` (los servidores suelen estar en UTC).
- **Paquetes:** `UruFactura` (core, sin dependencias de terceros), `UruFactura.Pdf` (FluentReport/SkiaSharp/ZXing) y `UruFactura.EntityFrameworkCore`. El core no debe depender de librerías de PDF.
- **Tests:** cada nueva funcionalidad debe tener tests en `tests/UruFactura.Tests/`. Usar xUnit; `TestData` y `DgiStub` tienen los datos y la DGI simulada.
- **Build y tests:** `dotnet test UruFactura.slnx` corre todo.

---

## Archivos clave

| Archivo | Propósito |
|---------|-----------|
| `src/UruFactura/UruFacturaClient.cs` | Flujo: registrar CAE, validar, emitir, enviar, consultar, reporte diario |
| `src/UruFactura/Xml/CfeXml.cs` | Generación del XML del CFE |
| `src/UruFactura/Models/Cfe.cs` | Contenido del CFE + totales + validaciones |
| `src/UruFactura/Models/CfeEmitido.cs` | CFE numerado y firmado (XML, CAE, totales, QR, estado ante la DGI) |
| `src/UruFactura/Models/RefCfe.cs` | Referencias (notas de crédito/débito; referencia global) |
| `src/UruFactura/Enums/IndicadorFacturacion.cs` / `CaeEspecial.cs` | Indicadores de facturación y tipos de CAE especial |
| `src/UruFactura/Xml/ReporteDiario.cs` | Reporte diario (formato v13.2) |
| `src/UruFactura/Xml/Intercambio.cs` / `Models/Intercambio.cs` | Intercambio con otros emisores electrónicos |
| `src/UruFactura/UruFacturaConfig.cs` / `Emisor.cs` | Configuración y datos del emisor + validaciones |
| `src/UruFactura/Enums/TipoCfe.cs` / `TipoCfeExtensions.cs` | Tipos de comprobante y su clasificación |
| `src/UruFactura/Persistence/IUruFacturaStore.cs` | Almacenamiento: CAE, numeración (`RegistrarEmisionAsync`: avanza el CAE y guarda el CFE de forma atómica), número de envío y CFE emitidos |
| `src/UruFactura.EntityFrameworkCore/` | Implementación EF Core (`UPDATE` condicional sobre `UruFacturaCaes` en la misma transacción que el `INSERT` en `UruFacturaCfes`; `UruFacturaSecuencias`) |
| `src/UruFactura.Pdf/CfePdf.cs` | Representación impresa (A4, térmico, QR) |
| `docs/NORMATIVA_DGI_UPDATE.md` | Runbook de actualización normativa |
| `docs/FACTURACION_URUGUAY.md` | Guía de facturación con ejemplos |
