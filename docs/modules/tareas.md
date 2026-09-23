# Tareas

## Resumen funcional

El modulo de tareas organiza el trabajo operativo asociado a proyectos. Permite listar, filtrar, ver detalle, crear, editar, cambiar estado y visualizar tareas en kanban.

## Flujos principales

- Listar tareas.
- Filtrar por estado, prioridad, proyecto, responsable u otros criterios disponibles.
- Crear tarea.
- Editar tarea.
- Cambiar estado.
- Eliminar permanentemente tareas canceladas y sus evidencias.
- Consultar detalle.
- Visualizar kanban (vista inicial).
- Mantener presionada una tarjeta y arrastrarla a otra columna para cambiar su estado.
- Adjuntar evidencias mediante selección, arrastre de archivos o pegado de capturas.

## Pantallas frontend

- Ruta: `/tareas`.
- Page: `frontend/src/app/modules/tareas/tareas-page.component.ts|html|scss`.
- Service: `frontend/src/app/modules/tareas/services/tareas.service.ts`.
- Models: `frontend/src/app/modules/tareas/models/tareas.models.ts`.
- Dialogs: `tarea-form-dialog`, `tarea-detail-dialog`, `tarea-status-dialog`.

## Contratos backend

- `GET /api/tasks`.
- `GET /api/tasks/{id}`.
- `GET /api/tasks/kanban`.
- `GET /api/tasks/lookups`.
- `POST /api/tasks`.
- `PUT /api/tasks/{id}`.
- `PATCH /api/tasks/{id}/status`.
- `DELETE /api/tasks/{id}` (sólo tareas `Cancelada`).
- `GET /api/tasks/{id}/attachments`.
- `POST /api/tasks/{id}/attachments` (`multipart/form-data`: `file`, `uploadId` UUID estable por archivo/reintento).
- `GET /api/tasks/{id}/attachments/{attachmentId}` (descarga autenticada).
- `DELETE /api/tasks/{id}/attachments/{attachmentId}`.

Archivos:

- Controller: `TasksController.cs`.
- Service: `TaskService.cs`.
- Abstraction: `ITaskService.cs`.
- DTOs/requests: `backend/src/Kodvian.Core.Application/Tasks/**`.

## Modelo de datos

- Entity: `TaskItem`.
- Relaciones: proyecto requerido, desarrollador opcional, responsable opcional, creador requerido.
- Enums: `TaskStatus`, `TaskPriority`.

Estados:

- `Pendiente`.
- `EnCurso`.
- `Bloqueada`.
- `Finalizada`.
- `Cancelada`.

Prioridades:

- `Baja`.
- `Media`.
- `Alta`.
- `Urgente`.

## Permisos

- `tasks.read` para consulta.
- `tasks.write` para alta, edicion, cambio de estado y eliminación permanente de tareas canceladas.
- El controller de tareas aplica policies de lectura/escritura.
- Adjuntos: lectura general con `tasks.read`, gestión general con `tasks.read` y `tasks.write`.
- Desde Mi trabajo: `developer.work.read` permite consultar adjuntos de tareas activas asignadas al desarrollador del token; `developer.tasks.status.write` permite subir y eliminar archivos propios en esas tareas.
- La pertenencia a la tarea y la autoría se verifican en servidor en cada operación; los identificadores de desarrollador/autor no los elige el cliente.

## Estados de UI

- Loading de listado/kanban.
- Empty state.
- Error con snackbar.
- Dialog de detalle.
- Dialog de cambio de estado.

## Reglas de negocio

- Toda tarea pertenece a un proyecto.
- Estado y prioridad deben expresarse con etiquetas claras.
- Las horas estimadas/reales usan precision decimal.
- Kanban depende de estado y orden.
- Una tarea sólo se puede eliminar permanentemente cuando su estado es `Cancelada`. La operación borra sus metadatos y sus binarios adjuntos; no se puede deshacer.

## Tablero y guardado

