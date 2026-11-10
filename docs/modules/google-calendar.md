# Google Calendar

Cada usuario puede conectar su cuenta personal de Google para que las reuniones que organiza en Kodvian se agenden en su Google Calendar, con los participantes como invitados.

## Qué hace

- **Conectar**: botón "Conectar Google Calendar" en el encabezado de Agenda y en las preferencias de Oficina Inteligente. Usa OAuth 2.0 (código de autorización con PKCE S256 y `state` firmado que vence a los 10 minutos). Permisos pedidos: `openid email https://www.googleapis.com/auth/calendar.events`.
- **Guardar**: el refresh token se guarda por usuario en `GoogleCalendarConnections`, cifrado con DataProtection (propósito `Kodvian.Core.GoogleCalendar.RefreshToken.v1`). Nunca se devuelve al frontend.
- **Sincronizar**: al crear, editar o cancelar una reunión (desde la pantalla o desde el asistente) se crea, actualiza o elimina el evento en el calendario principal del **organizador**. Los participantes van como invitados (`sendUpdates=all`, Google les manda la invitación), con la zona horaria `America/Argentina/Buenos_Aires`. El id del evento queda en `Meeting.GoogleEventId`.
- **Sin bloquear**: si Google falla, la reunión se guarda igual y queda marcada "No sincronizada" con el motivo. En el detalle de la reunión aparece **Reintentar** (lo puede usar el organizador o un Administrador).
- **Desconectar**: revoca el token en Google y borra la conexión. Los eventos ya creados quedan en el calendario.
- Si Google revoca el acceso (por ejemplo, el usuario lo quitó desde su cuenta de Google), la conexión muestra "volvé a conectar Google Calendar".
- La sincronización es en un solo sentido: Kodvian → Google. Los cambios hechos directamente en Google no vuelven a Kodvian.

## Endpoints

| Método | Ruta | Uso |
| --- | --- | --- |
| GET | `/api/integrations/google-calendar/status` | Estado: habilitado, conectado, email, último error |
| POST | `/api/integrations/google-calendar/connect` | Devuelve la URL de autorización de Google (409 si no está configurado) |
| GET | `/api/integrations/google-calendar/callback` | Vuelta desde Google (anónimo; valida el `state`) y redirige a `/agenda?googleCalendar=connected` o `…=error&reason=…` |
| DELETE | `/api/integrations/google-calendar` | Desconectar |
| POST | `/api/integrations/google-calendar/meetings/{id}/sync` | Reintentar la sincronización de una reunión |

## Configuración en Google Cloud (una sola vez)

1. Entrar a <https://console.cloud.google.com/> y crear un proyecto (por ejemplo "Kodvian").
2. **APIs y servicios → Biblioteca** → buscar **Google Calendar API** → **Habilitar**.
3. **APIs y servicios → Pantalla de consentimiento de OAuth** (Google Auth Platform):
   - Tipo de usuario: **Externo**.
   - Nombre de la app, email de soporte y email de contacto del desarrollador.
   - **Acceso a los datos / Permisos**: agregar `openid`, `.../auth/userinfo.email` y `https://www.googleapis.com/auth/calendar.events`.
   - **Público → Usuarios de prueba**: agregar la cuenta de Google de cada persona que vaya a conectar su calendario mientras la app esté en modo "Prueba" (máximo 100). Para abrirlo a cualquier cuenta hay que publicar la app y pasar la verificación de Google, porque `calendar.events` es un permiso sensible.
   - En modo "Prueba" Google vence los refresh tokens a los 7 días: cada usuario tendría que volver a conectar. Al publicar la app ya no vencen.
4. **APIs y servicios → Credenciales → Crear credenciales → ID de cliente de OAuth**:
   - Tipo de aplicación: **Aplicación web**.
   - **URIs de redireccionamiento autorizados** (exactos, sin barra final):
     - Producción: `https://<dominio-de-kodvian>/api/integrations/google-calendar/callback`
     - Local: `http://localhost:4200/api/integrations/google-calendar/callback`
   - Los "Orígenes de JavaScript autorizados" no hacen falta (el intercambio del código lo hace el backend).
5. Copiar el **ID de cliente** y el **Secreto del cliente**.

En local, `ng serve` (puerto 4200) reenvía `/api` al backend en `http://localhost:5297` con `proxy.conf.json`, por eso la URI local usa el puerto 4200. En producción la API sirve el SPA desde el mismo dominio.

## Variables de entorno

| Variable | Valor |
| --- | --- |
| `GOOGLE_CALENDAR_CLIENT_ID` | ID de cliente de OAuth |
| `GOOGLE_CALENDAR_CLIENT_SECRET` | Secreto del cliente (cargarlo como secreto en Railway) |
| `GOOGLE_CALENDAR_REDIRECT_URI` | `https://<dominio-de-kodvian>/api/integrations/google-calendar/callback` (igual a la registrada en Google) |

También se aceptan como `GoogleCalendar:ClientId`, `GoogleCalendar:ClientSecret` y `GoogleCalendar:RedirectUri` (o `GoogleCalendar__ClientId`, etc.). Si falta cualquiera de las tres, la integración queda deshabilitada: las reuniones funcionan igual y la pantalla avisa que Google Calendar no está configurado.

Las claves de DataProtection tienen que persistir entre despliegues (ya es así para el resto de los datos cifrados); si se pierden, los usuarios tienen que volver a conectar.

## Modelo y migración

- `GoogleCalendarConnection`: `UserId` (clave), `GoogleEmail`, `ProtectedRefreshToken`, `ConnectedAtUtc`, `LastSyncAtUtc`, `LastError`.
- `Meeting`: `GoogleEventId`, `GoogleCalendarOwnerUserId`, `GoogleSyncStatus` (`Synced`/`Failed`), `GoogleSyncError`, `GoogleSyncedAtUtc`.
- Migración `AddGoogleCalendarIntegration`, aplicada al arrancar la API (`MigrateAsync`).

## Pruebas

`GoogleCalendarIntegrationTests` usa un cliente de Google falso (sin red): integración deshabilitada, conexión con PKCE y token cifrado, callbacks inválidos, alta/edición/cancelación, fallas con reintento y acceso revocado con desconexión. En el frontend, `google-calendar.spec.ts` cubre conectar, desconectar, el mensaje de vuelta desde Google y el reintento.
