# Agenda

## Resumen funcional

La Agenda permite organizar reuniones de coordinación vinculadas a un proyecto. Cada reunión conserva los temas a tratar, horario, participantes internos, ubicación o enlace, estado y prioridad.

## Pantalla

- Ruta: `/agenda`.
- Vistas: día, semana, mes y lista.
- Desde el menú contextual de un proyecto se puede abrir una nueva reunión con el proyecto precargado.
- La agenda usa el color estable configurado en cada proyecto y un indicador de criticidad independiente: verde para baja, amarillo para media, naranja para alta y rojo para urgente.

## API

- `GET /api/meetings?from=&to=&projectId=&priority=&status=`.
- `GET /api/meetings/lookups`.
- `GET /api/meetings/{id}`.
- `POST /api/meetings`.
- `PUT /api/meetings/{id}`.
- `PATCH /api/meetings/{id}/cancel`.

Los horarios se almacenan en UTC. Las reuniones canceladas se conservan como historial mediante el estado `Cancelada` y confirmación explícita.

## Permisos y visibilidad

- `meetings.read`: consulta de la agenda.
- `meetings.write`: creación y edición.
- `meetings.cancel`: cancelación conservando historial.
- Los administradores ven toda la agenda. Los demás usuarios sólo ven reuniones creadas por ellos, donde participan o relacionadas con proyectos bajo su responsabilidad o asignación.

## Modelo

- `Meeting`: proyecto, creador, título, descripción, inicio/fin, prioridad, estado, enlace y ubicación.
- `MeetingParticipant`: relación única entre una reunión y un usuario interno.

Las restricciones aseguran que el fin sea posterior al inicio, que el proyecto exista y que los participantes estén activos.
