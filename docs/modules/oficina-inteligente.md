# Oficina Inteligente

Asistente con IA de Kodvian Core. Responde consultas con datos verificables de la empresa y prepara acciones (tareas, reuniones, movimientos, prospectos) que el usuario confirma con un botón. Funciona con OpenAI o con Claude (Anthropic).

## Quién lo usa

- Cualquier usuario con el permiso `ai.office.use`: lo tienen los roles Administrador, Operativo, Analista y Desarrollador (Solo lectura no).
- Cada herramienta exige además sus permisos de dominio y recorta los datos según el rol. Se revalidan al ejecutar la herramienta, al confirmar una acción y al deshacerla.
  - Un Desarrollador solo ve su día, sus tareas y sus reuniones, y solo puede cambiar el estado de sus propias tareas.
  - Operativo y Analista ven lo operativo (proyectos, tareas, clientes, agenda) pero no finanzas ni el panel ejecutivo.
  - Las reuniones usan el mismo alcance que la pantalla Agenda: fuera de Administración, solo las propias o de proyectos con acceso.
- La configuración del proveedor y el tablero de Uso de IA son solo para Administradores.
- Los permisos viajan en la sesión: después de un cambio de roles hay que volver a iniciar sesión.

## Configuración y aprobación de privacidad

Desde Administración → Configurar proveedor de IA, un Administrador elige el proveedor (OpenAI o Anthropic), el modelo y la clave API. La clave se cifra en backend y nunca se devuelve al frontend. Con OpenAI la prueba de conexión envía una solicitud mínima con `store: false` y puede generar un costo pequeño; con Anthropic consulta el modelo en la Models API, sin costo. Paso a paso: [guía de conexión](../guia-conectar-proveedor-ia.md).

Opcionalmente se puede indicar un **modelo rápido** (más chico y barato) del mismo proveedor: se usa en consultas simples; las acciones, comparaciones y análisis siempre usan el modelo principal.

Antes de habilitar consultas con datos reales, el Administrador debe probar la conexión, revisar las condiciones de privacidad y retención del proveedor y configurar un límite de gasto en el proyecto API. La aprobación queda ligada al proveedor y a los modelos (principal y rápido) y se invalida al cambiar la clave o cualquiera de los modelos; también puede revocarse en cualquier momento. Sin aprobación vigente, las consultas quedan bloqueadas.

`store: false` evita almacenar el estado de la respuesta por esa llamada en OpenAI, pero no equivale a Zero Data Retention ni modifica los controles de seguridad propios del proveedor.

## Cómo responde

- **Prompt de sistema** armado en cada ejecución (`AiSystemPromptBuilder`): fecha, hora y zona `America/Argentina/Buenos_Aires`, nombre del usuario, capacidades habilitadas, preferencias del usuario y pantalla desde la que se abrió (solo rutas internas). Responde en español rioplatense.
- **Respuesta en vivo** (`POST /api/ai/conversations/{id}/messages/stream`, server-sent events): eventos `status` ("Consultando tareas…"), `delta` (texto), `reset`, `done` y `error`. OpenAI usa `stream=true` en la Responses API y Claude `CreateStreaming` del SDK. El endpoint común `POST …/messages` queda como respaldo.
- **Sugerencias de seguimiento**: el modelo cierra cada respuesta con `[[sugerencias: …]]`; el backend las separa del texto (`AiMessages.SuggestionsJson`) y la pantalla las muestra como botones.
- **Respuestas sin fuente**: si no consultó datos se guardan igual con `Grounded = false` y la pantalla muestra "Sin datos consultados".
- **Errores de herramientas**: las validaciones y los rechazos de dominio vuelven al modelo como error de herramienta (con Claude, `tool_result` con `is_error`) para que lo explique o pida el dato que falta. Solo la sesión o la aprobación revocadas cortan la ejecución.
- **Memoria entre turnos**: de cada resultado se guardan solo pares tipo/nombre/id (`AiMessages.ContextJson`) y se agregan al historial del modelo, así entiende "ese proyecto" sin volver a buscar. No se guardan montos.
- **Límites por ejecución**: 6 llamadas al modelo, 12 herramientas y 5 acciones propuestas. Claude usa prompt caching automático. OpenAI: hasta 4000 tokens de salida y 120 s de espera.

## Herramientas de consulta

