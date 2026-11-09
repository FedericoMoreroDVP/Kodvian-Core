# Railway readiness — Kodvian Core

Checklist de pendientes e implementación para desplegar Kodvian Core en Railway.

**Arquitectura acordada**

- 1 servicio: la API .NET sirve el SPA Angular desde `wwwroot` (mismo origen).
- Archivos: object storage S3-compatible en Production; local en Development.
- Build: Dockerfile multi-stage en la raíz del repo.

## Estado

| # | Ítem | Estado | Notas |
|---|------|--------|-------|
| 1 | Empaquetado / Dockerfile / `.dockerignore` / `railway.toml` | Hecho | Multi-stage Node + .NET; entrypoint respeta `PORT` |
| 2 | SPA servido por la API (static + fallback) | Hecho | `wwwroot` + `MapFallbackToFile` |
| 3 | Proxy, HTTPS, CSP y cookies de auth | Hecho | Forwarded headers, sin HTTPS redirect en prod, CSP SPA |
| 4 | Storage S3-compatible | Hecho | `Provider=Local\|S3`, fail-fast al startup |
| 5 | Variables de entorno documentadas | Hecho | README + esta sección |
| 6 | Postgres + migraciones/seed | Listo (código) | Adjuntar plugin en Railway al deployar |
| 7 | Verificaciones packaging | Hecho (local) | Imagen `kodvian-core:local` con SPA en `/app/wwwroot` |

## 1. Empaquetado

- [x] `Dockerfile` multi-stage (Node 20 → Angular build → .NET publish → aspnet runtime)
- [x] `.dockerignore` (excluir `node_modules`, `bin`, `obj`, `.git`, etc.)
- [x] `railway.toml` con builder Dockerfile y healthcheck `/healthz`
- [x] `docker-entrypoint.sh` setea `ASPNETCORE_URLS` con `PORT`
- Artefacto Angular: `frontend/dist/frontend/browser` → `/app/wwwroot`

## 2. SPA servido por la API

- [x] `UseDefaultFiles` + `UseStaticFiles`
- [x] `MapFallbackToFile("index.html")` después de controllers y `/healthz`
- Los clients Angular ya usan paths relativos `/api/...` (sin cambios)

## 3. Proxy, HTTPS, CSP y cookies

- [x] `ForwardedHeaders` con `KnownNetworks` / `KnownProxies` vacíos
- [x] Production: no `UseHttpsRedirection` (TLS en Railway)
- [x] CSP compatible con SPA + Google Fonts / Material Icons
- [x] Cookie `auth_token` con `Secure=true` fuera de Development

## 4. Storage S3-compatible

- [x] Extender `StorageOptions` (`Provider`, bucket, creds, `ServiceUrl`, `Region`, `ForcePathStyle`)
- [x] `S3FileStorageService` + DI según `Storage:Provider`
- [x] Fail-fast al startup si `Provider=S3` y faltan settings
- [x] Development: `Local` + `App_Data/files`

## 5. Variables de entorno

### Runtime

| Variable | Requerida | Descripción |
|----------|-----------|-------------|
| `ASPNETCORE_ENVIRONMENT` | Sí | `Production` |
| `ASPNETCORE_URLS` | Sí | `http://0.0.0.0:${PORT}` (el entrypoint también usa `PORT`) |
| `DATABASE_URL` | Sí | Connection string del plugin Postgres Railway |

### Auth / JWT

| Variable | Requerida | Descripción |
|----------|-----------|-------------|
| `Jwt__Issuer` | Sí | Emisor JWT |
| `Jwt__Audience` | Sí | Audiencia JWT |
| `Jwt__Key` | Sí | ≥32 caracteres; no usar placeholder `SET_` |
| `Jwt__ExpirationMinutes` | No | Default 30 |
| `AuthSeed__AdminEmail` | Sí (prod) | Email admin inicial |
| `AuthSeed__AdminPassword` | Sí (prod) | Password seguro (no `Admin123*`) |
| `AuthSeed__AdminFullName` | Sí (prod) | Nombre del admin |

### CORS (opcional con mismo origen)

| Variable | Requerida | Descripción |
|----------|-----------|-------------|
| `Cors__AllowedOrigins` | No | Orígenes separados por coma |

### Storage

| Variable | Requerida | Descripción |
|----------|-----------|-------------|
| `Storage__Provider` | Sí (prod) | `S3` en Railway |
| `Storage__Bucket` | Si S3 | Nombre del bucket |
| `Storage__AccessKey` | Si S3 | Access key |
| `Storage__SecretKey` | Si S3 | Secret key |
| `Storage__ServiceUrl` | Si S3 | Endpoint (R2/MinIO/S3) |
| `Storage__Region` | Si S3 | Región (ej. `auto` en R2) |
| `Storage__ForcePathStyle` | Recomendado | `true` para R2/MinIO |
| `Storage__MaxPdfSizeMb` | No | Default 10 |

### Protección de credenciales del proveedor IA

La migración de configuración IA requiere estas variables runtime en el servicio API antes del deploy en Production:

| Variable | Requerida | Descripción |
|----------|-----------|-------------|
| `AiEncryption__CertificateBase64` | Sí | Certificado PFX con clave privada, codificado en Base64; protege el anillo Data Protection persistido en PostgreSQL. |
| `AiEncryption__CertificatePassword` | Sí | Contraseña del PFX. |

Provisionar ambos valores en Railway, nunca en Dockerfile, código o repositorio. Proteger también un respaldo seguro del PFX y contraseña: sin ellos no se pueden descifrar las claves API almacenadas. Cambiar el certificado requiere una rotación coordinada; ver [configuración IA](backend/ai-provider-configuration.md).

## 6. Postgres + migraciones / seed

- [x] Parseo de `DATABASE_URL` en Infrastructure
- [x] `MigrateAsync()` al arrancar
- [x] `AuthSeed` obligatorio fuera de Development
- [ ] En Railway: adjuntar plugin Postgres (inyecta `DATABASE_URL`) — **paso manual al deployar**
- [ ] Setear `AuthSeed__*` y `Jwt__*` **antes** del primer deploy — **paso manual**
- Nota: el admin por defecto de Development (`admin@kodvian.local`) **no** aplica en Production

## 7. Verificaciones

### Packaging local (hecho)

- [x] `docker build -t kodvian-core:local .` exitoso
- [x] Imagen contiene `/app/wwwroot/index.html` y `Kodvian.Core.Api.dll`
- [x] Fail-fast de JWT en Production (placeholder / key corta)

### Post-deploy en Railway (manual)

- [ ] `GET /` → SPA (index.html)
- [ ] `GET /healthz` → 200
- [ ] `GET /api/health` → success
- [ ] Login con el admin de `AuthSeed`
- [ ] Navegación de módulos principales
- [ ] Upload de PDF (documento/recibo) contra S3

## Fuera de alcance (después)

- CI/CD GitHub → Railway
- Custom domain / CDN detallado
- Migración de archivos ya existentes en disco local → S3
- Healthcheck profundo (Postgres/S3)
- Backups de bucket / multi-región
- Tests automatizados específicos de `S3FileStorageService`
