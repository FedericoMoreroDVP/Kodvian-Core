# Oficina Inteligente

## Alcance

Conversaciones privadas para Administradores con consultas L0 de lectura y acciones de escritura con confirmación en dos pasos. Las herramientas disponibles se filtran por permiso de dominio; la IA no tiene acceso directo a SQL, archivos ni red arbitraria. La IA nunca escribe por sí sola: las acciones quedan como propuesta hasta que el usuario las confirma con un botón.

El prompt de sistema se arma en cada ejecución (`AiSystemPromptBuilder`) con la fecha, la hora y la zona `America/Argentina/Buenos_Aires`, el nombre del usuario y las capacidades habilitadas. Responde en español rioplatense.
## Configuración y aprobación de privacidad

Desde Administración → Configurar proveedor de IA, un Administrador elige el proveedor (OpenAI o Anthropic), el modelo y la clave API. La clave se cifra en backend y nunca se devuelve al frontend. Con OpenAI la prueba de conexión envía una solicitud mínima con `store: false` y puede generar un costo pequeño; con Anthropic consulta el modelo en la Models API, sin costo. Paso a paso: [guía de conexión](../guia-conectar-proveedor-ia.md).

Antes de habilitar consultas con datos reales, el Administrador debe probar la conexión, revisar las condiciones de privacidad y retención del proveedor y configurar un límite de gasto en el proyecto API. La aprobación queda ligada al proveedor/modelo y se invalida al cambiar la clave o el modelo; también puede revocarse en cualquier momento. Sin aprobación vigente, las consultas quedan bloqueadas.

`store: false` evita almacenar el estado de la respuesta por esa llamada, pero no equivale a Zero Data Retention ni modifica los controles de seguridad propios del proveedor. En el piloto se deben usar datos sintéticos hasta revisar y aprobar las condiciones contractuales aplicables.

## Herramientas L0 (lectura)

- `dashboard.get_snapshot`: KPIs operativos y colección financiera por moneda.
- `finance.get_summary`: ingresos, egresos y pendientes en totales separados por moneda.
- `projects.list`: estado, prioridad, fechas, avance y cliente.
- `tasks.list`: estado, prioridad, vencimiento y proyecto; sin descripción ni horas.
- `clients.list`: nombre comercial y estado; sin contacto, CUIT, monto mensual ni notas.
- `meetings.list`: título, proyecto, horario, estado y cantidad de participantes; sin enlaces, ubicaciones ni nombres de participantes. Sin fechas devuelve los próximos 7 días.
- `people.list`: id y nombre de usuarios y desarrolladores activos, para resolver responsables y participantes por nombre.

Las listas usan un máximo de 25 registros. El período predeterminado de finanzas es de 30 días y el máximo de cualquier período es 90, en la zona `America/Argentina/Buenos_Aires`. Las respuestas muestran período, fuente, corte y enlace interno cuando corresponde. Las respuestas que no consultaron ninguna herramienta se guardan con `Grounded = false` y la pantalla muestra "Sin datos consultados".

## Acciones con confirmación

- `tasks.create` (`tasks.read` + `tasks.write`): proyecto y título obligatorios; descripción, fechas, prioridad, estado inicial y asignación a un desarrollador o a un usuario responsable.
- `tasks.update_status` (`tasks.read` + `tasks.write`): tarea y estado nuevo.
- `meetings.create` (`meetings.read` + `meetings.write`): proyecto, título e inicio en hora local de Buenos Aires (`yyyy-MM-ddTHH:mm`), con fin o duración (60 minutos por defecto), participantes, ubicación y descripción. Las horas se convierten a UTC al guardar.

Flujo:

1. La herramienta valida los argumentos, resuelve proyecto, tarea y personas y guarda un `AiPendingAction` con vencimiento de 10 minutos. Al modelo le devuelve `pending_confirmation` y el mensaje del asistente viaja con la tarjeta de la acción.
2. `POST /api/ai/conversations/{id}/actions/{actionId}/confirm` vuelve a validar permisos, sesión y argumentos y ejecuta el servicio de dominio con el mismo usuario. Un token de concurrencia evita ejecutar dos veces. Se agrega a la conversación un mensaje con el resultado ("Listo, creé la tarea…") y el enlace.
3. `POST /api/ai/conversations/{id}/actions/{actionId}/cancel` la descarta. Una acción vencida, cancelada o ya confirmada no se puede volver a confirmar.

Si el servicio de dominio rechaza la acción al confirmar, la acción queda en `Failed` y el mensaje explica el motivo. Los errores de validación de cualquier herramienta vuelven al modelo como error de herramienta para que pueda explicarlo o pedir el dato que falta.

## Historial y auditoría

- Las conversaciones y mensajes permanecen hasta que su propietario los elimina.
- El borrado elimina conversación, mensajes, ejecuciones y acciones propuestas asociadas.
- La auditoría conserva solo actor, herramienta, decisión (`Proposed`, `Confirmed`, `Cancelled`, `Expired`, `Failed`, etc.), resultado técnico, identificadores de correlación y fecha durante 90 días. No almacena pregunta, respuesta ni payloads de herramientas.
- La aplicación purga auditoría vencida diariamente.
- Las solicitudes tienen un límite de 10 por minuto por instalación; cada ejecución permite hasta 6 llamadas al modelo, 12 invocaciones de herramientas y 5 acciones propuestas.

## API

Las rutas `/api/ai/conversations...` requieren `ai.office.use` y rol Administrador. Cada consulta vuelve a comprobar la vigencia de sesión, las políticas de dominio y el catálogo allowlist. El límite mensual monetario debe configurarse en el proyecto API del proveedor; todavía no hay una cuota monetaria aplicada por Kodvian.

## Verificación

- Backend: pruebas de cifrado de claves, aprobación de privacidad, propiedad de conversación, idempotencia de solicitud, minimización de campos, permisos, prompt con fecha, respuestas sin fuente, errores devueltos al modelo y acciones propuestas (crear, confirmar, cancelar, vencida, rechazada).
- Frontend: pruebas de historial, fuentes, Markdown seguro, tarjetas de acción y sugerencias.
- Configuración operativa de Railway: [protección del proveedor IA](../backend/ai-provider-configuration.md).
