# Storage

Kodvian Core soporta almacenamiento local en desarrollo y almacenamiento S3-compatible para produccion.

## Archivos principales

- `backend/src/Kodvian.Core.Application/Common/Files/IFileStorageService.cs`.
- `backend/src/Kodvian.Core.Infrastructure/Services/LocalFileStorageService.cs`.
- `backend/src/Kodvian.Core.Infrastructure/Services/S3FileStorageService.cs`.
- `backend/src/Kodvian.Core.Infrastructure/Storage/StorageOptions.cs`.
- `backend/src/Kodvian.Core.Infrastructure/Extensions/DependencyInjectionExtensions.cs`.

## Providers

### Local

Usado en Development.

Guarda archivos en disco local, por defecto bajo `App_Data/files`.

No usar como storage productivo en contenedores, porque el filesystem puede ser efimero.

### S3

Usado para Production.

Compatible con S3, Cloudflare R2 o MinIO segun configuracion.

Variables relevantes:

- `Storage__Provider=S3`.
- `Storage__Bucket`.
- `Storage__AccessKey`.
- `Storage__SecretKey`.
- `Storage__ServiceUrl`.
- `Storage__Region`.
- `Storage__ForcePathStyle`.
- `Storage__MaxPdfSizeMb`.

## Adjuntos de tareas

Las evidencias de tareas aceptan PNG, JPG, WebP, PDF, Word, Excel, TXT, CSV y ZIP,
con un límite de 10 MB por archivo. El servidor valida extensión, tamaño y firma del
contenido para imágenes y PDF. Las imágenes muestran miniatura y vista ampliada antes
de guardar y después de subir; los reintentos conservan el mismo identificador para no
duplicar archivos.

## Casos de uso

- Documentos de proyecto.
- Versiones de documentos de proyecto.
- Comprobantes de movimientos financieros.
- Comprobantes de pagos a desarrolladores.
- Adjuntos y evidencias de tareas.

## Modelo relacionado

`DocumentFile` guarda metadata del archivo:

- Nombre original.
- Nombre almacenado.
- Content type.
- Storage path.
- SHA-256.
- Usuario que subio el archivo.
- Owner funcional.

## Cuidados

- No confiar solo en extension de archivo.
- No exponer paths internos de storage al frontend.
- Para produccion, configurar S3 antes del primer deploy.
- Eliminar archivo fisico/remoto cuando el flujo realmente borre comprobantes.
- Diferenciar baja logica de documentos de proyecto vs eliminacion de comprobantes.
- Si una subida devuelve 503, el almacenamiento no estuvo disponible: reintentar y
  revisar el log del POST de adjuntos. En S3 verificar bucket, credenciales, endpoint
  y permiso PutObject; en local, permisos y espacio de `App_Data/files`.