| Herramienta | Permisos | Qué devuelve |
|---|---|---|
| `dashboard.get_snapshot` | `dashboard.read` | KPIs del mes y finanzas por moneda |
| `finance.get_summary` | `finances.read` | Ingresos, egresos y pendientes por moneda (hasta 90 días) |
| `finance.list_movements` | `finances.read` | Movimientos con filtros y totales de todo el filtro |
| `finance.list_pending` | `finances.read` | Cobros y pagos pendientes con días de atraso y "¿quién me debe?" por cliente |
| `finance.compare_months` | `finances.read` | Mes contra mes, variación y categorías de gasto que más cambiaron |
| `projects.list` / `projects.get` | `projects.read` | Lista y ficha (avance, entrega, tareas atrasadas, reuniones y, con finanzas, montos) |
| `tasks.list` | `tasks.read` o `developer.work.read` | Tareas; sin acceso al tablero completo, solo las propias |
| `clients.list` / `clients.get` | `clients.read` | Lista y ficha sin datos de contacto ni fiscales |
| `meetings.list` | `meetings.read` | Reuniones del alcance del usuario; sin fechas, los próximos 7 días |
| `my_day.get` | cualquiera de tareas, agenda o finanzas | Tareas que vencen y atrasadas, reuniones y cobros del día |
| `people.list` | `team.read`, `tasks.write` o `meetings.write` | Id y nombre de usuarios y desarrolladores activos |

Las listas usan un máximo de 25 registros y los períodos un máximo de 90 días.

## Acciones con confirmación

| Acción | Permisos | Deshacer (15 min) |
|---|---|---|
| `tasks.create` | `tasks.read` + `tasks.write` | Sí (si nadie la modificó) |
| `tasks.update` (reprogramar, prioridad, título, reasignar) | `tasks.read` + `tasks.write` | Sí (si nadie la modificó) |
| `tasks.update_status` | `tasks.write`, o `developer.tasks.status.write` solo en tareas propias | Sí |
| `meetings.create` | `meetings.read` + `meetings.write` | Sí (queda cancelada) |
| `meetings.update` (mover, duración, título, ubicación) | `meetings.read` + `meetings.write` | Sí (si nadie la modificó) |
| `meetings.cancel` | `meetings.read` + `meetings.cancel` | No |
| `finance.create_movement` | `finances.read` + `finances.write` | No |
| `clients.create_prospect` | `clients.read` + `clients.write` | No |
| `preferences.update` | `ai.office.use` | Sí |

Flujo:

1. La herramienta valida, resuelve proyecto, tarea y personas y guarda un `AiPendingAction` que vence a los 10 minutos. No escribe nada.
2. `POST /api/ai/conversations/{id}/actions/{actionId}/confirm` revalida permisos, sesión y argumentos y ejecuta el servicio de dominio con el mismo usuario. Un token de concurrencia evita ejecutar dos veces; los movimientos usan el id de la acción como identificador de operación.
3. `POST …/messages/{messageId}/actions/confirm-all` confirma todas las acciones de un mensaje, cada una por separado, con un único resumen.
4. `POST …/actions/{actionId}/cancel` la descarta y `POST …/actions/{actionId}/undo` la revierte cuando es seguro.
5. Una acción que quedó "Procesando" más de 5 minutos se marca como no completada y se pide revisar la pantalla antes de reintentar.

## Resumen proactivo y alertas

`GET /api/ai/briefing` arma, sin IA y según los permisos, el resumen que se ve al abrir una conversación nueva: tareas vencidas y de la semana, reuniones, cobros pendientes y proyectos en riesgo. Además genera alertas con una consulta sugerida ("Resolver con IA"):

- tareas vencidas hace más de 7 días;
- proyectos con entrega en 14 días o vencida y menos de 70% de avance;
- cobros vencidos;
- gastos de los últimos 30 días que subieron 30% o más;
- tope mensual de IA al 80% o superado (solo Administradores).

## Pantalla

- Markdown seguro (se escapa todo el HTML), tarjetas de acción con Confirmar, Cancelar, Deshacer y "Confirmar todas".
- Tarjetas clickeables con lo que consultó el asistente y mini gráfico de ingresos contra gastos (uno por moneda).
- 👍/👎 por respuesta (`POST …/messages/{messageId}/feedback`).
- Conversaciones fijadas, renombradas y buscadas por título o por texto (`PATCH /api/ai/conversations/{id}`, parámetro `search`).
- Dictado por voz (Web Speech API, es-AR) cuando el navegador lo soporta.
- Botón flotante y atajo Ctrl+K (Cmd+K) en todas las pantallas: abre Oficina Inteligente con la pregunta y la pantalla de origen.
- Panel de preferencias (`GET/PUT /api/ai/preferences`): moneda que se muestra primero, duración de reuniones, prioridad de tareas, estilo de respuesta, alcance de "mi día" e indicaciones libres.

