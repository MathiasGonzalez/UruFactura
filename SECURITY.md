# Política de Seguridad — UruFactura

## Versiones con soporte de seguridad

| Versión | Soporte de seguridad |
|---------|----------------------|
| 1.x (actual) | ✅ |
| < 1.0 (pre-release) | ❌ |

Solo la rama principal (`main`) y el paquete NuGet publicado más reciente reciben actualizaciones de seguridad.

---

## Reportar una vulnerabilidad

Usá el canal de **[Private Vulnerability Reporting](https://github.com/MathiasGonzalez/UruFactura/security/advisories/new)** de GitHub para reportar vulnerabilidades de forma confidencial.

**Por favor, no reportes vulnerabilidades de seguridad en issues públicos.**

### Qué información incluir

- Descripción clara del problema y su impacto potencial.
- Pasos reproducibles (código de ejemplo, versión de UruFactura afectada).
- Entorno (sistema operativo, versión de .NET).
- Si encontraste una mitigación o workaround, incluilos.

### Qué esperar

- **Confirmación de recepción**: dentro de las 72 horas hábiles.
- **Evaluación inicial**: dentro de los 7 días.
- **Resolución y publicación del advisory**: coordinada con el reportante antes de la divulgación pública.

---

## Consideraciones de seguridad de UruFactura

### 🔑 Certificados digitales y claves privadas

- **Nunca hardcodees** `PasswordCertificado` en el código fuente. Usá variables de entorno o un gestor de secretos (Azure Key Vault, AWS Secrets Manager, .NET User Secrets, etc.).
- UruFactura carga el certificado con `X509KeyStorageFlags.EphemeralKeySet` para evitar persistir la clave privada en el almacén del sistema operativo — recomendado para contenedores y Linux.
- El objeto `UruFacturaClient` implementa `IDisposable`; asegurate de usar `using` para liberar el certificado de memoria cuando ya no lo necesitás.

```csharp
// ✅ Correcto
PasswordCertificado = Environment.GetEnvironmentVariable("CERT_PASSWORD")!

// ❌ Incorrecto
PasswordCertificado = "mi_password_en_texto_plano"
```

### 🔒 Validación TLS / SSL

- La propiedad `OmitirValidacionSsl` **jamás debe activarse en Producción**; está pensada únicamente para el ambiente de Homologación cuando la CA del servidor no es de confianza local.
- En producción el SDK valida el certificado TLS del servidor DGI por defecto.

### 📄 XML y firma digital

- UruFactura firma los CFE con **XAdES-BES usando RSA-SHA256**, en línea con los requisitos de la DGI.
- Los XML firmados contienen el certificado público del emisor embebido en `<KeyInfo>`; no incluyen la clave privada.
- Los archivos XML firmados deben conservarse según el Código Tributario (mínimo 5 años). Asegurate de que el almacenamiento tenga controles de acceso adecuados.

### 🌐 Comunicación con la DGI

- Todas las comunicaciones usan **HTTPS**. Las URLs de producción y homologación están definidas en `UruFacturaConfig.DgiSoapBaseUrl` y no son modificables en tiempo de ejecución (solo dependen del `Ambiente` configurado).
- Si usás un proxy corporativo, configurá el `HttpClient` correspondiente en tu infraestructura; el SDK no expone configuración de proxy directamente.

### 🗝️ CAE (Constancia de Autorización de Emisión)

- El CAE es un rango de números de comprobante autorizado por la DGI. No lo compartas públicamente ni lo expongas en logs.
- `CaeManager` es thread-safe: el método `ObtenerProximoNumero` usa un lock para garantizar secuencialidad en entornos concurrentes.

### 🧪 Ambiente de homologación vs. producción

- UruFactura por defecto apunta a **Homologación**. Verificá que `Ambiente = Ambiente.Produccion` esté correctamente configurado antes del despliegue en producción.
- Los CFE enviados en Homologación no tienen validez fiscal.

---

## Dependencias de seguridad relevantes

| Paquete | Uso en el SDK |
|---------|--------------|
| `System.Security.Cryptography.Xml` | Firma XML (XAdES-BES) |
| `System.Security.Cryptography.Pkcs` | Manejo de certificados PKCS#12 |
| `QuestPDF` | Generación de PDFs |
| `ZXing.Net` | Generación de código QR en el PDF |

Mantené estas dependencias actualizadas; el SDK sigue las actualizaciones de parches de seguridad de .NET 10.
