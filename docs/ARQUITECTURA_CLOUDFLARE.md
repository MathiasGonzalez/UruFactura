# Arquitectura — UruFactura CloudflareApi

Documento técnico que describe la arquitectura desplegada, los requerimientos de infraestructura y las capacidades de la API.

---

## Índice

- [Visión general](#visión-general)
- [Componentes del sistema](#componentes-del-sistema)
- [Modelo de despliegue](#modelo-de-despliegue)
- [Modos de operación](#modos-de-operación)
- [Requerimientos](#requerimientos)
- [Capacidades](#capacidades)
- [Seguridad](#seguridad)
- [Escalabilidad y límites](#escalabilidad-y-límites)
- [Ciclo de vida del contenedor](#ciclo-de-vida-del-contenedor)
- [Gestión de estado](#gestión-de-estado)
- [Pipeline CI/CD](#pipeline-cicd)
- [Decisiones de diseño](#decisiones-de-diseño)

---

## Visión general

**UruFactura.CloudflareApi** es una API HTTP que expone la funcionalidad de UruFactura (`UruFactura`) como un servicio desplegable en [Cloudflare Containers](https://developers.cloudflare.com/containers/). Soporta operación single-tenant y multi-tenant (SaaS) desde la misma imagen Docker.

```mermaid
graph TD
    subgraph Internet
        Client["Aplicaciones cliente<br/>(ERP, POS, Backend)"]
        Admin["Admin Web<br/>(Cloudflare Pages)"]
    end

    subgraph "Cloudflare Edge (global)"
        Worker["Worker (worker.js)<br/>Enrutamiento por tenant"]
        EmailW["Email Worker<br/>(envío emails)"]
        PF["Pages Functions<br/>(auth backend)"]
        KV["KV Storage<br/>(codes, sessions, tenants)"]
    end

    subgraph "Cloudflare Containers"
        DO1["Durable Object 'default'"]
        DO2["Durable Object 'tenant-a'"]
        DO3["Durable Object 'tenant-b'"]
        C1[".NET Container<br/>(default)"]
        C2[".NET Container<br/>(tenant-a)"]
        C3[".NET Container<br/>(tenant-b)"]
    end

    subgraph "Servicios externos"
        DGI["DGI Uruguay<br/>SOAP Web Services"]
        GHCR["GitHub Container Registry<br/>(imagen Docker)"]
    end

    Client -->|HTTPS| Worker
    Admin -->|HTTPS| PF
    Admin -->|"HTTPS + X-Tenant-Id"| Worker
    PF --> KV
    PF -->|"service binding"| EmailW
    Worker --> DO1
    Worker --> DO2
    Worker --> DO3
    DO1 --> C1
    DO2 --> C2
    DO3 --> C3
    C1 -->|SOAP/HTTPS| DGI
    C2 -->|SOAP/HTTPS| DGI
    C3 -->|SOAP/HTTPS| DGI
    GHCR -.->|pull on startup| C1
    GHCR -.->|pull on startup| C2
    GHCR -.->|pull on startup| C3

    classDef cf fill:#f6821f,color:#fff,stroke:none
    classDef net fill:#512bd4,color:#fff,stroke:none
    classDef ext fill:#555,color:#fff,stroke:none
    classDef pages fill:#2563eb,color:#fff,stroke:none
    class Worker,DO1,DO2,DO3,EmailW cf
    class C1,C2,C3 net
    class DGI,GHCR ext
    class Admin,PF,KV pages
```

---

## Componentes del sistema

### 1. Cloudflare Worker (`cloudflare/worker.js`)

| Aspecto | Detalle |
|---------|---------|
| **Rol** | Punto de entrada público. Recibe todas las solicitudes HTTP y las enruta al contenedor correcto. |
| **Enrutamiento** | Usa el header `X-Tenant-Id` para seleccionar el Durable Object. Sin header → `"default"`. |
| **Runtime** | Cloudflare Workers (V8 isolate, edge global). |
| **Responsabilidades** | Enrutamiento, TLS termination, rate limiting (configurable vía Cloudflare), y opcionalmente autenticación a nivel Worker. |

### 2. Durable Objects

| Aspecto | Detalle |
|---------|---------|
| **Rol** | Gestionan el ciclo de vida de cada contenedor (start, sleep, wake, stop). |
| **Aislamiento** | Cada tenant recibe un Durable Object con nombre único → un contenedor dedicado con estado en memoria completamente aislado. |
| **Sleep** | El contenedor se duerme automáticamente tras 5 min de inactividad (`sleepAfter = "5m"`). |

### 3. Contenedor .NET (`UruFactura.CloudflareApi`)

| Aspecto | Detalle |
|---------|---------|
| **Framework** | ASP.NET Core 10 Minimal API |
| **Puerto** | 8080 (`ASPNETCORE_HTTP_PORTS`) |
| **Imagen base** | `mcr.microsoft.com/dotnet/aspnet:10.0` (Alpine) |
| **Dependencia principal** | `UruFactura` (firma XML, SOAP, PDF, gestión de CAEs) |

```mermaid
graph LR
    subgraph "Contenedor .NET (por tenant)"
        HTTP["HTTP :8080"]
        MW["Minimal API<br/>Endpoints"]
        EXT["HttpContextExtensions<br/>(X-Tenant-Id)"]
        FACTORY["TenantClientFactory<br/>(singleton, ConcurrentDict)"]
        CLIENT["UruFacturaClient"]
        CAE["ICaeManager<br/>(en memoria)"]
        SOAP["DgiSoapClient<br/>(XmlDocument)"]
        PDF["PdfService"]
        FIRMA["FirmaService<br/>(X.509 + XMLDSIG)"]
    end

    HTTP --> MW
    MW --> EXT
    EXT --> FACTORY
    FACTORY --> CLIENT
    CLIENT --> CAE
    CLIENT --> SOAP
    CLIENT --> PDF
    CLIENT --> FIRMA

    classDef core fill:#512bd4,color:#fff,stroke:none
    class FACTORY,CLIENT,CAE,SOAP,PDF,FIRMA core
```

### 4. DGI (Dirección General Impositiva)

| Aspecto | Detalle |
|---------|---------|
| **Protocolo** | SOAP sobre HTTPS |
| **Endpoints** | `efactura.dgi.gub.uy` (Producción) / `efactura.dgi.gub.uy:6443` (Homologación) |
| **Operaciones** | Envío de sobres CFE, consulta de estado, reporte diario |
| **Autenticación** | Certificado digital X.509 (`.p12`) emitido por Correo Uruguayo |

### 5. Admin Web (`admin/`)

| Aspecto | Detalle |
|---------|---------|
| **Rol** | UI de administración de tenants (registro, login, emisión CFE, gestión CAEs) |
| **Stack** | React 19 + Vite 6 + React Router 7 |
| **Hosting** | Cloudflare Pages (SPA estática + Pages Functions) |
| **Auth backend** | Pages Functions (`admin/functions/auth/`) con KV |
| **Storage** | KV: `AUTH_CODES` (10 min TTL), `AUTH_SESSIONS` (24h), `TENANTS` |
| **Deploy workflow** | `.github/workflows/deploy-admin.yml` |

### 6. Email Worker (`cloudflare/email-worker/`)

| Aspecto | Detalle |
|---------|---------|
| **Rol** | Envío de emails transaccionales (códigos de verificación) |
| **Runtime** | Cloudflare Workers (V8 isolate) |
| **Proveedores** | MailChannels (gratis) o API custom (Resend, SendGrid) |
| **Integración** | Service binding desde Pages Functions o HTTP directo |
| **Deploy workflow** | `.github/workflows/deploy-email-worker.yml` |

---

## Modelo de despliegue

```mermaid
graph LR
    subgraph "GitHub"
        REPO["Repositorio<br/>UruFactura"]
        GHA["GitHub Actions"]
        GHCR["GHCR<br/>(Container Registry)"]
    end

    subgraph "Cloudflare"
        SECRETS["Cloudflare Secrets<br/>(cert, passwords, CAEs)"]
        WRANGLER["Wrangler<br/>(deploy tool)"]
        EDGE["Cloudflare Edge<br/>(Worker + Containers)"]
    end

    REPO -->|"push tag v*<br/>workflow_dispatch"| GHA
    GHA -->|"docker build+push"| GHCR
    GHA -->|"wrangler secret put"| SECRETS
    GHA -->|"wrangler deploy"| WRANGLER
    WRANGLER --> EDGE
    GHCR -.->|"image pull"| EDGE

    classDef gh fill:#24292e,color:#fff,stroke:none
    classDef cf fill:#f6821f,color:#fff,stroke:none
    class REPO,GHA,GHCR gh
    class SECRETS,WRANGLER,EDGE cf
```

### Artefactos desplegados

| Artefacto | Destino | Actualización |
|-----------|---------|---------------|
| `worker.js` + `wrangler.toml` | Cloudflare Workers | `wrangler deploy` |
| Imagen Docker (ASP.NET) | GHCR → Cloudflare Containers | Tag SHA en `wrangler.toml` |
| Secretos (cert, passwords) | Cloudflare Secrets | `wrangler secret put` |
| Variables no-sensibles | `wrangler.toml [vars]` | `wrangler deploy` |
| Admin SPA + Pages Functions | Cloudflare Pages | `wrangler pages deploy` |
| Email Worker | Cloudflare Workers | `wrangler deploy` |
| KV Namespaces (auth) | Cloudflare KV | Automático (runtime) |

---

## Modos de operación

### Single-tenant

- Una empresa, un certificado digital, un conjunto de CAEs.
- Configuración bajo `UruFactura__*` (variables de entorno).
- No requiere header `X-Tenant-Id`.
- Todas las solicitudes van al Durable Object `"default"` → un solo contenedor.

### Multi-tenant (SaaS)

- Múltiples empresas con aislamiento completo.
- Cada tenant tiene su propia configuración bajo `Tenants__{id}__*`.
- Cada solicitud incluye `X-Tenant-Id: {tenantId}`.
- Cada tenant recibe su propio Durable Object → su propio contenedor → estado en memoria aislado.
- `max_instances` en `wrangler.toml` debe ser ≥ al número de tenants concurrentes.

```mermaid
sequenceDiagram
    participant App as Aplicación
    participant W as Worker (edge)
    participant DO as Durable Object
    participant C as Contenedor .NET

    App->>W: POST /cfe/enviar<br/>X-Tenant-Id: empresa-abc
    W->>W: tenantId = "empresa-abc"
    W->>DO: getContainer(CONTAINER, "empresa-abc")
    DO->>C: wake container (if sleeping)
    DO->>C: forward request
    C->>C: TenantClientFactory.GetClient("empresa-abc")
    C->>C: Load config Tenants:empresa-abc:*
    C-->>App: 200 OK { Exitoso: true }
```

---

## Requerimientos

### Infraestructura

| Componente | Requisito |
|------------|-----------|
| **Cloudflare** | Cuenta con plan Workers Paid + acceso a Containers (beta) |
| **GHCR** | Repositorio con GitHub Packages habilitado |
| **Certificado DGI** | `.p12` emitido por CA autorizada (Correo Uruguayo) por cada empresa emisora |
| **CAEs** | Al menos un CAE vigente por tipo de CFE que se vaya a emitir |

### Herramientas (desarrollo / CI)

| Herramienta | Versión mínima | Uso |
|-------------|---------------|-----|
| .NET SDK | 10.0 | Build de la API |
| Docker | 24+ | Build de imagen |
| Wrangler CLI | 3+ | Deploy a Cloudflare |
| Node.js | 20+ | Wrangler runtime |

### Secretos GitHub Actions

| Secret | Descripción |
|--------|-------------|
| `CLOUDFLARE_API_TOKEN` | Token API con permisos Workers Scripts + Containers |
| `CLOUDFLARE_ACCOUNT_ID` | ID de la cuenta Cloudflare |
| `URUFACTURA_RUT_EMISOR` | RUT del emisor (12 dígitos) |
| `URUFACTURA_RAZON_SOCIAL` | Razón social |
| `URUFACTURA_DOMICILIO_FISCAL` | Domicilio fiscal |
| `URUFACTURA_CERT_B64` | Certificado `.p12` codificado en Base64 |
| `URUFACTURA_CERT_PASSWORD` | Contraseña del certificado |
| `URUFACTURA_CAES` | JSON array de CAEs (opcional) |

### Configuración del contenedor

El contenedor lee configuración exclusivamente de variables de entorno (inyectadas como Cloudflare Secrets o `[vars]` en `wrangler.toml`). El proveedor de configuración de .NET traduce `__` a `:` automáticamente.

**Restricción de tenant IDs:** no pueden contener `:` ni `__` (separadores del proveedor de configuración .NET).

---

## Capacidades

### Tipos de CFE soportados (13 de 13)

| Código | Tipo | Operaciones |
|-------:|------|-------------|
| 101 | e-Ticket | XML, Enviar, PDF A4/Térmico, Consultar |
| 102 | Nota Crédito e-Ticket | XML, Enviar, PDF A4/Térmico, Consultar |
| 103 | Nota Débito e-Ticket | XML, Enviar, PDF A4/Térmico, Consultar |
| 111 | e-Factura | XML, Enviar, PDF A4/Térmico, Consultar |
| 112 | Nota Crédito e-Factura | XML, Enviar, PDF A4/Térmico, Consultar |
| 113 | Nota Débito e-Factura | XML, Enviar, PDF A4/Térmico, Consultar |
| 121 | e-Factura Exportación | XML, Enviar, PDF A4/Térmico, Consultar |
| 122 | NC e-Factura Exportación | XML, Enviar, PDF A4/Térmico, Consultar |
| 123 | ND e-Factura Exportación | XML, Enviar, PDF A4/Térmico, Consultar |
| 131 | e-Remito Despachante | XML, Enviar, PDF A4/Térmico, Consultar |
| 151 | e-Resguardo | XML, Enviar, PDF A4/Térmico, Consultar |
| 181 | e-Remito | XML, Enviar, PDF A4/Térmico, Consultar |
| 182 | Nota Crédito e-Remito | XML, Enviar, PDF A4/Térmico, Consultar |

### Operaciones de la API

| Operación | Endpoint | Descripción |
|-----------|----------|-------------|
| Generar XML firmado | `POST /cfe/xml` | Genera CFE en XML con firma XMLDSIG |
| Emitir CFE | `POST /cfe/enviar` | Firma y envía a DGI via SOAP |
| PDF A4 | `POST /cfe/pdf/a4` | Representación impresa formato A4 |
| PDF Térmico | `POST /cfe/pdf/termico` | Representación impresa 80mm |
| Consultar estado | `POST /cfe/consultar` | Consulta estado en DGI |
| Reporte Diario | `POST /reporte-diario` | Envía reporte diario obligatorio |
| Listar CAEs | `GET /cae` | CAEs activos en memoria |
| Registrar CAE | `POST /cae` | Agrega CAE en runtime |
| Advertencias CAE | `GET /cae/advertencias` | CAEs por vencer / alto uso |
| Health check | `GET /health` | Liveness probe |

### Atajos tipados

Además de los endpoints genéricos (que requieren `Tipo` en el body), existen atajos para los tipos más comunes:

- `/cfe/eticket/{xml,enviar,pdf/a4,pdf/termico}` — e-Ticket (101)
- `/cfe/efactura/{xml,enviar,pdf/a4,pdf/termico}` — e-Factura (111)

---

## Seguridad

### Capas de seguridad

```mermaid
graph TD
    subgraph "Capa 1: Cloudflare Edge"
        TLS["TLS 1.3 (terminación)"]
        WAF["WAF / Rate Limiting<br/>(configurable)"]
        AUTH_W["Autenticación Worker<br/>(API Key / JWT / mTLS)"]
    end

    subgraph "Capa 2: Contenedor .NET"
        TENANT["Aislamiento por tenant<br/>(contenedor separado)"]
        CERT_PERM["Certificado con permisos 0600"]
        SOAP_SEC["SOAP builders seguros<br/>(XmlDocument, no interpolation)"]
    end

    subgraph "Capa 3: DGI"
        MTLS["mTLS con certificado X.509"]
    end

    TLS --> WAF --> AUTH_W --> TENANT --> CERT_PERM --> SOAP_SEC --> MTLS
```

### Consideraciones de seguridad

| Aspecto | Implementación |
|---------|---------------|
| **Autenticación de clientes** | Debe implementarse en el Worker (`worker.js`). La API .NET no valida callers — el Worker es la capa de confianza. |
| **Aislamiento multi-tenant** | Cada tenant corre en contenedor separado con memoria aislada. No hay acceso cruzado. |
| **Certificados digitales** | Almacenados como Cloudflare Secrets. Escritos temporalmente en disco con permisos `0600` (solo owner). Eliminados al disponer el servicio. |
| **Prevención XSS/Injection** | SOAP builders usan `XmlDocument`/`XmlWriter` con auto-escape. No hay interpolación de strings en XML. |
| **Secretos en CI** | Pasados con `printf '%s'` (sin trailing newline). Variables sensibles nunca se imprimen en logs. |
| **TLS a DGI** | Certificado cliente X.509 sobre HTTPS. Validación SSL habilitada por defecto (solo deshabitable explícitamente en homologación). |

> ⚠️ **Importante para multi-tenant:** el `worker.js` **debe** validar que el caller tiene permiso para usar el `X-Tenant-Id` que envía. Sin autenticación en el Worker, cualquier cliente podría operar con credenciales de otro tenant.

---

## Escalabilidad y límites

| Parámetro | Valor | Configurable |
|-----------|-------|:------------:|
| `max_instances` | 10 (default en `wrangler.toml`) | ✅ |
| Tenants concurrentes | Limitado por `max_instances` | ✅ |
| Sleep timeout | 5 min de inactividad | ✅ (`sleepAfter` en `worker.js`) |
| Wake time | ~2-5s (cold start contenedor) | – |
| Imagen Docker | ~80-120 MB (Alpine) | – |
| CAEs en memoria | Sin límite práctico por tenant | – |

### Comportamiento bajo carga

1. **Tenant activo** → respuesta inmediata (contenedor despierto).
2. **Tenant dormido** → cold start (~2-5s) → respuesta.
3. **Más tenants que `max_instances`** → el tenant menos reciente se detiene (pierde CAEs de runtime, no los de config). Se recrea al siguiente request.

### Recomendación de sizing

| Escenario | `max_instances` recomendado |
|-----------|:---------------------------:|
| Single-tenant | 1 |
| 2-5 tenants con actividad regular | 5-10 |
| 10+ tenants con picos desiguales | ≥ número de tenants con actividad simultánea esperada |

---

## Ciclo de vida del contenedor

```mermaid
stateDiagram-v2
    [*] --> Stopped: deploy
    Stopped --> Starting: first request
    Starting --> Running: container ready
    Running --> Sleeping: 5 min inactivity
    Sleeping --> Running: new request
    Running --> Stopped: eviction (max_instances)
    Sleeping --> Stopped: eviction

    note right of Starting
        - Pull image from GHCR
        - Start .NET process
        - TenantClientFactory loads config
        - Seed CAEs from env vars
    end note

    note right of Stopped
        - CAEs de runtime perdidos
        - CAEs de config se recargan
          al próximo start
    end note
```

### Implicaciones

- **Datos persistentes:** los CAEs registrados via `POST /cae` viven solo en memoria. Se pierden si el contenedor se detiene/reinicia.
- **Datos de configuración:** los CAEs en `UruFactura__Caes` se recargan en cada inicio del contenedor.
- **Recomendación:** use siempre `UruFactura__Caes` (Cloudflare Secret) como fuente de verdad. Use `POST /cae` solo para actualizaciones temporales.

---

## Gestión de estado

| Tipo de estado | Almacenamiento | Persistencia | Alcance |
|----------------|----------------|:------------:|---------|
| Configuración empresa | Cloudflare Secrets / `[vars]` | ✅ Permanente | Global |
| CAEs (semilla) | Cloudflare Secret (`__Caes`) | ✅ Permanente | Por tenant (env var) |
| CAEs (runtime) | Memoria del contenedor | ❌ Volátil | Por contenedor |
| Certificado digital | Secret → archivo temporal | ⚠️ Per-start | Por contenedor |
| Sesiones / tokens | No aplica | – | – |

> La API es **stateless** en diseño. El único estado mutable es el registro de CAEs en memoria, que se regenera desde configuración en cada cold start.

---

## Pipeline CI/CD

### Workflows

| Workflow | Trigger | Función |
|----------|---------|---------|
| `test-cloudflare-api.yml` | Push / PR | Build + test (172 tests) + smoke test Docker |
| `docker.yml` | Push a main / tags | Build y push imagen a GHCR |
| `deploy-cloudflare.yml` | Tag `v*` / manual | Build → push GHCR → secrets → deploy Worker + Container |
| `deploy-admin.yml` | Push a `admin/` en main / manual | Build admin SPA → deploy a Cloudflare Pages |
| `deploy-email-worker.yml` | Push a `cloudflare/email-worker/` en main / manual | Deploy email worker a Cloudflare Workers |

### Flujo de despliegue completo

```mermaid
sequenceDiagram
    participant Dev as Desarrollador
    participant GH as GitHub
    participant GHA as GitHub Actions
    participant GHCR as Container Registry
    participant CF as Cloudflare

    Dev->>GH: git push tag v1.2.3
    GH->>GHA: trigger deploy-cloudflare.yml

    rect rgb(36, 41, 46)
        Note over GHA: Job: build
        GHA->>GHA: docker build (multi-stage)
        GHA->>GHCR: docker push (sha-abc1234, v1.2.3, latest)
        GHA->>GHA: output sha_tag
    end

    rect rgb(246, 130, 31)
        Note over GHA: Job: deploy
        GHA->>GHA: sed wrangler.toml (image = sha_tag)
        GHA->>CF: wrangler secret put (×5 secrets)
        GHA->>CF: wrangler deploy
    end

    CF->>GHCR: pull image (on next request)
    Note over CF: Container ready to serve
```

### Ambientes

| Trigger | Ambiente por defecto |
|---------|---------------------|
| `workflow_dispatch` (manual) | Selección explícita (Homologacion / Produccion) |
| Tag push `v*` | **Homologacion** (seguro por defecto) |

Para desplegar a Producción, use `workflow_dispatch` y seleccione `Produccion` explícitamente.

---

## Decisiones de diseño

| Decisión | Justificación |
|----------|---------------|
| **Cloudflare Containers + Durable Objects** | Aislamiento por tenant sin orquestar Kubernetes. Sleep automático reduce costo. |
| **ASP.NET Core Minimal API** | Footprint mínimo, startup rápido, ideal para contenedores con cold start. |
| **Sin autenticación en el contenedor** | El Worker es el punto de entrada público; autenticar allí evita duplicar lógica y simplifica el .NET. |
| **CAEs en memoria (no DB)** | Diseño simple para contenedores efímeros. La configuración como fuente de verdad cubre el caso de restart. |
| **Certificado como variable de entorno (Base64)** | Cloudflare Containers no soporta volúmenes persistentes. El `.p12` se decodifica al inicio. |
| **Sin Scalar / Swagger UI** | Imagen mínima para producción. La documentación OpenAPI está disponible en desarrollo (`/openapi/v1.json`). |
| **`TenantClientFactory` singleton con `ConcurrentDictionary`** | Un solo cliente por tenant, creación lazy, thread-safe, con dispose atómico. |
| **SOAP builders con `XmlDocument`** | Previene XSS/injection (CodeQL). Los valores de usuario fluyen por `InnerText` (auto-escapado). |
| **`printf '%s'` en CI** | `echo` agrega `\n` que corrompe Base64 y passwords al inyectarlos como secretos. |
| **Default a Homologacion en tag push** | Previene despliegues accidentales a producción. Producción requiere acción manual explícita. |

---

## Admin Web (Cloudflare Pages)

La web de administración de tenants se despliega como **Cloudflare Pages** (SPA React + Vite) con **Pages Functions** como backend de autenticación.

### Componentes

| Componente | Ubicación | Función |
|------------|-----------|---------|
| SPA (React + Vite) | `admin/src/` | UI de administración |
| Pages Functions | `admin/functions/auth/` | Backend de autenticación (KV) |
| KV: AUTH_CODES | Cloudflare KV | Códigos temporales (TTL 10 min) |
| KV: AUTH_SESSIONS | Cloudflare KV | Tokens de sesión JWT (TTL 24h) |
| KV: TENANTS | Cloudflare KV | Registro de tenants |

### Flujo de autenticación

```mermaid
sequenceDiagram
    participant U as Usuario
    participant SPA as Admin SPA
    participant PF as Pages Functions
    participant KV as Cloudflare KV
    participant EM as Email (MailChannels/Worker)

    U->>SPA: Ingresa email
    SPA->>PF: POST /auth/send-code
    PF->>KV: Guardar código (10 min TTL)
    PF->>EM: Enviar email con código
    EM-->>U: Email con código 6 dígitos
    U->>SPA: Ingresa código
    SPA->>PF: POST /auth/verify-code
    PF->>KV: Validar código
    PF->>KV: Crear sesión (24h TTL)
    PF-->>SPA: JWT token
    SPA->>SPA: Almacenar token (localStorage)
```

### Endpoints Auth (Pages Functions)

| Endpoint | Método | Descripción |
|----------|--------|-------------|
| `/auth/send-code` | POST | Enviar código de verificación |
| `/auth/verify-code` | POST | Verificar código → obtener JWT |
| `/auth/register` | POST | Registrar tenant (requiere email verificado) |
| `/auth/session` | GET | Info de sesión actual |
| `/auth/tenants` | GET | Listar tenants del usuario |

### Despliegue

- **Automático**: GitHub Actions (`.github/workflows/deploy-admin.yml`) en push a `main` que toque `admin/`
- **Manual**: `npx wrangler pages deploy dist --project-name urufactura-admin`
- **Secrets**: `JWT_SECRET`, `EMAIL_API_KEY` (via Cloudflare Pages dashboard o CLI)

---

## Email Worker

Worker dedicado al envío de emails transaccionales (`cloudflare/email-worker/`).

### Características

| Aspecto | Detalle |
|---------|---------|
| **Rol** | Envío de emails (códigos de verificación, notificaciones) |
| **Proveedores** | MailChannels (gratis, SPF requerido) o API custom (Resend, SendGrid) |
| **Integración** | Service binding desde Pages Functions (`EMAIL_WORKER`) o invocación HTTP directa |
| **Configuración** | `MAIL_FROM`, `APP_NAME`, `ALLOWED_ORIGINS`, `EMAIL_API_URL` (opcional), `EMAIL_API_KEY` (secret) |

### Despliegue

- **Automático**: GitHub Actions (`.github/workflows/deploy-email-worker.yml`) en push a `main` que toque `cloudflare/email-worker/`
- **Manual**: `cd cloudflare/email-worker && wrangler deploy`

### Requisito DNS (MailChannels)

Para enviar emails con MailChannels (gratis), agregar registro TXT:

```
_mailchannels  TXT  "v=mc1 cfid=urufactura-admin.pages.dev"
```