## Uso, costos y evals

- Administración → Uso de IA (`GET /api/ai/usage`): consultas por día, tokens, costo estimado por modelo, herramientas más usadas, errores, acciones y valoraciones.
- Precios por millón de tokens y tope mensual propio (`PUT /api/ai/usage/settings`). Cada ejecución guarda su costo estimado. Al 80% del tope hay alerta; al 100% el asistente queda pausado hasta el mes siguiente o hasta ampliarlo. Sin precios cargados no se estima costo ni se aplica el tope.
- Evals: `backend/tests/Kodvian.Core.Application.Tests/AiEvals/evals.json` (26 preguntas). `EvalSetIsConsistentWithTheCatalog` corre siempre. `LiveEvalsAgainstTheRealModel` corre contra el modelo real solo si se configuran `KODVIAN_AI_EVALS_PROVIDER`, `KODVIAN_AI_EVALS_MODEL` y `KODVIAN_AI_EVALS_API_KEY` (consume tokens; exige 80% de aciertos).

## Historial y auditoría

- Las conversaciones y mensajes permanecen hasta que su propietario los elimina; el borrado elimina conversación, mensajes, ejecuciones y acciones.
- La auditoría conserva actor, herramienta, decisión (`Allowed`, `Denied`, `Proposed`, `Confirmed`, `Cancelled`, `Expired`, `Failed`, `Undone`, `Feedback`, etc.), resultado técnico, identificadores de correlación y fecha durante 90 días. No almacena pregunta, respuesta ni payloads de herramientas.
- Límite de 10 solicitudes de mensaje por minuto por instalación.

## Migraciones

`AddAiMessageGrounded`, `AddAiPendingActions`, `AddAiMessageSuggestions`, `AddAiPendingActionUndo`, `AddAiMemoryAndPreferences`, `AddAiScreenFeatures` y `AddAiUsageControls`. Se aplican al arrancar la API (`MigrateAsync`).

## Pendientes (requieren una decisión y credenciales externas)

- **Google Calendar**: crear un proyecto en Google Cloud con OAuth (pantalla de consentimiento, client id y secret), guardar los tokens de cada usuario cifrados y, al confirmar `meetings.create`/`meetings.update`/`meetings.cancel`, crear, mover o cancelar el evento en el calendario de los participantes. Hace falta definir si se sincroniza en un solo sentido o en ambos.
- **WhatsApp o Telegram**: Telegram necesita un bot (token de BotFather) y vincular cada chat con un usuario de Kodvian; WhatsApp necesita la API de Meta Business (número verificado, plantillas aprobadas y webhook público). En ambos casos las acciones tendrían que confirmarse con un botón del chat y reutilizar el mismo flujo de `AiPendingAction`.
- **Resumen semanal por mail**: elegir un servicio de envío (SMTP, Resend, SendGrid…), guardar su credencial como secreto de Railway y programar un trabajo semanal que reutilice `AiBriefingService` por usuario, con opción de darse de baja.
- **Servidor MCP**: exponer el catálogo (`AiToolCatalog`) como servidor MCP remoto con OAuth para usar Kodvian desde Claude Desktop, Claude Code u otros clientes, reutilizando permisos, confirmaciones y auditoría. Conviene antes desacoplar el catálogo en una definición más un handler por herramienta.

## Verificación

- Backend: prompt, streaming de ambos proveedores, sugerencias, errores devueltos al modelo, herramientas de consulta y de acción, confirmar, confirmar todas, cancelar, vencidas, deshacer, memoria, preferencias, resumen, uso y tope, modelo por tarea, acciones trabadas y permisos por rol.
- Frontend: streaming, Markdown seguro, tarjetas de acción, resumen, preferencias, valoraciones, conversaciones fijadas o buscadas, lanzador Ctrl+K y gráficos.
- Configuración operativa de Railway: [protección del proveedor IA](../backend/ai-provider-configuration.md).
