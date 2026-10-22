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
- **Modelo CFE:** el modelo principal es `Cfe` en `src/UruFactura/Models/Cfe.cs`. Siempre agregar validaciones en `Cfe.Validar()` cuando un campo se vuelve obligatorio.
- **XML builder:** `CfeXmlBuilder.cs` usa `XmlWriter` (no interpolación de strings) para evitar vulnerabilidades XSS.
- **Serialización XML:** usar `MemoryStream` + `UTF8Encoding(false)` (sin BOM). No usar `StringBuilder` para la salida XML.
- **Fechas fiscales:** usar `HoraUruguay.Hoy()` / `HoraUruguay.Ahora()`, nunca `DateTime.Today` ni `DateTime.Now` (los servidores suelen estar en UTC).
- **Lite:** `UruFactura.Lite` y `UruFactura.Lite.EntityFrameworkCore` compilan las mismas fuentes enlazadas; al agregar una carpeta o un archivo en la raíz de `src/UruFactura`, enlazarlo en `UruFactura.Lite.csproj`.
- **Tests:** cada nueva funcionalidad debe tener tests en `tests/UruFactura.Tests/`. Usar xUnit.
- **Build y tests:** `dotnet test UruFactura.slnx` corre todo.

---

## Archivos clave

| Archivo | Propósito |
|---------|-----------|
| `src/UruFactura/Xml/CfeXmlBuilder.cs` | Generación del XML del CFE |
| `src/UruFactura/Models/Cfe.cs` | Modelo principal + validaciones |
| `src/UruFactura/Models/RefCfe.cs` | Modelo de referencias (notas de crédito/débito) |
| `src/UruFactura/Configuration/UruFacturaConfig.cs` | Config del emisor + validaciones |
| `src/UruFactura/Enums/TipoCfe.cs` | Tipos de comprobante |
| `src/UruFactura/Cae/ICaeRepositoryCompartido.cs` | Numeración atómica entre instancias |
| `src/UruFactura/Persistence/ICfeRepository.cs` | Registro de CFE emitidos (fuente del reporte diario) |
| `src/UruFactura.EntityFrameworkCore/` | Implementación EF Core (CAS sobre `UruFacturaCaes`, tablas `UruFacturaCfes` y `UruFacturaSecuencias`) |
| `docs/NORMATIVA_DGI_UPDATE.md` | Runbook de actualización normativa |
| `docs/FACTURACION_URUGUAY.md` | Guía de facturación con ejemplos |
