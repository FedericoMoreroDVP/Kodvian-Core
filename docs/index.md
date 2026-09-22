# Kodvian Core Documentation

Esta carpeta concentra la documentacion funcional y tecnica de Kodvian Core.

Kodvian Core es un sistema interno liviano para gestionar una empresa de software: clientes, proyectos, tareas, desarrolladores, contratos, pagos, finanzas, administracion y dashboard operativo.

## Lectura recomendada

1. [Product overview](product-overview.md)
2. [Arquitectura general](architecture.md)
3. [Backend overview](backend/overview.md)
4. [Frontend overview](frontend/overview.md)
5. [Modulos funcionales](modules/dashboard.md)
6. [Setup local](development/local-setup.md)
7. [Estrategia de testing](development/testing-strategy.md)
8. [Railway readiness](railway-readiness.md)

## Documentacion general

- [Product overview](product-overview.md): vision funcional del producto y alcance actual.
- [Arquitectura general](architecture.md): arquitectura full-stack, capas, runtime y contratos principales.

## Backend

- [Overview](backend/overview.md): estructura backend y responsabilidades por capa.
- [API reference](backend/api-reference.md): endpoints disponibles agrupados por controller.
- [Autenticacion y autorizacion](backend/authentication-authorization.md): JWT, cookie, roles, permisos y policies.
- [Modelo de datos](backend/data-model.md): entidades, relaciones y enums.
- [Modulos backend](backend/modules.md): mapa tecnico por modulo.
- [Storage](backend/storage.md): almacenamiento local/S3 y archivos PDF.
- [Migraciones y seeding](backend/migrations-seeding.md): EF Core migrations, startup y datos iniciales.
- [Riesgos operativos](backend/operational-risks.md): puntos sensibles para produccion y mantenimiento.

## Frontend

- [Overview](frontend/overview.md): arquitectura Angular y estructura de la app.
- [Routing y navegacion](frontend/routing-navigation.md): rutas, layout, sidebar y guards.
- [Sistema visual](frontend/design-system.md): tokens, Material, SCSS y lenguaje visual.
- [Modulos frontend](frontend/modules.md): mapa tecnico por modulo.
- [Auth y sesion](frontend/auth-session.md): login, session state, interceptor y permisos.
- [Guia UX/UI](frontend/ux-ui-guidelines.md): criterios de experiencia, patrones y anti-patrones.
- [Responsive y accesibilidad](frontend/responsive-accessibility.md): criterios minimos por viewport y accesibilidad.

## Modulos

- [Dashboard](modules/dashboard.md)
- [Clientes](modules/clientes.md)
- [Equipo](modules/equipo.md)
- [Proyectos](modules/proyectos.md)
- [Tareas](modules/tareas.md)
- [Finanzas](modules/finanzas.md)
- [Visión financiera](modules/vision-financiera.md)
- [Administracion](modules/administracion.md)

## Desarrollo

- [Setup local](development/local-setup.md)
- [Estrategia de testing](development/testing-strategy.md)
- [Checklist de validacion](development/validation-checklist.md)
- [Convenciones](development/conventions.md)

## Mantenimiento

Al cambiar un flujo, actualizar la documentacion relacionada en este orden:

1. Documento del modulo en `docs/modules`.
2. Referencia API o documentacion frontend/backend afectada.
3. Tests automaticos del flujo nuevo o modificado.
4. `docs/development/testing-strategy.md` si cambia el criterio de cobertura.
5. `docs/architecture.md` si cambia una decision transversal.
6. `docs/index.md` si se agrega una seccion nueva.
