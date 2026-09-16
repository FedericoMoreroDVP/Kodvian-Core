# Equipo

## Resumen funcional

El modulo Equipo gestiona desarrolladores, analistas y accesos operativos. Es el punto de trabajo para administradores y analistas sobre el equipo tecnico, sin exponer contratos, pagos ni arreglos economicos al rol `Analista`.

## Ruta frontend

- Ruta visible: `/equipo`.
- Ruta legacy: `/desarrolladores` redirige a `/equipo`.
- Implementacion actual: `frontend/src/app/modules/desarrolladores/**`.

## Permisos

- `team.read`: ver equipo.
- `team.write`: crear y editar desarrolladores, analistas y accesos.

## Rol Analista

Puede gestionar clientes, proyectos, documentos, equipo y tareas. No puede ver finanzas, contratos economicos, pagos, cobros ni ledger.

Desde `/equipo`, el boton `Nuevo analista` crea un usuario del sistema con rol `Analista` y un perfil `Developer` asociado para acuerdos economicos y pagos. Este perfil remunerable permite que el analista a cargo aparezca en contratos asociados de proyecto.

El perfil remunerable del analista aparece en la grilla de desarrolladores si su cuenta también tiene el rol Desarrollador. La misma persona puede figurar en ambas categorías utilizando el mismo perfil.

## Desarrolladores

El boton `Nuevo desarrollador` crea un `Developer`. Si se habilita `Permitir acceso al sistema`, tambien se crea o actualiza un usuario asociado con rol `Desarrollador`.

La grilla muestra perfiles externos sin cuenta y perfiles de usuarios que incluyan Desarrollador entre sus roles. Los roles adicionales se muestran junto al nombre. Editar un desarrollador conserva todos sus roles.

Los roles se gestionan en Administración → Usuarios, exclusivamente por administradores. Solo administradores pueden editar una cuenta administradora desde Equipo. Cambiar de desarrollador a analista conserva su identidad, tareas, contratos y asignaciones. Nombre, correo y actividad se sincronizan con el perfil vinculado; los cambios de cuenta invalidan sus sesiones anteriores.

## Endpoints de usuarios de equipo

- `GET /api/team/users/analysts`: lista analistas.
- `POST /api/team/users/analysts`: crea analista con contraseña inicial obligatoria.
- `PUT /api/team/users/analysts/{id}`: edita analista y permite cambiar contraseña opcionalmente.

## Asignacion a proyectos

La asignacion operativa de desarrolladores a proyectos se realiza con `ProjectDeveloperAssignment` desde el dialog de Equipo del proyecto. Esta asignacion no incluye monto, porcentaje, modalidad de pago ni pagos.

El analista a cargo se asigna desde el mismo dialog usando `Project.ResponsableId`. Si tiene perfil remunerable asociado, puede crear acuerdos y pagos desde contratos asociados.

## Datos economicos

Contratos de desarrolladores o analistas, pagos, comprobantes de pago, ledger y resumen de contratos quedan restringidos a `Administrador`.