- La vista inicial siempre es tablero. Se conserva la opción de lista.
- Las tarjetas ofrecen Ver detalle y Editar; el formulario permite cambiar el estado con teclado.
- Las tarjetas y filas canceladas muestran Eliminar para usuarios con permisos de escritura. La confirmación advierte que se borrarán la tarea y sus adjuntos.
- Arrastre inmediato con mouse manteniendo el botón presionado, sin espera inicial; pulsación sostenida de 300 ms en pantallas táctiles. Los botones de la tarjeta no inician arrastre y el texto no se selecciona durante el gesto.
- Soltar sobre otra columna, incluso vacía, persiste el estado usando el PATCH existente y conserva `kanbanOrder`.
- Soltar fuera o dentro de la misma columna no escribe ni reordena.
- Durante la escritura se bloquean nuevos movimientos; si falla se restaura la posición original.
- El formulario guarda sin cerrarse previamente. Un error conserva los datos ingresados.

## Adjuntos y evidencias

- Componente compartido `tarea-attachments`, disponible en edición y detalle, incluido el detalle de Mi trabajo.
- Formatos: PNG/JPG/JPEG/WebP, PDF, DOC/DOCX, XLS/XLSX, TXT/CSV y ZIP. Máximo 10 MiB por archivo (mostrado como 10 MB en la UI).
- La API valida extensión, tamaño y firma básica del formato; determina el tipo de contenido sin confiar en el MIME enviado.
- Imágenes con miniatura y ampliación antes de guardar, durante un reintento y después de subir; todos los archivos se pueden descargar. Se muestra autor, fecha y tamaño.
- Al crear/editar, los archivos quedan en cola hasta guardar. Se guarda primero la tarea y luego se suben secuencialmente.
- Si falla un archivo, se conserva en cola y puede reintentarse individualmente; no se vuelve a crear la tarea.
- Si el almacenamiento no está disponible, la API responde 503 con una indicación segura para reintentar. El detalle técnico queda únicamente en el log del servidor; la captura y su preview local se conservan hasta quitarla o subirla correctamente.
- `UploadId` tiene índice único por tarea y permite repetir una subida cuya respuesta se perdió sin duplicar el archivo.
- Los binarios usan `IFileStorageService` (local o S3 según configuración); PostgreSQL guarda solo metadatos en `TaskAttachments`.
- El borrado conserva un registro marcado como eliminado y la ruta para permitir reintentos si falla el almacenamiento. Si el usuario cerró el diálogo tras ese fallo, la eliminación física puede reintentarse con el mismo endpoint/ID.
- Una subida fallida intenta limpiar el archivo. Si no se puede determinar si hubo commit, se conserva y registra la ruta para reconciliación, evitando borrar archivos referenciados.

### Migración

`20260916183020_TaskAttachments` crea exclusivamente tabla, relaciones e índices de adjuntos.
El snapshot también incorpora la definición omitida de `ProjectDeveloperAssignment`, ya creada por una migración anterior. No se recrea esa tabla.
`KodvianDbContextFactory` permite generar/verificar migraciones sin ejecutar startup, migraciones automáticas ni seeding de la API.

### Verificación de esta mejora

- Backend: pruebas con EF InMemory y almacenamiento simulado para permisos, tareas ajenas/inactivas/reasignadas, autoría, descarga, reintentos idempotentes, límites y fallos de persistencia/almacenamiento. No sustituyen una prueba contra PostgreSQL/S3.
- Frontend: pruebas en ChromeHeadless de movimiento/rollback, permisos, selección/pegado/arrastre de archivos, reintentos y conservación del formulario.
- Antes del despliegue, probar con almacenamiento real la subida/descarga y con dispositivo táctil el gesto sostenido.

## Riesgos y cuidados

- Cuidar transiciones de estado invalidas si se agregan reglas.
- Evitar cambiar orden kanban sin persistencia consistente.
- No mostrar campos tecnicos al usuario final.

## Tests requeridos

- Backend: tests de `TaskService` para listado, detalle, kanban, lookups, alta, edicion y cambio de estado.
- Backend: tests de validacion para proyecto requerido, prioridad, estado, horas y fechas.
- Backend: tests de transiciones de estado cuando se agreguen reglas.
- Backend: integration tests de `GET`, `POST`, `PUT`, `PATCH /api/tasks/{id}/status` y `GET /api/tasks/kanban`.
- Frontend: specs de `TareasService` para rutas, filtros, params y payloads.
- Frontend: specs de pagina para listado, kanban, filtros, loading, empty, error y acciones.
- Frontend: specs de dialogs de form, detail y status para validacion, submit y cierre.

## Mejoras futuras

- Reordenamiento manual dentro de una columna con persistencia de orden.
- Historial de cambios.
- Comentarios o actividad por tarea.
