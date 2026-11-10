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

public sealed class AiToolCatalog(
    IClientService clients,
    IProjectService projects,
    ITaskService tasks,
    IMeetingService meetings,
    IFinanceOverviewService finance,
    IDashboardService dashboard,
    IAiProviderConfigurationService providerConfiguration,
    ISessionValidator sessionValidator,
    KodvianDbContext dbContext) : IAiToolExecutor
{
    private const int MaxPageSize = 25;
    private static readonly string[] TaskStatuses = ["Pendiente", "EnCurso", "Bloqueada", "Finalizada", "Cancelada"];
    private static readonly string[] ProjectStatuses = ["Planificacion", "EnCurso", "Pausado", "Finalizado", "Cancelado", "Presupuestado"];
    private static readonly string[] ClientStatuses = ["Prospecto", "Activo", "Pausado", "Finalizado", "Presupuestado"];
    private static readonly string[] MeetingStatuses = Enum.GetNames<MeetingStatus>();
    private static readonly TimeZoneInfo BuenosAiresTimeZone = ResolveBuenosAiresTimeZone();

    private static readonly AiToolDefinitionDto[] Definitions =
    [
        new() { Name = "dashboard.get_snapshot", Description = "Consulta los indicadores ejecutivos del mes calendario indicado. La respuesta financiera viene separada por moneda.", RequiredPermissions = [PermissionCodes.DashboardRead], Parameters = ObjectSchema(
            [("year", IntegerSchema(2000, 2100)), ("month", IntegerSchema(1, 12))], []) },
        new() { Name = "finance.get_summary", Description = "Consulta ingresos, gastos y pendientes por moneda para un período de hasta 90 días. No mezcles ARS y USD.", RequiredPermissions = [PermissionCodes.FinancesRead], Parameters = ObjectSchema(
            [("from", DateSchema()), ("to", DateSchema())], []) },
        new() { Name = "projects.list", Description = "Lista proyectos visibles con campos operativos mínimos. Limita los resultados a la primera página.", RequiredPermissions = [PermissionCodes.ProjectsRead], Parameters = ObjectSchema(
            [("search", StringSchema(100)), ("status", EnumSchema(ProjectStatuses)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "tasks.list", Description = "Lista tareas visibles. Devuelve título, proyecto, estado, prioridad y vencimiento; no devuelve descripción ni horas.", RequiredPermissions = [PermissionCodes.TasksRead], Parameters = ObjectSchema(
            [("search", StringSchema(120)), ("projectId", GuidSchema()), ("status", EnumSchema(TaskStatuses)), ("dueDateFrom", DateSchema()), ("dueDateTo", DateSchema()), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "clients.list", Description = "Lista clientes visibles con nombre comercial y estado; excluye datos de contacto, montos y notas.", RequiredPermissions = [PermissionCodes.ClientsRead], Parameters = ObjectSchema(
            [("search", StringSchema(100)), ("status", EnumSchema(ClientStatuses)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) },
        new() { Name = "meetings.list", Description = "Lista reuniones visibles durante un período de hasta 90 días. Sin fechas devuelve los próximos 7 días (desde hoy). Para reuniones pasadas indicá from y to. No devuelve enlaces, ubicación ni nombres de participantes.", RequiredPermissions = [PermissionCodes.MeetingsRead], Parameters = ObjectSchema(
            [("from", DateSchema()), ("to", DateSchema()), ("projectId", GuidSchema()), ("status", EnumSchema(MeetingStatuses)), ("pageSize", IntegerSchema(1, MaxPageSize))], []) }
    ];

    public IReadOnlyCollection<AiToolDefinitionDto> GetAvailableTools(AiExecutionContext actor) =>
        IsAdministrator(actor) && actor.Permissions.Contains(PermissionCodes.AiOfficeUse)
            ? Definitions.Where(definition => definition.RequiredPermissions.All(actor.Permissions.Contains)).ToArray()
            : [];

    public async Task<AiToolExecutionResultDto> ExecuteAsync(AiExecutionContext actor, Guid runId, Guid expectedConfigurationVersion, string traceId, string toolName, JsonObject arguments, CancellationToken cancellationToken)
    {
        var definition = Definitions.SingleOrDefault(x => x.Name == toolName);
        if (definition is null)
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "tool_not_allowlisted", cancellationToken);
            throw new AiToolRejectedException("La herramienta solicitada no está habilitada.");
        }

        if (!IsAdministrator(actor) || !actor.Permissions.Contains(PermissionCodes.AiOfficeUse)
            || definition.RequiredPermissions.Any(permission => !actor.Permissions.Contains(permission)))
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
        JsonObject data;
        AiSourceReferenceDto source;
        try
        {
            (data, source) = toolName switch
            {
                "dashboard.get_snapshot" => await DashboardAsync(arguments, now, cancellationToken),
                "finance.get_summary" => await FinanceAsync(arguments, now, traceId, cancellationToken),
                "projects.list" => await ProjectsAsync(arguments, now, cancellationToken),
                "tasks.list" => await TasksAsync(arguments, now, cancellationToken),
                "clients.list" => await ClientsAsync(arguments, now, cancellationToken),
                "meetings.list" => await MeetingsAsync(actor, arguments, now, cancellationToken),
                _ => throw new AiToolRejectedException("La herramienta solicitada no está habilitada.")
            };
        }
        catch (AiToolRejectedException)
        {
            await WriteAuditAsync(actor, runId, toolName, "Denied", "invalid_arguments", cancellationToken);
            throw;
        }
        catch (Exception exception) when (IsDomainRejection(exception))
        {
            // Validaciones de los servicios de dominio: se devuelven al modelo como error de herramienta.
            await WriteAuditAsync(actor, runId, toolName, "Denied", "domain_rejected", cancellationToken);
            throw new AiToolRejectedException(exception.Message);
        }

        await WriteAuditAsync(actor, runId, toolName, "Allowed", "completed", cancellationToken);
        data["traceId"] = traceId;
        return new AiToolExecutionResultDto { Data = data, Source = source };
    }

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

    private async Task<(JsonObject, AiSourceReferenceDto)> TasksAsync(JsonObject args, DateTime now, CancellationToken ct)
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
        var page = await meetings.GetPagedAsync(actor.UserId, true, request, ct);
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
    private static JsonObject EnumSchema(string[] options) => new() { ["type"] = "string", ["enum"] = JsonSerializer.SerializeToNode(options) };

    private static bool IsAdministrator(AiExecutionContext actor) => actor.Roles.Contains(RoleNames.Administrator);

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
