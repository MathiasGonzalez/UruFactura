# Política de Seguridad — UruFactura

## Versiones con soporte de seguridad

| Versión | Soporte de seguridad |
|---------|----------------------|
| 2.x (actual) | ✅ |
| < 2.0 | ❌ |

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
- UruFactura carga el `.p12`/`.pfx` con `X509KeyStorageFlags.EphemeralKeySet` (salvo en macOS, que no lo admite) para no persistir la clave privada en el almacén del sistema operativo.
- También acepta un `X509Certificate2` ya cargado (`UruFacturaConfig.Certificado`), por ejemplo desde Azure Key Vault o un HSM; en ese caso el cliente no lo libera.
- `UruFacturaClient` implementa `IDisposable`: liberalo (o registralo como singleton) para liberar el certificado que cargó.
- `UruFacturaConfig.ToString()` no muestra la contraseña ni el certificado, así que la configuración se puede loguear.

```csharp
// ✅ Correcto
PasswordCertificado = Environment.GetEnvironmentVariable("CERT_PASSWORD")!

// ❌ Incorrecto
PasswordCertificado = "mi_password_en_texto_plano"
```

### 🔒 Validación TLS / SSL

- `OmitirValidacionSsl` es solo para Homologación, cuando la CA del servidor no es de confianza local: en Producción la configuración no es válida y el cliente no se crea.
- Por defecto UruFactura valida el certificado TLS de los servidores de la DGI.

### 📄 XML y firma digital

- UruFactura firma los CFE y el reporte diario con **XML Signature enveloped (RSA-SHA256 por defecto; RSA-SHA1 opcional con `AlgoritmoFirma`)**, con el formato de los ejemplos oficiales de la DGI, y cada mensaje SOAP con **WS-Security** (certificado X.509 en `BinarySecurityToken` y firma del `Body`).
- Los XML firmados contienen el certificado público del emisor embebido en `<KeyInfo>`; no incluyen la clave privada.
- Los archivos XML firmados deben conservarse según el Código Tributario (mínimo 5 años). Asegurate de que el almacenamiento tenga controles de acceso adecuados.
- Las respuestas de la DGI y los XML de terceros se cargan sin procesar DTD (sin entidades externas ni expansión de entidades).
- `FirmaDigital.VerificarFirma(xml)` verifica la firma de un CFE recibido y devuelve el certificado con que se firmó. Una firma válida solo prueba que el documento no cambió desde que lo firmó el titular de ese certificado, que viene en el propio XML: comprobá que pertenece al emisor del CFE (su RUT) y, si corresponde, su cadena de confianza.
- En el intercambio con otros emisores, `RecibirSobreAsync` carga el sobre sin DTD, verifica la firma de cada CFE (rechazo `E04`), controla que el sobre y sus CFE sean para el propio RUT y del emisor de la carátula (`S02`) y que el certificado de la carátula sea legible, esté vigente en la fecha del sobre (`S03`) y sea el de las firmas (`S06`). No valida la cadena del certificado contra la PKI Uruguay: si lo necesitás, hacelo con el certificado de `CfeRecibido.Xml`. `LeerAcuseReceptor`, `LeerRespuestaReceptor` y `LeerNotificacionAnulacion` rechazan los mensajes con firma inválida o dirigidos a otro RUT.
- La adenda no forma parte de la firma del CFE (va fuera, en el sobre al receptor): no la uses para datos que deban estar firmados.

### 🌐 Comunicación con la DGI

- Todas las comunicaciones usan **HTTPS**. En Homologación se usan por defecto los servicios de prueba de la DGI; en Producción `UrlEnvio` y `UrlConsultas` deben configurarse explícitamente con las URL entregadas por la DGI.
- Para un proxy corporativo, pasá un `HttpClient` propio (`new UruFacturaClient(config, httpClient: …)`); su handler debe tener el certificado de cliente TLS y la validación del servidor configurados.
- UruFactura no envía datos a ningún otro servicio: solo se comunica con las URL de la DGI configuradas.

### 🗝️ Numeración (CAE)

- El almacenamiento (`IUruFacturaStore`) es la fuente de verdad de la numeración: cada número se asigna en la misma operación atómica que registra el CFE, así que no se repite (tampoco con varias instancias de la aplicación) ni queda consumido sin comprobante.
- En Producción el cliente exige un almacenamiento durable (por ejemplo, `EfUruFacturaStore`): con uno en memoria, un reinicio volvería a emitir números ya usados. Protegé la base como cualquier registro contable.
- No se admiten CAE de la misma serie con números en común.

### 🧪 Ambiente de homologación vs. producción

- UruFactura por defecto apunta a **Homologación**. Verificá que `Ambiente = Ambiente.Produccion` esté correctamente configurado antes del despliegue en producción.
- Los CFE enviados en Homologación no tienen validez fiscal.

---

## Dependencias de seguridad relevantes

| Paquete | Uso en UruFactura |
|---------|--------------|
| `System.Security.Cryptography.Xml` | Firma XML (XML Signature / WS-Security), en `UruFactura` |
| `FluentReport`, `SkiaSharp` | Generación de los PDF, en `UruFactura.Pdf` |
| `ZXing.Net` | Código QR del PDF, en `UruFactura.Pdf` |
| `Microsoft.EntityFrameworkCore` | Almacenamiento en base de datos, en `UruFactura.EntityFrameworkCore` |

Mantené estas dependencias actualizadas; UruFactura sigue las actualizaciones de parches de seguridad de .NET 10.
