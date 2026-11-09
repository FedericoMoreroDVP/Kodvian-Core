# Operación con asistente

## Base para trabajar

Un asistente con OpenCode necesita acceso al checkout local, sin leer archivos `.env` ni secretos, y las herramientas del proyecto: .NET SDK 8, Node.js 20+, npm 10+ y PostgreSQL 15+ para ejecución local.

Estructura relevante:

- `backend/`: API .NET 8 por capas; `src/` contiene Api, Application, Domain e Infrastructure, y `tests/Kodvian.Core.Application.Tests/` los tests.
- `frontend/`: Angular standalone con Angular Material; código en `src/app/`.
- `docs/`: documentación operativa.
- `Dockerfile` y `docker-entrypoint.sh`: imagen integrada de frontend y API.
- `railway.toml`: configuración de build y healthcheck de Railway.

Comandos habituales:

```bash
# desde backend/
dotnet restore
dotnet build Kodvian.Core.slnx
dotnet test Kodvian.Core.slnx
dotnet run --project src/Kodvian.Core.Api

# desde frontend/
npm ci
npm run build
npm test
npm start
```

Variables runtime de Production identificadas por nombre, sin valores:

- Runtime: `PORT`, `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, `DATABASE_URL`.
- JWT: `Jwt__Issuer`, `Jwt__Audience`, `Jwt__Key`, `Jwt__ExpirationMinutes`.
- Seed administrativo: `AuthSeed__AdminEmail`, `AuthSeed__AdminPassword`, `AuthSeed__AdminFullName`.
- CORS: `Cors__AllowedOrigins`.
- Storage S3: `Storage__Provider`, `Storage__Bucket`, `Storage__AccessKey`, `Storage__SecretKey`, `Storage__ServiceUrl`, `Storage__Region`, `Storage__ForcePathStyle`, `Storage__MaxPdfSizeMb`.
- Cifrado de credenciales IA: `AiEncryption__CertificateBase64`, `AiEncryption__CertificatePassword`.

En Railway el servicio se construye con el `Dockerfile` multi-stage: Angular se compila, la API .NET se publica y sirve el SPA desde `wwwroot`. `railway.toml` indica builder `DOCKERFILE`, healthcheck `/healthz` y reinicio ante fallos. El entrypoint escucha en `PORT`. El README indica un servicio único y un plugin PostgreSQL que aporta `DATABASE_URL`. No hay `railway.json` en el repositorio.

## Checklist antes de deploy

- Confirmar el proyecto, servicio API y ambiente correctos en Railway antes de editar nada.
- Cargar los nombres exactos de variables anteriores: las secciones .NET usan doble guion bajo (`__`), no dos puntos.
- Usar `ASPNETCORE_ENVIRONMENT=Production`, el `DATABASE_URL` del PostgreSQL adjunto y storage `S3` con todos sus datos requeridos.
- Verificar `Jwt__Key` con al menos 32 caracteres y sin placeholder; revisar comillas, espacios y caracteres especiales en passwords, especialmente `AuthSeed__AdminPassword` y `AiEncryption__CertificatePassword`.
- Proveer `AiEncryption__CertificateBase64` como PFX en Base64 con clave privada y su contraseña correspondiente. Nunca incorporar ambos valores al repositorio, Dockerfile o logs.
- Tras cambiar una variable, ejecutar un redeploy: el proceso las lee al arrancar.
- Antes de rotar el certificado, conservar de forma segura el PFX y la contraseña anteriores. No reemplazarlo como rotación rutinaria: puede impedir descifrar credenciales IA existentes.
- Tratar el reset de `AuthSeed__AdminPassword` como una operación aparte: cambiar la variable no equivale a resetear una cuenta administrativa ya creada.
- Después del deploy autorizado, comprobar `/healthz`, `/api/health`, login, SPA y carga de archivos S3.

## Diagnóstico de inicio: AiEncryption

Buscar el mensaje exacto en los logs del arranque, sin copiar ni exponer variables:

- `AiEncryption:CertificateBase64 is required outside Development.`: falta `AiEncryption__CertificateBase64` en el ambiente que inicia.
- `AiEncryption:CertificatePassword is required when AiEncryption:CertificateBase64 is configured.`: falta o quedó vacía `AiEncryption__CertificatePassword`.
- `AiEncryption:CertificateBase64 is not valid Base64.`: el contenido no es Base64 válido; volver a provisionar el PFX codificado, sin modificarlo.
- `AiEncryption:CertificateBase64 could not be opened with AiEncryption:CertificatePassword.`: PFX y contraseña no corresponden, o la contraseña fue alterada por el formulario/configuración.
- `AiEncryption:CertificateBase64 must contain a PFX certificate with a private key.`: se cargó un certificado público u otro formato, no un PFX con clave privada.

Corregir la variable en el servicio y ambiente correctos, conservar el certificado anterior y redeployar. Si hay credenciales IA ya almacenadas, no rotar ni sustituir el certificado sin el procedimiento de migración y recuperación.

## Decisiones de Nicolás

Siempre requieren decisión explícita de Nicolás:

- Hacer `git push`.
- Entregar, visualizar o modificar credenciales, secretos y archivos `.env`.
- Cambiar configuración, variables, servicio o ambiente de producción.
- Desplegar, redeployar, vincular Railway o tocar sus recursos.

Propuesta para habilitar deploys en el futuro, sin aplicarla: Nicolás debería autorizar por escrito cada deploy indicando repositorio, commit, servicio y ambiente destino; conceder acceso limitado al servicio designado; confirmar que las variables y PostgreSQL ya están configurados; y aprobar las verificaciones post-deploy. El asistente mantendría prohibido leer o mostrar secretos y no haría push implícito.

## Resumen

Este documento reúne la estructura y comandos del repositorio, variables de Production por nombre, flujo Railway basado en los archivos versionados, checklist previo, diagnóstico de `AiEncryption` y límites de autorización para el asistente.
