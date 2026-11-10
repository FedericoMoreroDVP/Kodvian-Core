using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Abstractions;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Clients.Abstractions;
using Kodvian.Core.Application.Clients.Requests;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Dashboard.Abstractions;
using Kodvian.Core.Application.Dashboard.Requests;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Application.Meetings.Abstractions;
using Kodvian.Core.Application.Meetings.Requests;
using Kodvian.Core.Application.Projects.Abstractions;
using Kodvian.Core.Application.Projects.Requests;
using Kodvian.Core.Application.Tasks.Abstractions;
using Kodvian.Core.Application.Tasks.Requests;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed partial class AiToolCatalog(
    IClientService clients,
    IProjectService projects,
    ITaskService tasks,
    IMeetingService meetings,
    IFinanceOverviewService finance,
    IDashboardService dashboard,
    IFinancialMovementService movements,
    IAiProviderConfigurationService providerConfiguration,
    ISessionValidator sessionValidator,
    KodvianDbContext dbContext) : IAiToolExecutor
{
    private const int MaxPageSize = 25;
    private static readonly string[] TaskStatuses = ["Pendiente", "EnCurso", "Bloqueada", "Finalizada", "Cancelada"];
    private static readonly string[] ProjectStatuses = ["Planificacion", "EnCurso", "Pausado", "Finalizado", "Cancelado", "Presupuestado"];
    private static readonly string[] ClientStatuses = ["Prospecto", "Activo", "Pausado", "Finalizado", "Presupuestado"];
    private static readonly string[] MeetingStatuses = Enum.GetNames<MeetingStatus>();
    private static readonly string[] Priorities = ["Baja", "Media", "Alta", "Urgente"];
    private static readonly string[] NewTaskStatuses = ["Pendiente", "EnCurso"];
    // Declarados acá (y no en los archivos parciales) para que estén inicializados antes que Definitions.
    private static readonly string[] MovementTypes = ["Ingreso", "Egreso"];
    private static readonly string[] MovementStatuses = ["Pendiente", "Cobrado", "Pagado", "Vencido", "Anulado"];
    private static readonly string[] CurrencyOptions = ["ARS", "USD"];
    private static readonly string[] PendingKinds = ["cobros", "pagos", "todos"];
    private static readonly string[] DayScopes = ["todo", "mio"];
    private static readonly TimeZoneInfo BuenosAiresTimeZone = ResolveBuenosAiresTimeZone();
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-AR");
    private const string LocalDateTimePattern = "^\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}$";

    private static readonly AiToolDefinitionDto[] Definitions =
    [
        new() { Name = "dashboard.get_snapshot", Description = "Consulta los indicadores ejecutivos del mes calendario indicado. La respuesta financiera viene separada por moneda.", RequiredPermissions = [PermissionCodes.DashboardRead], Parameters = ObjectSchema(
            [("year", IntegerSchema(2000, 2100)), ("month", IntegerSchema(1, 12))], []) },
        new() { Name = "finance.get_summary", Description = "Consulta ingresos, gastos y pendientes por moneda para un período de hasta 90 días. No mezcles ARS y USD.", RequiredPermissions = [PermissionCodes.FinancesRead], Parameters = ObjectSchema(
            [("from", DateSchema()), ("to", DateSchema())], []) },
        new() { Name = "projects.list", Description = "Lista proyectos visibles con campos operativos mínimos. Limita los resultados a la primera página.", RequiredPermissions = [PermissionCodes.ProjectsRead], Parameters = ObjectSchema(
            [("search", StringSchema(100)), ("status", EnumSchema(ProjectStatuses)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "tasks.list", Description = "Lista tareas visibles. Devuelve título, proyecto, estado, prioridad y vencimiento; no devuelve descripción ni horas. Quien no tiene acceso al tablero completo ve solo sus tareas asignadas.", RequiredPermissions = [], AnyOfPermissions = [PermissionCodes.TasksRead, PermissionCodes.DeveloperWorkRead], Parameters = ObjectSchema(
            [("search", StringSchema(120)), ("projectId", GuidSchema()), ("status", EnumSchema(TaskStatuses)), ("dueDateFrom", DateSchema()), ("dueDateTo", DateSchema()), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "clients.list", Description = "Lista clientes visibles con nombre comercial y estado; excluye datos de contacto, montos y notas.", RequiredPermissions = [PermissionCodes.ClientsRead], Parameters = ObjectSchema(
            [("search", StringSchema(100)), ("status", EnumSchema(ClientStatuses)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "meetings.list", Description = "Lista reuniones visibles durante un período de hasta 90 días. Sin fechas devuelve los próximos 7 días (desde hoy). Para reuniones pasadas indicá from y to. No devuelve enlaces, ubicación ni nombres de participantes.", RequiredPermissions = [PermissionCodes.MeetingsRead], Parameters = ObjectSchema(
            [("from", DateSchema()), ("to", DateSchema()), ("projectId", GuidSchema()), ("status", EnumSchema(MeetingStatuses)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "people.list", Description = "Busca personas por nombre para asignar tareas o invitar a reuniones. Devuelve usuarios activos (sirven como responsable de tarea y como participantes de reunión) y desarrolladores activos (sirven como desarrollador asignado de una tarea). Solo id y nombre.", RequiredPermissions = [], AnyOfPermissions = [PermissionCodes.TeamRead, PermissionCodes.TasksWrite, PermissionCodes.MeetingsWrite], Parameters = ObjectSchema(
            [("search", StringSchema(100)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "finance.list_movements", Description = "Lista movimientos financieros (ingresos y egresos) de un período de hasta 90 días, por defecto los últimos 30. Filtra por tipo, estado, moneda, cliente, proyecto o texto (descripción, cliente, proveedor o categoría). Devuelve totales por moneda y tipo de todo el filtro. Excluye anulados salvo que se pidan.", RequiredPermissions = [PermissionCodes.FinancesRead], Parameters = ObjectSchema(
            [("from", DateSchema()), ("to", DateSchema()), ("type", EnumSchema(MovementTypes)), ("status", EnumSchema(MovementStatuses)), ("currency", EnumSchema(CurrencyOptions)),
             ("search", StringSchema(100)), ("clientId", GuidSchema()), ("projectId", GuidSchema()), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "finance.list_pending", Description = "Cobros y pagos pendientes o vencidos, ordenados por vencimiento, con días de atraso, totales por moneda (y cuánto está vencido) y receivablesByClient: cuánto debe cada cliente (sirve para \"¿quién me debe?\"). kind: cobros, pagos o todos.", RequiredPermissions = [PermissionCodes.FinancesRead], Parameters = ObjectSchema(
            [("kind", EnumSchema(PendingKinds)), ("overdueOnly", BooleanSchema()), ("dueUntil", DateSchema()), ("currency", EnumSchema(CurrencyOptions)), ("clientId", GuidSchema()), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "finance.compare_months", Description = "Compara ingresos, egresos y resultado por moneda entre dos meses (por defecto el actual contra el anterior), con variación porcentual y las categorías de gasto que más cambiaron.", RequiredPermissions = [PermissionCodes.FinancesRead], Parameters = ObjectSchema(
            [("year", IntegerSchema(2000, 2100)), ("month", IntegerSchema(1, 12)), ("compareYear", IntegerSchema(2000, 2100)), ("compareMonth", IntegerSchema(1, 12))], []) },
        new() { Name = "my_day.get", Description = "Arma el día: tareas que vencen y tareas atrasadas, reuniones del día (hora de Buenos Aires) y cobros/pagos que vencen o están vencidos. Por defecto hoy. scope: todo (toda la empresa) o mio (solo lo asignado al usuario). Usalo para \"¿qué tengo hoy?\".", RequiredPermissions = [], AnyOfPermissions = [PermissionCodes.TasksRead, PermissionCodes.DeveloperWorkRead, PermissionCodes.MeetingsRead, PermissionCodes.FinancesRead], Parameters = ObjectSchema(
            [("date", DateSchema()), ("scope", EnumSchema(DayScopes))], []) },
        new() { Name = "projects.get", Description = "Ficha de un proyecto: estado, avance, fechas y días a la entrega, responsable, desarrolladores asignados, cantidad de tareas por estado, tareas atrasadas, próximas tareas, próximas reuniones y, con permiso de finanzas, cobrado/pagado y pendiente por moneda. Requiere projectId de projects.list.", RequiredPermissions = [PermissionCodes.ProjectsRead], Parameters = ObjectSchema(
            [("projectId", GuidSchema())], ["projectId"]) },
        new() { Name = "clients.get", Description = "Ficha de un cliente: estado, proyectos con avance, próximas reuniones y, con permiso de finanzas, saldo pendiente y vencido por moneda y fecha del último cobro. No incluye datos de contacto ni fiscales. Requiere clientId de clients.list.", RequiredPermissions = [PermissionCodes.ClientsRead], Parameters = ObjectSchema(
            [("clientId", GuidSchema())], ["clientId"]) },
        new() { Name = "tasks.create", RequiresConfirmation = true, Description = "Prepara la creación de una tarea para que el usuario la confirme; no la crea. Requiere projectId (de projects.list) y title. Asigná con developerId (desarrollador de people.list) o responsibleUserId (usuario de people.list), no ambos. Fechas en yyyy-MM-dd.", RequiredPermissions = [PermissionCodes.TasksRead, PermissionCodes.TasksWrite], Parameters = ObjectSchema(
            [("projectId", GuidSchema()), ("title", StringSchema(200)), ("description", StringSchema(2000)), ("startDate", DateSchema()), ("dueDate", DateSchema()),
             ("priority", EnumSchema(Priorities)), ("status", EnumSchema(NewTaskStatuses)), ("developerId", GuidSchema()), ("responsibleUserId", GuidSchema())], ["projectId", "title"]) },
        new() { Name = "tasks.update_status", RequiresConfirmation = true, Description = "Prepara el cambio de estado de una tarea para que el usuario lo confirme; no lo aplica. Requiere taskId (de tasks.list) y el estado nuevo.", RequiredPermissions = [], AnyOfPermissions = [PermissionCodes.TasksWrite, PermissionCodes.DeveloperTasksStatusWrite], Parameters = ObjectSchema(
            [("taskId", GuidSchema()), ("status", EnumSchema(TaskStatuses))], ["taskId", "status"]) },
        new() { Name = "meetings.create", RequiresConfirmation = true, Description = "Prepara la creación de una reunión en la agenda para que el usuario la confirme; no la crea. Requiere projectId (de projects.list), title y startsAt en hora local de Buenos Aires (yyyy-MM-ddTHH:mm). Indicá endsAt o durationMinutes (por defecto 60). Participantes: ids de usuarios de people.list.", RequiredPermissions = [PermissionCodes.MeetingsRead, PermissionCodes.MeetingsWrite], Parameters = ObjectSchema(
            [("projectId", GuidSchema()), ("title", StringSchema(200)), ("startsAt", LocalDateTimeSchema()), ("endsAt", LocalDateTimeSchema()), ("durationMinutes", IntegerSchema(15, 480)),
             ("description", StringSchema(2000)), ("location", StringSchema(300)), ("participantIds", GuidArraySchema(20))], ["projectId", "title", "startsAt"]) },
        new() { Name = "tasks.update", RequiresConfirmation = true, Description = "Prepara el cambio de una tarea para que el usuario lo confirme: reprogramar (startDate, dueDate), cambiar prioridad o título, o reasignar (developerId o responsibleUserId de people.list). Solo se envían los campos que cambian. Requiere taskId de tasks.list.", RequiredPermissions = [PermissionCodes.TasksRead, PermissionCodes.TasksWrite], Parameters = ObjectSchema(
            [("taskId", GuidSchema()), ("title", StringSchema(200)), ("startDate", DateSchema()), ("dueDate", DateSchema()), ("priority", EnumSchema(Priorities)),
             ("developerId", GuidSchema()), ("responsibleUserId", GuidSchema())], ["taskId"]) },
        new() { Name = "meetings.update", RequiresConfirmation = true, Description = "Prepara el cambio de una reunión para que el usuario lo confirme: moverla (startsAt en hora local de Buenos Aires yyyy-MM-ddTHH:mm; si no se indica fin se conserva la duración), cambiar la duración, el título o la ubicación. Requiere meetingId de meetings.list.", RequiredPermissions = [PermissionCodes.MeetingsRead, PermissionCodes.MeetingsWrite], Parameters = ObjectSchema(
            [("meetingId", GuidSchema()), ("startsAt", LocalDateTimeSchema()), ("endsAt", LocalDateTimeSchema()), ("durationMinutes", IntegerSchema(15, 480)), ("title", StringSchema(200)), ("location", StringSchema(300))], ["meetingId"]) },
        new() { Name = "meetings.cancel", RequiresConfirmation = true, Description = "Prepara la cancelación de una reunión para que el usuario la confirme. Requiere meetingId de meetings.list.", RequiredPermissions = [PermissionCodes.MeetingsRead, PermissionCodes.MeetingsCancel], Parameters = ObjectSchema(
            [("meetingId", GuidSchema())], ["meetingId"]) },
        new() { Name = "finance.create_movement", RequiresConfirmation = true, Description = "Prepara el registro de un ingreso o gasto operativo de la empresa para que el usuario lo confirme. Requiere type, amount, currency, description y category (nombre de la categoría; si no sabés cuál, enviá la más parecida y la herramienta devuelve las opciones). settled=true si ya se cobró o pagó (con fecha date, no futura); si no, queda pendiente con dueDate opcional.", RequiredPermissions = [PermissionCodes.FinancesRead, PermissionCodes.FinancesWrite], Parameters = ObjectSchema(
            [("type", EnumSchema(MovementTypes)), ("amount", NumberSchema(0.01m, 1_000_000_000m)), ("currency", EnumSchema(CurrencyOptions)), ("description", StringSchema(300)), ("category", StringSchema(100)),
             ("date", DateSchema()), ("settled", BooleanSchema()), ("dueDate", DateSchema()), ("clientId", GuidSchema()), ("projectId", GuidSchema())], ["type", "amount", "currency", "description"]) },
        new() { Name = "clients.create_prospect", RequiresConfirmation = true, Description = "Prepara el alta de un cliente en estado Prospecto para que el usuario la confirme. Requiere el nombre comercial; serviceType opcional.", RequiredPermissions = [PermissionCodes.ClientsRead, PermissionCodes.ClientsWrite], Parameters = ObjectSchema(
            [("name", StringSchema(160)), ("serviceType", StringSchema(120))], ["name"]) },
        new() { Name = "preferences.update", RequiresConfirmation = true, Description = "Prepara guardar preferencias personales del usuario para que las confirme: moneda que se muestra primero, duración por defecto de reuniones, prioridad por defecto de tareas, estilo de respuesta (breve o detallado), alcance por defecto (todo o mio) e indicaciones libres. Solo enviá lo que cambia.", RequiredPermissions = [], Parameters = ObjectSchema(
            [("preferredCurrency", EnumSchema(CurrencyOptions)), ("defaultMeetingMinutes", IntegerSchema(15, 480)), ("defaultTaskPriority", EnumSchema(Priorities)),
             ("responseStyle", EnumSchema(["breve", "detallado"])), ("defaultScope", EnumSchema(DayScopes)), ("notes", StringSchema(500)), ("clearNotes", BooleanSchema())], []) }
    ];

    public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) =>
        Definitions.Where(definition => CanUse(actor, definition)).ToArray();

    // Ya no hace falta ser Administrador: alcanza con ai.office.use y los permisos de dominio de cada herramienta.
    // Los datos que ve cada herramienta se recortan además según el rol (por ejemplo, un desarrollador solo ve lo suyo).
    private static bool CanUse(AiExecutionContext actor, AiToolDefinitionDto definition) =>
        actor.Permissions.Contains(PermissionCodes.AiOfficeUse)
        && definition.RequiredPermissions.All(actor.Permissions.Contains)
        && (definition.AnyOfPermissions.Count == 0 || definition.AnyOfPermissions.Any(actor.Permissions.Contains));

    public async Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid expectedConfigurationVersion, string traceId, string toolName, JsonObject arguments, CancellationToken cancellationToken)
    {
        var definition = Definitions.SingleOrDefault(x => x.Name == toolName);
        if (definition is null)
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "tool_not_allowlisted", cancellationToken);
            throw new AiToolRejectedException("La herramienta solicitada no está habilitada.");
        }

        if (!CanUse(actor, definition))
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "permission_denied", cancellationToken);
            throw new AiToolRejectedException("No tienes permisos para consultar este dominio.");
        }

        if (!await sessionValidator.IsValidAsync(actor.UserId, actor.SessionVersion, cancellationToken))
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "session_revoked", cancellationToken);
            throw new UnauthorizedAccessException("La sesión dejó de estar vigente.");
        }

        var configuration = await providerConfiguration.GetAsync(cancellationToken);
        if (configuration.Version != expectedConfigurationVersion || !configuration.IsConnectionVerified || !configuration.LiveDataApproved)
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "live_data_approval_revoked", cancellationToken);
            throw new AiLiveDataApprovalRequiredException();
        }

        try
        {
            ValidateProperties(arguments, definition.Parameters);
        }
        catch (AiToolRejectedException)
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "invalid_arguments", cancellationToken);
            throw;
        }
        var now = DateTime.UtcNow;
        if (definition.RequiresConfirmation)
        {
            // Las acciones no escriben: validan, resuelven referencias y devuelven una propuesta para confirmar.
            var proposal = await RunGuardedAsync(actor, runId, toolName, () => PrepareActionAsync(toolName, actor, arguments, now, cancellationToken), cancellationToken);
            await WriteAuditAsync(actor, runId, toolName, "Proposed", "pending_confirmation", cancellationToken);
            var proposalData = new JsonObject
            {
                ["status"] = "pending_confirmation",
                ["title"] = proposal.Title,
                ["summary"] = proposal.Summary,
                ["details"] = JsonSerializer.SerializeToNode(proposal.Details.Select(x => new { x.Label, x.Value })),
                ["instructions"] = "La acción NO se ejecutó todavía. Avisale al usuario que la dejaste lista y que la confirme con el botón Confirmar de la tarjeta.",
                ["traceId"] = traceId
            };
            return new AiToolExecutionResultDto { Data = proposalData, Proposal = proposal };
        }

        var (data, source) = await RunGuardedAsync(actor, runId, toolName, async () =>
            toolName switch
            {
                "dashboard.get_snapshot" => await DashboardAsync(arguments, now, cancellationToken),
                "finance.get_summary" => await FinanceAsync(arguments, now, traceId, cancellationToken),
                "projects.list" => await ProjectsAsync(arguments, now, cancellationToken),
                "tasks.list" => await TasksAsync(actor, arguments, now, cancellationToken),
                "clients.list" => await ClientsAsync(arguments, now, cancellationToken),
                "meetings.list" => await MeetingsAsync(actor, arguments, now, cancellationToken),
                "people.list" => await PeopleAsync(arguments, now, cancellationToken),
                "finance.list_movements" => await FinanceMovementsAsync(arguments, now, cancellationToken),
                "finance.list_pending" => await FinancePendingAsync(arguments, now, cancellationToken),
                "finance.compare_months" => await FinanceCompareAsync(arguments, now, cancellationToken),
                "my_day.get" => await MyDayAsync(actor, arguments, now, cancellationToken),
                "projects.get" => await ProjectDetailAsync(actor, arguments, now, cancellationToken),
                "clients.get" => await ClientDetailAsync(actor, arguments, now, cancellationToken),
                _ => throw new AiToolRejectedException("La herramienta solicitada no está habilitada.")
            }, cancellationToken);

        await WriteAuditAsync(actor, runId, toolName, "Allowed", "completed", cancellationToken);
        data["traceId"] = traceId;
        return new AiToolExecutionResultDto { Data = data, Source = source };
    }

    public async Task<AiActionOutcomeDto> ExecuteConfirmedActionAsync(AiExecutionContext actor, Guid actionId, string toolName, JsonObject arguments, CancellationToken cancellationToken)
    {
        var definition = Definitions.SingleOrDefault(x => x.Name == toolName && x.RequiresConfirmation);
        if (definition is null)
        {
            await WriteAuditAsync(actor, actionId, toolName, "Denied", "tool_not_allowlisted", cancellationToken);
            throw new AiToolRejectedException("La acción solicitada no está habilitada.");
        }

        if (!CanUse(actor, definition))
        {
            await WriteAuditAsync(actor, actionId, toolName, "Denied", "permission_denied", cancellationToken);
            throw new AiToolRejectedException("No tenés permisos para realizar esta acción.");
        }

        if (!await sessionValidator.IsValidAsync(actor.UserId, actor.SessionVersion, cancellationToken))
        {
            await WriteAuditAsync(actor, actionId, toolName, "Denied", "session_revoked", cancellationToken);
            throw new UnauthorizedAccessException("La sesión dejó de estar vigente.");
        }

        var outcome = await RunGuardedAsync(actor, actionId, toolName, async () =>
        {
            ValidateProperties(arguments, definition.Parameters);
            // Se vuelve a validar todo: entre la propuesta y la confirmación pudo cambiar el proyecto, la tarea o las personas.
            var prepared = await PrepareActionAsync(toolName, actor, arguments, DateTime.UtcNow, cancellationToken);
            return await ExecuteActionAsync(actor, actionId, prepared, cancellationToken);
        }, cancellationToken);

        await WriteAuditAsync(actor, actionId, toolName, "Allowed", "executed", cancellationToken);
        return outcome;
    }

    private async Task<T> RunGuardedAsync<T>(AiExecutionContext actor, Guid runId, string toolName, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (AiToolRejectedException)
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "invalid_arguments", cancellationToken);
            throw;
        }
        catch (Exception exception) when (IsDomainRejection(exception))
        {
            // Validaciones de los servicios de dominio: se devuelven como error de herramienta.
            await WriteAuditAsync(actor, runId, toolName, "Denied", "domain_rejected", cancellationToken);
            throw new AiToolRejectedException(exception.Message);
        }
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> PeopleAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var search = GetString(args, "search", 100)?.Trim();
        var pageSize = GetPageSize(args);
        var users = dbContext.Users.AsNoTracking().Where(x => x.Activo);
        var developers = dbContext.Developers.AsNoTracking().Where(x => x.Activo);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = search.ToLower();
            users = users.Where(x => x.FullName.ToLower().Contains(pattern));
            developers = developers.Where(x => x.FullName.ToLower().Contains(pattern));
        }
        var userItems = await users.OrderBy(x => x.FullName).Take(pageSize).Select(x => new { x.Id, Name = x.FullName }).ToListAsync(ct);
        var developerItems = await developers.OrderBy(x => x.FullName).Take(pageSize).Select(x => new { x.Id, Name = x.FullName }).ToListAsync(ct);
        var output = new { users = userItems, developers = developerItems };
        return (Wrap("people.list", now, new { search, pageSize }, output, [], null, null), Source("people.list", "Kodvian.Core.Application.Team", now, null, null, null, null));
    }

    private Task<AiActionProposalDto> PrepareActionAsync(string toolName, AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct) => toolName switch
    {
        "tasks.create" => PrepareTaskCreateAsync(args, ct),
        "tasks.update_status" => PrepareTaskStatusAsync(actor, args, ct),
        "meetings.create" => PrepareMeetingCreateAsync(actor, args, ct),
        _ => PrepareExtendedActionAsync(toolName, actor, args, now, ct)
    };

    private async Task<AiActionProposalDto> PrepareTaskCreateAsync(JsonObject args, CancellationToken ct)
    {
        var projectId = GetGuid(args, "projectId") ?? throw new AiToolRejectedException("Falta el proyecto de la tarea.");
        var title = GetString(args, "title", 200)?.Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new AiToolRejectedException("Falta el título de la tarea.");
        var description = GetString(args, "description", 2000)?.Trim();
        var startDate = GetDate(args, "startDate");
        var dueDate = GetDate(args, "dueDate");
        if (startDate.HasValue && dueDate.HasValue && dueDate < startDate)
            throw new AiToolRejectedException("La fecha de vencimiento no puede ser anterior a la fecha de inicio.");
        var priority = NormalizeOption(GetEnum(args, "priority", Priorities), Priorities) ?? "Media";
        var status = NormalizeOption(GetEnum(args, "status", NewTaskStatuses), NewTaskStatuses) ?? "Pendiente";
        var developerId = GetGuid(args, "developerId");
        var responsibleUserId = GetGuid(args, "responsibleUserId");
        if (developerId.HasValue && responsibleUserId.HasValue)
            throw new AiToolRejectedException("Asigná la tarea a un desarrollador o a un usuario responsable, no a ambos.");

        var project = await dbContext.Projects.AsNoTracking().Where(x => x.Id == projectId && x.Activo).Select(x => new { x.Nombre }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("El proyecto indicado no existe o no está activo. Buscalo con projects.list.");

        string? assignee = null;
        if (developerId.HasValue)
            assignee = await dbContext.Developers.AsNoTracking().Where(x => x.Id == developerId && x.Activo).Select(x => x.FullName).SingleOrDefaultAsync(ct)
                ?? throw new AiToolRejectedException("El desarrollador indicado no existe o no está activo. Buscalo con people.list.");
        else if (responsibleUserId.HasValue)
            assignee = await dbContext.Users.AsNoTracking().Where(x => x.Id == responsibleUserId && x.Activo).Select(x => x.FullName).SingleOrDefaultAsync(ct)
                ?? throw new AiToolRejectedException("El usuario responsable no existe o no está activo. Buscalo con people.list.");

        var details = new List<AiActionDetailDto> { Detail("Proyecto", project.Nombre), Detail("Prioridad", priority), Detail("Estado", StatusLabel(status)) };
        if (startDate.HasValue) details.Add(Detail("Inicio", FormatDate(startDate.Value)));
        if (dueDate.HasValue) details.Add(Detail("Vence", FormatDate(dueDate.Value)));
        if (assignee is not null) details.Add(Detail(developerId.HasValue ? "Desarrollador" : "Responsable", assignee));
        if (!string.IsNullOrWhiteSpace(description)) details.Add(Detail("Descripción", description.Length <= 160 ? description : description[..157] + "…"));

        var normalized = new JsonObject { ["projectId"] = projectId.ToString(), ["title"] = title, ["priority"] = priority, ["status"] = status };
        if (!string.IsNullOrWhiteSpace(description)) normalized["description"] = description;
        if (startDate.HasValue) normalized["startDate"] = startDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (dueDate.HasValue) normalized["dueDate"] = dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (developerId.HasValue) normalized["developerId"] = developerId.Value.ToString();
        if (responsibleUserId.HasValue) normalized["responsibleUserId"] = responsibleUserId.Value.ToString();

        return new AiActionProposalDto
        {
            ToolName = "tasks.create",
            Title = "Crear tarea",
            Summary = $"Crear la tarea «{title}» en {project.Nombre}" + (dueDate.HasValue ? $", con vencimiento el {FormatDate(dueDate.Value)}." : "."),
            Details = details,
            Arguments = normalized
        };
    }

    private async Task<AiActionProposalDto> PrepareTaskStatusAsync(AiExecutionContext actor, JsonObject args, CancellationToken ct)
    {
        var taskId = GetGuid(args, "taskId") ?? throw new AiToolRejectedException("Falta la tarea.");
        var status = NormalizeOption(GetEnum(args, "status", TaskStatuses), TaskStatuses) ?? throw new AiToolRejectedException("Falta el estado nuevo.");
        var task = await dbContext.Tasks.AsNoTracking().Where(x => x.Id == taskId && x.Activo)
            .Select(x => new { x.Titulo, x.Estado, x.DeveloperId, x.ResponsableId, ProjectName = x.Proyecto != null ? x.Proyecto.Nombre : null }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("La tarea indicada no existe o no está activa. Buscala con tasks.list.");
        if (!actor.Permissions.Contains(PermissionCodes.TasksWrite) && !await IsOwnTaskAsync(actor, task.DeveloperId, task.ResponsableId, ct))
            throw new AiToolRejectedException("Solo podés cambiar el estado de tus propias tareas.");
        var current = task.Estado.ToString();
        if (string.Equals(current, status, StringComparison.OrdinalIgnoreCase))
            throw new AiToolRejectedException($"La tarea ya está en estado {StatusLabel(status)}.");

        var details = new List<AiActionDetailDto> { Detail("Tarea", task.Titulo), Detail("Estado actual", StatusLabel(current)), Detail("Estado nuevo", StatusLabel(status)) };
        if (task.ProjectName is not null) details.Insert(1, Detail("Proyecto", task.ProjectName));
        return new AiActionProposalDto
        {
            ToolName = "tasks.update_status",
            Title = "Cambiar estado de tarea",
            Summary = $"Pasar la tarea «{task.Titulo}» de {StatusLabel(current)} a {StatusLabel(status)}.",
            Details = details,
            Arguments = new JsonObject { ["taskId"] = taskId.ToString(), ["status"] = status }
        };
    }

    private async Task<AiActionProposalDto> PrepareMeetingCreateAsync(AiExecutionContext actor, JsonObject args, CancellationToken ct)
    {
        var projectId = GetGuid(args, "projectId") ?? throw new AiToolRejectedException("Falta el proyecto de la reunión.");
        var title = GetString(args, "title", 200)?.Trim();
        if (string.IsNullOrWhiteSpace(title)) throw new AiToolRejectedException("Falta el título de la reunión.");
        var startsAt = GetLocalDateTime(args, "startsAt") ?? throw new AiToolRejectedException("Falta la fecha y hora de inicio.");
        var endsAtArgument = GetLocalDateTime(args, "endsAt");
        var duration = GetInt(args, "durationMinutes");
        if (duration is < 15 or > 480) throw new AiToolRejectedException("La duración debe estar entre 15 y 480 minutos.");
        var endsAt = endsAtArgument ?? startsAt.AddMinutes(duration ?? 60);
        if (endsAt <= startsAt) throw new AiToolRejectedException("La hora de finalización debe ser posterior al inicio.");
        if ((endsAt - startsAt).TotalHours > 12) throw new AiToolRejectedException("La reunión no puede durar más de 12 horas.");
        var description = GetString(args, "description", 2000)?.Trim();
        var location = GetString(args, "location", 300)?.Trim();
        var participantIds = GetGuidArray(args, "participantIds", 20);

        var project = await dbContext.Projects.AsNoTracking().Where(x => x.Id == projectId && x.Activo).Select(x => new { x.Nombre }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("El proyecto indicado no existe o no está activo. Buscalo con projects.list.");
        if (!IsAdministrator(actor))
        {
            // Fuera de Administración solo se agenda en proyectos a los que el usuario tiene acceso (igual que en Agenda).
            var lookups = await meetings.GetLookupsAsync(actor.UserId, false, true, ct);
            if (lookups.Projects.All(x => x.Id != projectId)) throw new AiToolRejectedException("No tenés acceso a ese proyecto para agendar reuniones.");
        }
        var participants = participantIds.Count == 0 ? [] : await dbContext.Users.AsNoTracking()
            .Where(x => participantIds.Contains(x.Id) && x.Activo).OrderBy(x => x.FullName).Select(x => x.FullName).ToListAsync(ct);
        if (participants.Count != participantIds.Count)
            throw new AiToolRejectedException("Uno o más participantes no existen o no están activos. Buscalos con people.list.");

        var details = new List<AiActionDetailDto>
        {
            Detail("Proyecto", project.Nombre),
            Detail("Cuándo", $"{startsAt.ToString("ddd d/M HH:mm", Spanish)} a {endsAt:HH:mm}" + (endsAt.Date != startsAt.Date ? $" del {endsAt.ToString("d/M", Spanish)}" : string.Empty))
        };
        if (participants.Count > 0) details.Add(Detail("Participantes", string.Join(", ", participants)));
        if (!string.IsNullOrWhiteSpace(location)) details.Add(Detail("Ubicación", location));
        if (!string.IsNullOrWhiteSpace(description)) details.Add(Detail("Descripción", description.Length <= 160 ? description : description[..157] + "…"));

        var normalized = new JsonObject
        {
            ["projectId"] = projectId.ToString(),
            ["title"] = title,
            ["startsAt"] = startsAt.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
            ["endsAt"] = endsAt.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(description)) normalized["description"] = description;
        if (!string.IsNullOrWhiteSpace(location)) normalized["location"] = location;
        if (participantIds.Count > 0) normalized["participantIds"] = new JsonArray(participantIds.Select(x => (JsonNode?)JsonValue.Create(x.ToString())).ToArray());

        return new AiActionProposalDto
        {
            ToolName = "meetings.create",
            Title = "Agendar reunión",
            Summary = $"Agendar «{title}» ({project.Nombre}) el {startsAt.ToString("dddd d 'de' MMMM 'a las' HH:mm", Spanish)}.",
            Details = details,
            Arguments = normalized
        };
    }

    private async Task<AiActionOutcomeDto> ExecuteActionAsync(AiExecutionContext actor, Guid actionId, AiActionProposalDto prepared, CancellationToken ct)
    {
        var args = prepared.Arguments;
        var now = DateTime.UtcNow;
        switch (prepared.ToolName)
        {
            case "tasks.create":
            {
                var created = await tasks.CreateAsync(actor.UserId, new TaskUpsertRequestDto
                {
                    ProjectId = GetGuid(args, "projectId")!.Value,
                    Title = GetString(args, "title", 200)!,
                    Description = GetString(args, "description", 2000),
                    StartDate = GetDate(args, "startDate"),
                    DueDate = GetDate(args, "dueDate"),
                    Priority = GetString(args, "priority", 20) ?? "Media",
                    Status = GetString(args, "status", 20) ?? "Pendiente",
                    DeveloperId = GetGuid(args, "developerId"),
                    ResponsibleId = GetGuid(args, "responsibleUserId")
                }, ct);
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, creé la tarea «{created.Title}» en {created.ProjectName}.",
                    Link = "/tareas",
                    Source = Source("tasks.create", "Kodvian.Core.Application.Tasks", now, null, null, null, "/tareas"),
                    EntityId = created.Id,
                    UndoData = new JsonObject()
                };
            }
            case "tasks.update_status":
            {
                var taskId = GetGuid(args, "taskId")!.Value;
                var previous = await dbContext.Tasks.AsNoTracking().Where(x => x.Id == taskId).Select(x => new { x.OrdenKanban, x.Estado }).SingleOrDefaultAsync(ct)
                    ?? throw new AiToolRejectedException("La tarea ya no existe.");
                var kanbanOrder = previous.OrdenKanban;
                var status = GetString(args, "status", 20)!;
                var updated = await tasks.UpdateStatusAsync(taskId, new TaskStatusUpdateRequestDto { Status = status, KanbanOrder = kanbanOrder }, ct)
                    ?? throw new AiToolRejectedException("La tarea ya no existe.");
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, la tarea «{updated.Title}» quedó en {StatusLabel(status)}.",
                    Link = "/tareas",
                    Source = Source("tasks.update_status", "Kodvian.Core.Application.Tasks", now, null, null, null, "/tareas"),
                    EntityId = taskId,
                    UndoData = new JsonObject { ["previousStatus"] = previous.Estado.ToString(), ["previousKanbanOrder"] = previous.OrdenKanban, ["newStatus"] = status }
                };
            }
            case "meetings.create":
            {
                var startsAt = GetLocalDateTime(args, "startsAt")!.Value;
                var endsAt = GetLocalDateTime(args, "endsAt")!.Value;
                var created = await meetings.CreateAsync(actor.UserId, IsAdministrator(actor), new MeetingUpsertRequestDto
                {
                    ProjectId = GetGuid(args, "projectId")!.Value,
                    Title = GetString(args, "title", 200)!,
                    Description = GetString(args, "description", 2000),
                    Location = GetString(args, "location", 300),
                    StartsAt = TimeZoneInfo.ConvertTimeToUtc(startsAt, BuenosAiresTimeZone),
                    EndsAt = TimeZoneInfo.ConvertTimeToUtc(endsAt, BuenosAiresTimeZone),
                    ParticipantIds = GetGuidArray(args, "participantIds", 20)
                }, ct);
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, agendé «{created.Title}» para el {startsAt.ToString("dddd d 'de' MMMM 'a las' HH:mm", Spanish)}.",
                    Link = "/agenda",
                    Source = Source("meetings.create", "Kodvian.Core.Application.Meetings", now, null, null, null, "/agenda"),
                    EntityId = created.Id,
                    UndoData = new JsonObject()
                };
            }
            default:
                return await ExecuteExtendedActionAsync(actor, actionId, prepared, ct);
        }
    }

    private static AiActionDetailDto Detail(string label, string value) => new() { Label = label, Value = value };

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string StatusLabel(string status) => status switch
    {
        "EnCurso" => "En curso",
        _ => status
    };

    private static string? NormalizeOption(string? value, string[] options) =>
        value is null ? null : options.First(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    private async Task<(JsonObject, AiSourceReferenceDto)> DashboardAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var year = GetInt(args, "year") ?? TimeZoneInfo.ConvertTimeFromUtc(now, BuenosAiresTimeZone).Year;
        var month = GetInt(args, "month") ?? TimeZoneInfo.ConvertTimeFromUtc(now, BuenosAiresTimeZone).Month;
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new AiToolRejectedException("El período solicitado no es válido.");
        var result = await dashboard.GetOverviewAsync(new DashboardOverviewRequestDto { Year = year, Month = month }, ct);
        var output = new
        {
            period = new { year, month },
            kpis = new
            {
                result.Kpis.ActiveClients,
                result.Kpis.ProjectsInProgress,
                result.Kpis.OverdueTasks,
                result.Kpis.TasksForToday,
                currencies = result.Finance.Currencies.Select(x => new { x.Currency, x.Income, x.Expense, x.Result, x.PendingIncome, x.PendingExpense })
            },
            priorityTasks = result.PriorityTasks.Select(x => new { x.Id, x.Title, x.ProjectName, x.Status, x.Priority, x.DueDate }),
            upcomingCollections = result.UpcomingCollections.Select(x => new { x.Id, x.Currency, x.DueDate, x.Status })
        };
        return (Wrap("dashboard.get_snapshot", now, new { year, month }, output, [], null, null), Source("dashboard.get_snapshot", "Kodvian.Core.Application.Dashboard", now, null, null, null, "/dashboard"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> FinanceAsync(JsonObject args, DateTime now, string traceId, CancellationToken ct)
    {
        var (from, to) = ResolvePeriod(args, "from", "to", now);
        var result = await finance.GetPeriodSummaryAsync(from, to, ct);
        var output = new { from = result.From, to = result.To, currencies = result.Currencies.Select(x => new { x.Currency, x.Income, x.Expense, x.Result, x.PendingIncome, x.PendingExpense }) };
        return (Wrap("finance.get_summary", now, new { from, to }, output, [], null, null), Source("finance.get_summary", "Kodvian.Core.Application.Finances", now, from, to, null, "/vision-financiera"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> ProjectsAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var request = new ProjectListRequestDto
        {
            Search = GetString(args, "search", 100),
            Status = GetEnum(args, "status", ProjectStatuses),
            ActiveOnly = true,
            PageSize = GetPageSize(args)
        };
        var page = await projects.GetPagedAsync(request, ct);
        var output = page.Items.Select(x => new { x.Id, x.Name, x.ClientName, x.Status, x.Priority, x.StartDate, x.EstimatedDeliveryDate, x.ProgressPercentage }).ToArray();
        var warnings = page.TotalCount > page.Items.Count ? new[] { "Se muestran los primeros resultados; limita la consulta para ver más." } : [];
        return (Wrap("projects.list", now, new { request.Search, request.Status, page.PageSize }, output, warnings, null, null), Source("projects.list", "Kodvian.Core.Application.Projects", now, null, null, null, "/proyectos"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> TasksAsync(AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct)
    {
        var (dueDateFrom, dueDateTo) = ResolveOptionalBoundedRange(args, "dueDateFrom", "dueDateTo");
        var request = new TaskListRequestDto
        {
            Search = GetString(args, "search", 120),
            Status = GetEnum(args, "status", TaskStatuses),
            DueDateFrom = dueDateFrom,
            DueDateTo = dueDateTo,
            ActiveOnly = true,
            PageSize = GetPageSize(args)
        };
        var projectId = GetGuid(args, "projectId");
        if (projectId.HasValue) request.ProjectId = projectId;
        if (!actor.Permissions.Contains(PermissionCodes.TasksRead))
        {
            // Sin acceso al tablero completo (por ejemplo, un desarrollador): solo sus tareas.
            var developerId = await dbContext.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.DeveloperId).SingleOrDefaultAsync(ct);
            if (developerId.HasValue) request.DeveloperId = developerId; else request.ResponsibleId = actor.UserId;
        }
        if (request.DueDateFrom.HasValue && request.DueDateTo.HasValue && request.DueDateFrom > request.DueDateTo)
            throw new AiToolRejectedException("El rango de vencimientos no es válido.");
        var page = await tasks.GetPagedAsync(request, ct);
        var output = page.Items.Select(x => new { x.Id, x.Title, x.ProjectId, x.ProjectName, x.Status, x.Priority, x.DueDate }).ToArray();
        var warnings = page.TotalCount > page.Items.Count ? new[] { "Se muestran los primeros resultados; limita la consulta para ver más." } : [];
        return (Wrap("tasks.list", now, new { request.Search, request.Status, request.DueDateFrom, request.DueDateTo, request.ProjectId, page.PageSize }, output, warnings, null, null), Source("tasks.list", "Kodvian.Core.Application.Tasks", now, request.DueDateFrom, request.DueDateTo, null, "/tareas"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> ClientsAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var request = new ClientListRequestDto
        {
            Search = GetString(args, "search", 100),
            Status = GetEnum(args, "status", ClientStatuses),
            ActiveOnly = true,
            PageSize = GetPageSize(args)
        };
        var page = await clients.GetPagedAsync(request, ct);
        var output = page.Items.Select(x => new { x.Id, x.CommercialName, x.Status, x.IsActive }).ToArray();
        var warnings = page.TotalCount > page.Items.Count ? new[] { "Se muestran los primeros resultados; limita la consulta para ver más." } : [];
        return (Wrap("clients.list", now, new { request.Search, request.Status, page.PageSize }, output, warnings, null, null), Source("clients.list", "Kodvian.Core.Application.Clients", now, null, null, null, "/clientes"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> MeetingsAsync(AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct)
    {
        var (fromDate, toDate) = ResolveUpcomingPeriod(args, "from", "to", now, 7);
        var request = new MeetingListRequestDto
        {
            From = BuenosAiresStartUtc(fromDate),
            To = BuenosAiresEndUtc(toDate),
            ProjectId = GetGuid(args, "projectId"),
            Status = GetEnum(args, "status", MeetingStatuses),
            ActiveOnly = true,
            PageSize = GetPageSize(args)
        };
        var page = await meetings.GetPagedAsync(actor.UserId, IsAdministrator(actor), request, ct);
        var output = page.Items.Select(x => new { x.Id, x.Title, x.ProjectId, x.ProjectName, x.StartsAt, x.EndsAt, x.Status, ParticipantCount = x.Participants.Count }).ToArray();
        var warnings = page.TotalCount > page.Items.Count ? new[] { "Se muestran las primeras reuniones del período." } : [];
        return (Wrap("meetings.list", now, new { fromDate, toDate, request.ProjectId, page.PageSize }, output, warnings, null, null), Source("meetings.list", "Kodvian.Core.Application.Meetings", now, fromDate, toDate, null, "/agenda"));
    }

    private async Task WriteAuditAsync(AiExecutionContext actor, Guid runId, string toolName, string decision, string reason, CancellationToken ct)
    {
        dbContext.AiAuditEvents.Add(new AiAuditEvent
        {
            ActorUserId = actor.UserId,
            RunId = runId,
            ToolName = toolName.Length <= 100 ? toolName : toolName[..100],
            Decision = decision,
            ReasonCode = reason,
            CreatedAtUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(ct);
    }

    private static JsonObject Wrap(string tool, DateTime asOf, object scope, object result, string[] warnings, string? traceId, string? currency) => new()
    {
        ["source"] = tool,
        ["asOfUtc"] = asOf,
        ["scope"] = JsonSerializer.SerializeToNode(scope),
        ["result"] = JsonSerializer.SerializeToNode(result),
        ["warnings"] = JsonSerializer.SerializeToNode(warnings),
        ["traceId"] = traceId,
        ["currency"] = currency
    };

    private static AiSourceReferenceDto Source(string tool, string source, DateTime asOf, DateOnly? from, DateOnly? to, string? currency, string? link) => new()
    {
        ToolName = tool, Source = source, AsOfUtc = asOf, From = from, To = to, Currency = currency, Link = link
    };

    private static (DateOnly From, DateOnly To) ResolvePeriod(JsonObject args, string fromProperty, string toProperty, DateTime utcNow)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, BuenosAiresTimeZone));
        var from = GetDate(args, fromProperty);
        var to = GetDate(args, toProperty);
        var actualTo = to ?? localToday;
        var actualFrom = from ?? actualTo.AddDays(-29);
        if (actualFrom > actualTo) throw new AiToolRejectedException("El inicio del período no puede ser posterior al fin.");
        if (actualTo.DayNumber - actualFrom.DayNumber > 89) throw new AiToolRejectedException("El período máximo de consulta es de 90 días.");
        return (actualFrom, actualTo);
    }

    // Para agenda: sin fechas, los próximos días desde hoy (hora de Buenos Aires).
    private static (DateOnly From, DateOnly To) ResolveUpcomingPeriod(JsonObject args, string fromProperty, string toProperty, DateTime utcNow, int defaultDays)
    {
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, BuenosAiresTimeZone));
        var from = GetDate(args, fromProperty);
        var to = GetDate(args, toProperty);
        var actualFrom = from ?? (to.HasValue && to.Value < localToday ? to.Value.AddDays(-(defaultDays - 1)) : localToday);
        var actualTo = to ?? actualFrom.AddDays(defaultDays - 1);
        if (actualFrom > actualTo) throw new AiToolRejectedException("El inicio del período no puede ser posterior al fin.");
        if (actualTo.DayNumber - actualFrom.DayNumber > 89) throw new AiToolRejectedException("El período máximo de consulta es de 90 días.");
        return (actualFrom, actualTo);
    }

    private static bool IsDomainRejection(Exception exception) =>
        exception is ArgumentException or KeyNotFoundException or UnauthorizedAccessException;

    private static (DateOnly? From, DateOnly? To) ResolveOptionalBoundedRange(JsonObject args, string fromProperty, string toProperty)
    {
        var from = GetDate(args, fromProperty);
        var to = GetDate(args, toProperty);
        if (!from.HasValue && !to.HasValue) return (null, null);
        if (!from.HasValue) from = to!.Value.AddDays(-89);
        if (!to.HasValue) to = from.Value.AddDays(89);
        if (from > to || to.Value.DayNumber - from.Value.DayNumber > 89)
            throw new AiToolRejectedException("El rango de fechas no puede superar los 90 días.");
        return (from, to);
    }

    private static JsonObject ObjectSchema((string Name, JsonObject Schema)[] properties, string[] required)
    {
        var propertyObject = new JsonObject();
        foreach (var property in properties) propertyObject[property.Name] = property.Schema;
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = propertyObject,
            ["required"] = JsonSerializer.SerializeToNode(required)
        };
    }

    private static JsonObject StringSchema(int maxLength) => new() { ["type"] = "string", ["maxLength"] = maxLength };
    private static JsonObject IntegerSchema(int min, int max) => new() { ["type"] = "integer", ["minimum"] = min, ["maximum"] = max };
    private static JsonObject GuidSchema() => new() { ["type"] = "string", ["format"] = "uuid" };
    private static JsonObject DateSchema() => new() { ["type"] = "string", ["format"] = "date" };
    private static JsonObject LocalDateTimeSchema() => new() { ["type"] = "string", ["pattern"] = LocalDateTimePattern, ["description"] = "Hora local de Buenos Aires, formato yyyy-MM-ddTHH:mm" };
    private static JsonObject GuidArraySchema(int maxItems) => new() { ["type"] = "array", ["items"] = GuidSchema(), ["maxItems"] = maxItems };

    private static DateTime? GetLocalDateTime(JsonObject args, string property)
    {
        var value = GetString(args, property, 19);
        if (value is null) return null;
        return DateTime.TryParseExact(value, ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified)
            : throw new AiToolRejectedException($"La fecha y hora {property} no es válida; usá yyyy-MM-ddTHH:mm (hora de Buenos Aires).");
    }

    private static IReadOnlyCollection<Guid> GetGuidArray(JsonObject args, string property, int maxItems)
    {
        if (!args.TryGetPropertyValue(property, out var node) || node is null) return [];
        if (node is not JsonArray array || array.Count > maxItems) throw new AiToolRejectedException($"El parámetro {property} no es válido.");
        var result = new List<Guid>();
        foreach (var item in array)
        {
            if (item is not JsonValue value || !value.TryGetValue<string>(out var text) || !Guid.TryParse(text, out var id) || id == Guid.Empty)
                throw new AiToolRejectedException($"El parámetro {property} no es válido.");
            if (!result.Contains(id)) result.Add(id);
        }
        return result;
    }

    private static JsonObject EnumSchema(string[] options) => new() { ["type"] = "string", ["enum"] = JsonSerializer.SerializeToNode(options) };

    private static bool IsAdministrator(AiExecutionContext actor) => actor.Roles.Contains(RoleNames.Administrator);

    /// <summary>La tarea está asignada al usuario (como desarrollador o como responsable).</summary>
    private async Task<bool> IsOwnTaskAsync(AiExecutionContext actor, Guid? taskDeveloperId, Guid? taskResponsibleId, CancellationToken ct)
    {
        if (taskResponsibleId == actor.UserId) return true;
        if (!taskDeveloperId.HasValue) return false;
        var developerId = await dbContext.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.DeveloperId).SingleOrDefaultAsync(ct);
        return developerId == taskDeveloperId;
    }

    private static int GetPageSize(JsonObject args)
    {
        var value = GetInt(args, "pageSize") ?? MaxPageSize;
        if (value is < 1 or > MaxPageSize) throw new AiToolRejectedException($"La cantidad máxima por consulta es {MaxPageSize}.");
        return value;
    }

    private static int? GetInt(JsonObject args, string property)
    {
        if (!args.TryGetPropertyValue(property, out var value) || value is null) return null;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<int>(out var number)) return number;
        throw new AiToolRejectedException($"El parámetro {property} no es válido.");
    }

    private static string? GetString(JsonObject args, string property, int maxLength)
    {
        if (!args.TryGetPropertyValue(property, out var node) || node is null) return null;
        if (node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var value) || value is null || value.Length > maxLength)
            throw new AiToolRejectedException($"El parámetro {property} no es válido.");
        return value;
    }

    private static string? GetEnum(JsonObject args, string property, string[] values)
    {
        var value = GetString(args, property, 40);
        if (value is not null && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new AiToolRejectedException($"El valor de {property} no es válido.");
        return value;
    }

    private static Guid? GetGuid(JsonObject args, string property)
    {
        var value = GetString(args, property, 36);
        if (value is null) return null;
        return Guid.TryParse(value, out var parsed) ? parsed : throw new AiToolRejectedException($"El identificador {property} no es válido.");
    }

    private static DateOnly? GetDate(JsonObject args, string property)
    {
        var value = GetString(args, property, 10);
        if (value is null) return null;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed : throw new AiToolRejectedException($"La fecha {property} no es válida.");
    }

    private static void ValidateProperties(JsonObject arguments, JsonObject schema)
    {
        var props = schema["properties"]?.AsObject() ?? [];
        var required = schema["required"]?.AsArray().OfType<JsonValue>().Select(x => x.GetValue<string>()).ToHashSet(StringComparer.Ordinal) ?? [];
        if (arguments.Any(property => !props.ContainsKey(property.Key)) || required.Any(key => !arguments.ContainsKey(key)))
            throw new AiToolRejectedException("Los parámetros de la herramienta no son válidos.");
    }

    private static DateTime BuenosAiresStartUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), BuenosAiresTimeZone);

    private static DateTime BuenosAiresEndUtc(DateOnly date) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified), BuenosAiresTimeZone);

    private static TimeZoneInfo ResolveBuenosAiresTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time"); }
    }
}
