using System.Globalization;
using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Clients.Requests;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Finances.Requests;
using Kodvian.Core.Application.Meetings.Requests;
using Kodvian.Core.Application.Tasks.Requests;
using Kodvian.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using DomainTaskStatus = Kodvian.Core.Domain.Enums.TaskStatus;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>
/// Acciones adicionales con confirmación (reprogramar o reasignar tareas, mover o cancelar reuniones,
/// registrar movimientos y crear prospectos) y el "deshacer" de las acciones que se pueden revertir con seguridad.
/// </summary>
public sealed partial class AiToolCatalog
{
    private Task<AiActionProposalDto> PrepareExtendedActionAsync(string toolName, AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct) => toolName switch
    {
        "tasks.update" => PrepareTaskUpdateAsync(args, ct),
        "meetings.update" => PrepareMeetingUpdateAsync(actor, args, ct),
        "meetings.cancel" => PrepareMeetingCancelAsync(actor, args, ct),
        "finance.create_movement" => PrepareMovementAsync(args, now, ct),
        "clients.create_prospect" => PrepareProspectAsync(args, ct),
        "preferences.update" => PreparePreferencesAsync(actor, args, ct),
        _ => throw new AiToolRejectedException("La acción solicitada no está habilitada.")
    };

    private async Task<AiActionProposalDto> PreparePreferencesAsync(AiExecutionContext actor, JsonObject args, CancellationToken ct)
    {
        var current = await AiPreferencesService.LoadAsync(dbContext, actor.UserId, ct);
        var clear = GetBool(args, "clearNotes") ?? false;
        var requested = new AiUserPreferencesDto
        {
            PreferredCurrency = GetString(args, "preferredCurrency", 3) ?? current.PreferredCurrency,
            DefaultMeetingMinutes = GetInt(args, "defaultMeetingMinutes") ?? current.DefaultMeetingMinutes,
            DefaultTaskPriority = GetString(args, "defaultTaskPriority", 20) ?? current.DefaultTaskPriority,
            ResponseStyle = GetString(args, "responseStyle", 20) ?? current.ResponseStyle,
            DefaultScope = GetString(args, "defaultScope", 10) ?? current.DefaultScope,
            Notes = clear ? null : GetString(args, "notes", AiPreferenceRules.MaxNotesLength) ?? current.Notes
        };
        AiUserPreferencesDto normalized;
        try { normalized = AiPreferenceRules.Normalize(requested); }
        catch (ArgumentException exception) { throw new AiToolRejectedException(exception.Message); }

        var details = new List<AiActionDetailDto>();
        void Changed(string label, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal)) details.Add(Detail(label, $"{before ?? "sin preferencia"} → {after ?? "sin preferencia"}"));
        }
        Changed("Moneda", current.PreferredCurrency, normalized.PreferredCurrency);
        Changed("Duración de reuniones", current.DefaultMeetingMinutes?.ToString(CultureInfo.InvariantCulture), normalized.DefaultMeetingMinutes?.ToString(CultureInfo.InvariantCulture));
        Changed("Prioridad de tareas", current.DefaultTaskPriority, normalized.DefaultTaskPriority);
        Changed("Estilo de respuesta", current.ResponseStyle, normalized.ResponseStyle);
        Changed("Alcance", current.DefaultScope, normalized.DefaultScope);
        Changed("Indicaciones", current.Notes, normalized.Notes);
        if (details.Count == 0) throw new AiToolRejectedException("Esas preferencias ya están guardadas.");

        var arguments = new JsonObject();
        if (normalized.PreferredCurrency is not null) arguments["preferredCurrency"] = normalized.PreferredCurrency;
        if (normalized.DefaultMeetingMinutes is not null) arguments["defaultMeetingMinutes"] = normalized.DefaultMeetingMinutes;
        if (normalized.DefaultTaskPriority is not null) arguments["defaultTaskPriority"] = normalized.DefaultTaskPriority;
        if (normalized.ResponseStyle is not null) arguments["responseStyle"] = normalized.ResponseStyle;
        if (normalized.DefaultScope is not null) arguments["defaultScope"] = normalized.DefaultScope;
        if (normalized.Notes is not null) arguments["notes"] = normalized.Notes;
        else arguments["clearNotes"] = true;
        return new AiActionProposalDto { ToolName = "preferences.update", Title = "Guardar preferencias", Summary = "Recordar estas preferencias para las próximas respuestas.", Details = details, Arguments = arguments };
    }

    private static JsonObject PreferencesSnapshot(AiUserPreferencesDto preferences) => new()
    {
        ["preferredCurrency"] = preferences.PreferredCurrency, ["defaultMeetingMinutes"] = preferences.DefaultMeetingMinutes,
        ["defaultTaskPriority"] = preferences.DefaultTaskPriority, ["responseStyle"] = preferences.ResponseStyle,
        ["defaultScope"] = preferences.DefaultScope, ["notes"] = preferences.Notes
    };

    private static AiUserPreferencesDto PreferencesFromSnapshot(JsonObject snapshot) => new()
    {
        PreferredCurrency = snapshot["preferredCurrency"]?.GetValue<string>(), DefaultMeetingMinutes = snapshot["defaultMeetingMinutes"]?.GetValue<int>(),
        DefaultTaskPriority = snapshot["defaultTaskPriority"]?.GetValue<string>(), ResponseStyle = snapshot["responseStyle"]?.GetValue<string>(),
        DefaultScope = snapshot["defaultScope"]?.GetValue<string>(), Notes = snapshot["notes"]?.GetValue<string>()
    };

    private async Task<AiActionProposalDto> PrepareTaskUpdateAsync(JsonObject args, CancellationToken ct)
    {
        var taskId = GetGuid(args, "taskId") ?? throw new AiToolRejectedException("Falta la tarea. Buscala con tasks.list.");
        var task = await dbContext.Tasks.AsNoTracking().Where(x => x.Id == taskId && x.Activo)
            .Select(x => new
            {
                x.Titulo, x.FechaInicio, x.FechaVencimiento, x.Prioridad, x.DeveloperId, x.ResponsableId, x.Estado,
                Developer = x.Developer != null ? x.Developer.FullName : null, Responsible = x.Responsable != null ? x.Responsable.FullName : null,
                Project = x.Proyecto != null ? x.Proyecto.Nombre : null
            }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("La tarea indicada no existe o no está activa. Buscala con tasks.list.");
        if (task.Estado is DomainTaskStatus.Finalizada or DomainTaskStatus.Cancelada)
            throw new AiToolRejectedException("La tarea está finalizada o cancelada; cambiá primero su estado.");

        var title = GetString(args, "title", 200)?.Trim();
        var startDate = GetDate(args, "startDate");
        var dueDate = GetDate(args, "dueDate");
        var priority = NormalizeOption(GetEnum(args, "priority", Priorities), Priorities);
        var developerId = GetGuid(args, "developerId");
        var responsibleUserId = GetGuid(args, "responsibleUserId");
        if (developerId.HasValue && responsibleUserId.HasValue)
            throw new AiToolRejectedException("Asigná la tarea a un desarrollador o a un usuario responsable, no a ambos.");
        if (title is not null && title.Length == 0) throw new AiToolRejectedException("El título no puede quedar vacío.");
        if (title is null && startDate is null && dueDate is null && priority is null && developerId is null && responsibleUserId is null)
            throw new AiToolRejectedException("Indicá qué querés cambiar de la tarea (fechas, prioridad, título o asignación).");

        var finalStart = startDate ?? task.FechaInicio;
        var finalDue = dueDate ?? task.FechaVencimiento;
        if (finalStart.HasValue && finalDue.HasValue && finalDue < finalStart)
            throw new AiToolRejectedException("La fecha de vencimiento no puede ser anterior a la fecha de inicio.");

        var details = new List<AiActionDetailDto> { Detail("Tarea", task.Titulo) };
        if (task.Project is not null) details.Add(Detail("Proyecto", task.Project));
        if (title is not null && title != task.Titulo) details.Add(Detail("Título", $"{task.Titulo} → {title}"));
        if (startDate.HasValue) details.Add(Detail("Inicio", $"{FormatOptionalDate(task.FechaInicio)} → {FormatDate(startDate.Value)}"));
        if (dueDate.HasValue) details.Add(Detail("Vence", $"{FormatOptionalDate(task.FechaVencimiento)} → {FormatDate(dueDate.Value)}"));
        if (priority is not null) details.Add(Detail("Prioridad", $"{task.Prioridad} → {priority}"));

        var currentAssignee = task.Developer ?? task.Responsible ?? "sin asignar";
        if (developerId.HasValue)
        {
            var name = await dbContext.Developers.AsNoTracking().Where(x => x.Id == developerId && x.Activo).Select(x => x.FullName).SingleOrDefaultAsync(ct)
                ?? throw new AiToolRejectedException("El desarrollador indicado no existe o no está activo. Buscalo con people.list.");
            details.Add(Detail("Asignada a", $"{currentAssignee} → {name}"));
        }
        else if (responsibleUserId.HasValue)
        {
            var name = await dbContext.Users.AsNoTracking().Where(x => x.Id == responsibleUserId && x.Activo).Select(x => x.FullName).SingleOrDefaultAsync(ct)
                ?? throw new AiToolRejectedException("El usuario responsable no existe o no está activo. Buscalo con people.list.");
            details.Add(Detail("Asignada a", $"{currentAssignee} → {name}"));
        }

        var normalized = new JsonObject { ["taskId"] = taskId.ToString() };
        if (title is not null) normalized["title"] = title;
        if (startDate.HasValue) normalized["startDate"] = startDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (dueDate.HasValue) normalized["dueDate"] = dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (priority is not null) normalized["priority"] = priority;
        if (developerId.HasValue) normalized["developerId"] = developerId.Value.ToString();
        if (responsibleUserId.HasValue) normalized["responsibleUserId"] = responsibleUserId.Value.ToString();

        var verb = dueDate.HasValue || startDate.HasValue ? "Reprogramar" : developerId.HasValue || responsibleUserId.HasValue ? "Reasignar" : "Modificar";
        return new AiActionProposalDto
        {
            ToolName = "tasks.update",
            Title = $"{verb} tarea",
            Summary = $"{verb} la tarea «{task.Titulo}».",
            Details = details,
            Arguments = normalized
        };
    }

    private async Task<AiActionProposalDto> PrepareMeetingUpdateAsync(AiExecutionContext actor, JsonObject args, CancellationToken ct)
    {
        var meetingId = GetGuid(args, "meetingId") ?? throw new AiToolRejectedException("Falta la reunión. Buscala con meetings.list.");
        if (!IsAdministrator(actor) && await meetings.GetByIdAsync(actor.UserId, false, meetingId, ct) is null)
            throw new AiToolRejectedException("No tenés acceso a esa reunión.");
        var meeting = await dbContext.Meetings.AsNoTracking().Where(x => x.Id == meetingId && x.Activo)
            .Select(x => new { x.Titulo, x.Inicio, x.Fin, x.Estado, x.Ubicacion, Project = x.Proyecto != null ? x.Proyecto.Nombre : null }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("La reunión indicada no existe. Buscala con meetings.list.");
        if (meeting.Estado == MeetingStatus.Cancelada) throw new AiToolRejectedException("La reunión está cancelada.");

        var currentStart = ToLocal(meeting.Inicio);
        var currentEnd = ToLocal(meeting.Fin);
        var startsAt = GetLocalDateTime(args, "startsAt");
        var endsAt = GetLocalDateTime(args, "endsAt");
        var duration = GetInt(args, "durationMinutes");
        if (duration is < 15 or > 480) throw new AiToolRejectedException("La duración debe estar entre 15 y 480 minutos.");
        var title = GetString(args, "title", 200)?.Trim();
        var location = GetString(args, "location", 300)?.Trim();
        if (startsAt is null && endsAt is null && duration is null && title is null && location is null)
            throw new AiToolRejectedException("Indicá qué querés cambiar de la reunión (horario, duración, título o ubicación).");

        var newStart = startsAt ?? currentStart;
        var newEnd = endsAt ?? (duration.HasValue ? newStart.AddMinutes(duration.Value) : newStart + (currentEnd - currentStart));
        if (newEnd <= newStart) throw new AiToolRejectedException("La hora de finalización debe ser posterior al inicio.");
        if ((newEnd - newStart).TotalHours > 12) throw new AiToolRejectedException("La reunión no puede durar más de 12 horas.");

        var details = new List<AiActionDetailDto> { Detail("Reunión", meeting.Titulo) };
        if (meeting.Project is not null) details.Add(Detail("Proyecto", meeting.Project));
        if (newStart != currentStart || newEnd != currentEnd)
            details.Add(Detail("Horario", $"{FormatRange(currentStart, currentEnd)} → {FormatRange(newStart, newEnd)}"));
        if (title is not null && title != meeting.Titulo) details.Add(Detail("Título", $"{meeting.Titulo} → {title}"));
        if (location is not null) details.Add(Detail("Ubicación", location));

        var normalized = new JsonObject
        {
            ["meetingId"] = meetingId.ToString(),
            ["startsAt"] = newStart.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture),
            ["endsAt"] = newEnd.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture)
        };
        if (!string.IsNullOrWhiteSpace(title)) normalized["title"] = title;
        if (location is not null) normalized["location"] = location;
        return new AiActionProposalDto
        {
            ToolName = "meetings.update",
            Title = "Mover reunión",
            Summary = $"Pasar «{meeting.Titulo}» al {newStart.ToString("dddd d 'de' MMMM 'a las' HH:mm", Spanish)}.",
            Details = details,
            Arguments = normalized
        };
    }

    private async Task<AiActionProposalDto> PrepareMeetingCancelAsync(AiExecutionContext actor, JsonObject args, CancellationToken ct)
    {
        var meetingId = GetGuid(args, "meetingId") ?? throw new AiToolRejectedException("Falta la reunión. Buscala con meetings.list.");
        if (!IsAdministrator(actor) && await meetings.GetByIdAsync(actor.UserId, false, meetingId, ct) is null)
            throw new AiToolRejectedException("No tenés acceso a esa reunión.");
        var meeting = await dbContext.Meetings.AsNoTracking().Where(x => x.Id == meetingId && x.Activo)
            .Select(x => new { x.Titulo, x.Inicio, x.Fin, x.Estado, Project = x.Proyecto != null ? x.Proyecto.Nombre : null, Participants = x.Participantes.Count }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("La reunión indicada no existe. Buscala con meetings.list.");
        if (meeting.Estado == MeetingStatus.Cancelada) throw new AiToolRejectedException("La reunión ya está cancelada.");
        var details = new List<AiActionDetailDto> { Detail("Reunión", meeting.Titulo), Detail("Cuándo", FormatRange(ToLocal(meeting.Inicio), ToLocal(meeting.Fin))) };
        if (meeting.Project is not null) details.Add(Detail("Proyecto", meeting.Project));
        if (meeting.Participants > 0) details.Add(Detail("Participantes", meeting.Participants.ToString(CultureInfo.InvariantCulture)));
        return new AiActionProposalDto
        {
            ToolName = "meetings.cancel",
            Title = "Cancelar reunión",
            Summary = $"Cancelar «{meeting.Titulo}». Esta acción no se puede deshacer desde el asistente.",
            Details = details,
            Arguments = new JsonObject { ["meetingId"] = meetingId.ToString() }
        };
    }

    private async Task<AiActionProposalDto> PrepareMovementAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var type = NormalizeOption(GetEnum(args, "type", MovementTypes), MovementTypes) ?? throw new AiToolRejectedException("Indicá si es un ingreso o un egreso.");
        var amount = GetDecimal(args, "amount") ?? throw new AiToolRejectedException("Falta el monto.");
        if (amount <= 0 || amount > 1_000_000_000) throw new AiToolRejectedException("El monto debe ser mayor a cero.");
        var currency = NormalizeOption(GetEnum(args, "currency", CurrencyOptions), CurrencyOptions) ?? throw new AiToolRejectedException("Indicá la moneda (ARS o USD).");
        var description = GetString(args, "description", 300)?.Trim();
        if (string.IsNullOrWhiteSpace(description)) throw new AiToolRejectedException("Falta la descripción del movimiento.");
        var categoryName = GetString(args, "category", 100)?.Trim();
        var date = GetDate(args, "date") ?? LocalToday(now);
        var settled = GetBool(args, "settled") ?? false;
        var dueDate = GetDate(args, "dueDate");
        var clientId = GetGuid(args, "clientId");
        var projectId = GetGuid(args, "projectId");
        if (settled && date > LocalToday(now)) throw new AiToolRejectedException("Un cobro o pago ya realizado no puede tener fecha futura.");
        if (dueDate.HasValue && dueDate < date) throw new AiToolRejectedException("El vencimiento no puede ser anterior a la fecha del movimiento.");

        var parsedType = Enum.Parse<FinancialMovementType>(type);
        var categories = await dbContext.FinancialCategories.AsNoTracking().Where(x => x.Activo && x.IsActive && x.MovementType == parsedType)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var available = string.Join(", ", categories.Select(x => x.Name));
        if (string.IsNullOrWhiteSpace(categoryName))
            throw new AiToolRejectedException($"Falta la categoría. Opciones para {type.ToLowerInvariant()}: {available}.");
        var matches = categories.Where(x => string.Equals(x.Name, categoryName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) matches = categories.Where(x => x.Name.Contains(categoryName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
            throw new AiToolRejectedException(matches.Count == 0
                ? $"No encontré la categoría «{categoryName}». Opciones para {type.ToLowerInvariant()}: {available}."
                : $"La categoría «{categoryName}» es ambigua: {string.Join(", ", matches.Select(x => x.Name))}.");
        var category = matches[0];

        var details = new List<AiActionDetailDto>
        {
            Detail("Tipo", type), Detail("Monto", $"{currency} {amount.ToString("N2", Spanish)}"), Detail("Categoría", category.Name),
            Detail("Fecha", FormatDate(date)), Detail("Estado", settled ? (parsedType == FinancialMovementType.Ingreso ? "Cobrado" : "Pagado") : "Pendiente")
        };
        if (dueDate.HasValue) details.Add(Detail("Vence", FormatDate(dueDate.Value)));
        if (clientId.HasValue)
            details.Add(Detail("Cliente", await dbContext.Clients.AsNoTracking().Where(x => x.Id == clientId).Select(x => x.CommercialName).SingleOrDefaultAsync(ct)
                ?? throw new AiToolRejectedException("El cliente indicado no existe. Buscalo con clients.list.")));
        if (projectId.HasValue)
            details.Add(Detail("Proyecto", await dbContext.Projects.AsNoTracking().Where(x => x.Id == projectId && x.Activo).Select(x => x.Nombre).SingleOrDefaultAsync(ct)
                ?? throw new AiToolRejectedException("El proyecto indicado no existe. Buscalo con projects.list.")));

        var normalized = new JsonObject
        {
            ["type"] = type, ["amount"] = amount, ["currency"] = currency, ["description"] = description, ["category"] = category.Name,
            ["date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["settled"] = settled
        };
        if (dueDate.HasValue) normalized["dueDate"] = dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (clientId.HasValue) normalized["clientId"] = clientId.Value.ToString();
        if (projectId.HasValue) normalized["projectId"] = projectId.Value.ToString();
        return new AiActionProposalDto
        {
            ToolName = "finance.create_movement",
            Title = parsedType == FinancialMovementType.Ingreso ? "Registrar ingreso" : "Registrar gasto",
            Summary = $"Registrar {(parsedType == FinancialMovementType.Ingreso ? "un ingreso" : "un gasto")} de {currency} {amount.ToString("N2", Spanish)}: {description}.",
            Details = details,
            Arguments = normalized
        };
    }

    private async Task<AiActionProposalDto> PrepareProspectAsync(JsonObject args, CancellationToken ct)
    {
        var name = GetString(args, "name", 160)?.Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new AiToolRejectedException("Falta el nombre comercial del prospecto.");
        var serviceType = GetString(args, "serviceType", 120)?.Trim();
        var lowered = name.ToLower();
        var existing = await dbContext.Clients.AsNoTracking().Where(x => x.CommercialName.ToLower() == lowered).Select(x => x.CommercialName).FirstOrDefaultAsync(ct);
        if (existing is not null) throw new AiToolRejectedException($"Ya existe un cliente llamado «{existing}». Buscalo con clients.list.");
        var details = new List<AiActionDetailDto> { Detail("Nombre", name), Detail("Estado", "Prospecto") };
        if (!string.IsNullOrWhiteSpace(serviceType)) details.Add(Detail("Servicio", serviceType));
        var normalized = new JsonObject { ["name"] = name };
        if (!string.IsNullOrWhiteSpace(serviceType)) normalized["serviceType"] = serviceType;
        return new AiActionProposalDto
        {
            ToolName = "clients.create_prospect",
            Title = "Crear prospecto",
            Summary = $"Crear el cliente prospecto «{name}».",
            Details = details,
            Arguments = normalized
        };
    }

    private async Task<AiActionOutcomeDto> ExecuteExtendedActionAsync(AiExecutionContext actor, Guid actionId, AiActionProposalDto prepared, CancellationToken ct)
    {
        var args = prepared.Arguments;
        var now = DateTime.UtcNow;
        switch (prepared.ToolName)
        {
            case "tasks.update":
            {
                var taskId = GetGuid(args, "taskId")!.Value;
                var current = await tasks.GetByIdAsync(taskId, ct) ?? throw new AiToolRejectedException("La tarea ya no existe.");
                var request = ToUpsert(current);
                var before = TaskSnapshot(request);
                if (GetString(args, "title", 200) is { } title) request.Title = title;
                if (GetDate(args, "startDate") is { } start) request.StartDate = start;
                if (GetDate(args, "dueDate") is { } due) request.DueDate = due;
                if (GetString(args, "priority", 20) is { } priority) request.Priority = priority;
                if (GetGuid(args, "developerId") is { } developerId) { request.DeveloperId = developerId; request.ResponsibleId = null; }
                if (GetGuid(args, "responsibleUserId") is { } responsibleId) { request.ResponsibleId = responsibleId; request.DeveloperId = null; }
                var updated = await tasks.UpdateAsync(taskId, request, ct) ?? throw new AiToolRejectedException("La tarea ya no existe.");
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, actualicé la tarea «{updated.Title}»" + (updated.DueDate.HasValue ? $" (vence el {FormatDate(updated.DueDate.Value)})." : "."),
                    Link = "/tareas", EntityId = taskId,
                    Source = Source("tasks.update", "Kodvian.Core.Application.Tasks", now, null, null, null, "/tareas"),
                    UndoData = new JsonObject { ["before"] = before, ["after"] = TaskSnapshot(ToUpsert(updated)) }
                };
            }
            case "meetings.update":
            {
                var meetingId = GetGuid(args, "meetingId")!.Value;
                var current = await meetings.GetByIdAsync(actor.UserId, IsAdministrator(actor), meetingId, ct) ?? throw new AiToolRejectedException("La reunión ya no existe o no tenés permiso para modificarla.");
                var request = ToUpsert(current);
                var before = MeetingSnapshot(request);
                request.StartsAt = TimeZoneInfo.ConvertTimeToUtc(GetLocalDateTime(args, "startsAt")!.Value, BuenosAiresTimeZone);
                request.EndsAt = TimeZoneInfo.ConvertTimeToUtc(GetLocalDateTime(args, "endsAt")!.Value, BuenosAiresTimeZone);
                if (GetString(args, "title", 200) is { } title) request.Title = title;
                if (GetString(args, "location", 300) is { } location) request.Location = location;
                var updated = await meetings.UpdateAsync(actor.UserId, IsAdministrator(actor), meetingId, request, ct) ?? throw new AiToolRejectedException("La reunión ya no existe o no tenés permiso para modificarla.");
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, moví «{updated.Title}» al {ToLocal(updated.StartsAt).ToString("dddd d 'de' MMMM 'a las' HH:mm", Spanish)}.",
                    Link = "/agenda", EntityId = meetingId,
                    Source = Source("meetings.update", "Kodvian.Core.Application.Meetings", now, null, null, null, "/agenda"),
                    UndoData = new JsonObject { ["before"] = before, ["after"] = MeetingSnapshot(ToUpsert(updated)) }
                };
            }
            case "meetings.cancel":
            {
                var meetingId = GetGuid(args, "meetingId")!.Value;
                var cancelled = await meetings.CancelAsync(actor.UserId, IsAdministrator(actor), meetingId, ct) ?? throw new AiToolRejectedException("La reunión ya no existe o no tenés permiso para modificarla.");
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, cancelé «{cancelled.Title}».", Link = "/agenda", EntityId = meetingId,
                    Source = Source("meetings.cancel", "Kodvian.Core.Application.Meetings", now, null, null, null, "/agenda")
                };
            }
            case "finance.create_movement":
            {
                var type = GetString(args, "type", 20)!;
                var parsedType = Enum.Parse<FinancialMovementType>(type);
                var categoryName = GetString(args, "category", 100)!;
                var categoryId = await dbContext.FinancialCategories.AsNoTracking()
                    .Where(x => x.Activo && x.IsActive && x.MovementType == parsedType && x.Name == categoryName).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct)
                    ?? throw new AiToolRejectedException("La categoría ya no está disponible.");
                var settled = GetBool(args, "settled") ?? false;
                var date = GetDate(args, "date")!.Value;
                var created = await movements.CreateAsync(actor.UserId, new FinancialMovementUpsertRequestDto
                {
                    // El id de la acción se usa como identificador de operación: si se reintenta, no se duplica el movimiento.
                    RequestId = actionId,
                    MovementType = type,
                    CategoryId = categoryId,
                    Amount = GetDecimal(args, "amount")!.Value,
                    Currency = GetString(args, "currency", 3)!,
                    Description = GetString(args, "description", 300)!,
                    MovementDate = date,
                    DueDate = GetDate(args, "dueDate"),
                    Status = settled ? (parsedType == FinancialMovementType.Ingreso ? "Cobrado" : "Pagado") : "Pendiente",
                    SettlementDate = settled ? date : null,
                    ClientId = GetGuid(args, "clientId"),
                    ProjectId = GetGuid(args, "projectId"),
                    Nature = "Operacion",
                    Funding = "Empresa"
                }, ct);
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, registré {(parsedType == FinancialMovementType.Ingreso ? "el ingreso" : "el gasto")} de {created.Currency} {created.Amount.ToString("N2", Spanish)} ({created.Description}).",
                    Link = "/finanzas", EntityId = created.Id,
                    Source = Source("finance.create_movement", "Kodvian.Core.Application.Finances", now, null, null, created.Currency, "/finanzas")
                };
            }
            case "clients.create_prospect":
            {
                var created = await clients.CreateAsync(new ClientUpsertRequestDto
                {
                    CommercialName = GetString(args, "name", 160)!,
                    ServiceType = GetString(args, "serviceType", 120),
                    Status = "Prospecto",
                    IsActive = true
                }, ct);
                return new AiActionOutcomeDto
                {
                    Message = $"Listo, creé el prospecto «{created.CommercialName}».", Link = "/clientes", EntityId = created.Id,
                    Source = Source("clients.create_prospect", "Kodvian.Core.Application.Clients", now, null, null, null, "/clientes")
                };
            }
            case "preferences.update":
            {
                var before = await AiPreferencesService.LoadAsync(dbContext, actor.UserId, ct);
                var after = AiPreferenceRules.Normalize(new AiUserPreferencesDto
                {
                    PreferredCurrency = GetString(args, "preferredCurrency", 3), DefaultMeetingMinutes = GetInt(args, "defaultMeetingMinutes"),
                    DefaultTaskPriority = GetString(args, "defaultTaskPriority", 20), ResponseStyle = GetString(args, "responseStyle", 20),
                    DefaultScope = GetString(args, "defaultScope", 10), Notes = GetString(args, "notes", AiPreferenceRules.MaxNotesLength)
                });
                await AiPreferencesService.SaveAsync(dbContext, actor.UserId, after, ct);
                return new AiActionOutcomeDto
                {
                    Message = "Listo, guardé tus preferencias. Las voy a tener en cuenta desde la próxima respuesta.",
                    EntityId = actor.UserId,
                    UndoData = new JsonObject { ["before"] = PreferencesSnapshot(before), ["after"] = PreferencesSnapshot(after) }
                };
            }
            default:
                throw new AiToolRejectedException("La acción solicitada no está habilitada.");
        }
    }

    public async Task<AiActionOutcomeDto> UndoActionAsync(AiExecutionContext actor, Guid actionId, string toolName, Guid entityId, JsonObject undoData, CancellationToken cancellationToken)
    {
        var definition = Definitions.SingleOrDefault(x => x.Name == toolName && x.RequiresConfirmation)
            ?? throw new AiToolRejectedException("La acción no se puede deshacer.");
        if (!CanUse(actor, definition))
        {
            await WriteAuditAsync(actor, actionId, toolName, "Denied", "permission_denied", cancellationToken);
            throw new AiToolRejectedException("No tenés permisos para deshacer esta acción.");
        }
        if (!await sessionValidator.IsValidAsync(actor.UserId, actor.SessionVersion, cancellationToken))
        {
            await WriteAuditAsync(actor, actionId, toolName, "Denied", "session_revoked", cancellationToken);
            throw new UnauthorizedAccessException("La sesión dejó de estar vigente.");
        }

        var outcome = await RunGuardedAsync(actor, actionId, toolName, () => UndoCoreAsync(actor, toolName, entityId, undoData, cancellationToken), cancellationToken);
        await WriteAuditAsync(actor, actionId, toolName, "Undone", "user_undo", cancellationToken);
        return outcome;
    }

    private async Task<AiActionOutcomeDto> UndoCoreAsync(AiExecutionContext actor, string toolName, Guid entityId, JsonObject undoData, CancellationToken ct)
    {
        const string changedSince = "Alguien la modificó después de confirmarla; revisala desde su pantalla.";
        var now = DateTime.UtcNow;
        switch (toolName)
        {
            case "tasks.create":
            {
                var task = await dbContext.Tasks.AsNoTracking().Where(x => x.Id == entityId).Select(x => new { x.Titulo, x.FechaActualizacion, x.Activo }).SingleOrDefaultAsync(ct)
                    ?? throw new AiToolRejectedException("La tarea ya no existe.");
                if (task.FechaActualizacion.HasValue || !task.Activo) throw new AiToolRejectedException(changedSince);
                // Las tareas solo se eliminan canceladas: se cancela y se elimina en el mismo paso.
                await tasks.UpdateStatusAsync(entityId, new TaskStatusUpdateRequestDto { Status = "Cancelada" }, ct);
                await tasks.DeleteAsync(entityId, ct);
                return new AiActionOutcomeDto { Message = $"Deshice la creación de «{task.Titulo}».", Link = "/tareas", Source = Source("tasks.create", "Kodvian.Core.Application.Tasks", now, null, null, null, "/tareas") };
            }
            case "tasks.update_status":
            {
                var task = await dbContext.Tasks.AsNoTracking().Where(x => x.Id == entityId).Select(x => new { x.Titulo, x.Estado, x.DeveloperId, x.ResponsableId }).SingleOrDefaultAsync(ct)
                    ?? throw new AiToolRejectedException("La tarea ya no existe.");
                if (!actor.Permissions.Contains(PermissionCodes.TasksWrite) && !await IsOwnTaskAsync(actor, task.DeveloperId, task.ResponsableId, ct))
                    throw new AiToolRejectedException("Solo podés cambiar el estado de tus propias tareas.");
                if (!string.Equals(task.Estado.ToString(), undoData["newStatus"]?.GetValue<string>(), StringComparison.Ordinal)) throw new AiToolRejectedException(changedSince);
                var previous = undoData["previousStatus"]!.GetValue<string>();
                await tasks.UpdateStatusAsync(entityId, new TaskStatusUpdateRequestDto { Status = previous, KanbanOrder = undoData["previousKanbanOrder"]?.GetValue<int>() ?? 0 }, ct);
                return new AiActionOutcomeDto { Message = $"Listo, «{task.Titulo}» volvió a {StatusLabel(previous)}.", Link = "/tareas", Source = Source("tasks.update_status", "Kodvian.Core.Application.Tasks", now, null, null, null, "/tareas") };
            }
            case "tasks.update":
            {
                var current = await tasks.GetByIdAsync(entityId, ct) ?? throw new AiToolRejectedException("La tarea ya no existe.");
                var request = ToUpsert(current);
                if (!JsonNode.DeepEquals(TaskSnapshot(request), undoData["after"])) throw new AiToolRejectedException(changedSince);
                var before = undoData["before"]!.AsObject();
                request.Title = before["title"]!.GetValue<string>();
                request.StartDate = ParseOptionalDate(before["startDate"]);
                request.DueDate = ParseOptionalDate(before["dueDate"]);
                request.Priority = before["priority"]!.GetValue<string>();
                request.DeveloperId = ParseOptionalGuid(before["developerId"]);
                request.ResponsibleId = ParseOptionalGuid(before["responsibleId"]);
                await tasks.UpdateAsync(entityId, request, ct);
                return new AiActionOutcomeDto { Message = $"Listo, la tarea «{request.Title}» volvió a como estaba.", Link = "/tareas", Source = Source("tasks.update", "Kodvian.Core.Application.Tasks", now, null, null, null, "/tareas") };
            }
            case "meetings.create":
            {
                var cancelled = await meetings.CancelAsync(actor.UserId, IsAdministrator(actor), entityId, ct) ?? throw new AiToolRejectedException("La reunión ya no existe o no tenés permiso para modificarla.");
                return new AiActionOutcomeDto { Message = $"Deshice la reunión «{cancelled.Title}» (quedó cancelada).", Link = "/agenda", Source = Source("meetings.create", "Kodvian.Core.Application.Meetings", now, null, null, null, "/agenda") };
            }
            case "meetings.update":
            {
                var current = await meetings.GetByIdAsync(actor.UserId, IsAdministrator(actor), entityId, ct) ?? throw new AiToolRejectedException("La reunión ya no existe o no tenés permiso para modificarla.");
                var request = ToUpsert(current);
                if (!JsonNode.DeepEquals(MeetingSnapshot(request), undoData["after"])) throw new AiToolRejectedException(changedSince);
                var before = undoData["before"]!.AsObject();
                request.StartsAt = DateTime.Parse(before["startsAt"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                request.EndsAt = DateTime.Parse(before["endsAt"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
                request.Title = before["title"]!.GetValue<string>();
                request.Location = before["location"]?.GetValue<string>();
                await meetings.UpdateAsync(actor.UserId, IsAdministrator(actor), entityId, request, ct);
                return new AiActionOutcomeDto { Message = $"Listo, «{request.Title}» volvió a su horario original.", Link = "/agenda", Source = Source("meetings.update", "Kodvian.Core.Application.Meetings", now, null, null, null, "/agenda") };
            }
            case "preferences.update":
            {
                if (entityId != actor.UserId) throw new AiToolRejectedException("Estas preferencias no son tuyas.");
                var current = await AiPreferencesService.LoadAsync(dbContext, actor.UserId, ct);
                if (!JsonNode.DeepEquals(PreferencesSnapshot(current), undoData["after"])) throw new AiToolRejectedException("Las preferencias cambiaron después; revisalas desde Preferencias.");
                await AiPreferencesService.SaveAsync(dbContext, actor.UserId, PreferencesFromSnapshot(undoData["before"]!.AsObject()), ct);
                return new AiActionOutcomeDto { Message = "Listo, tus preferencias volvieron a como estaban." };
            }
            default:
                throw new AiToolRejectedException("Esta acción no se puede deshacer.");
        }
    }

    private static TaskUpsertRequestDto ToUpsert(Kodvian.Core.Application.Tasks.Dtos.TaskDetailDto task) => new()
    {
        ProjectId = task.ProjectId, Title = task.Title, Description = task.Description, DeveloperId = task.DeveloperId, ResponsibleId = task.ResponsibleId,
        Status = task.Status, Priority = task.Priority, StartDate = task.StartDate, DueDate = task.DueDate, FinishedDate = task.FinishedDate,
        EstimatedHours = task.EstimatedHours, RealHours = task.RealHours, KanbanOrder = task.KanbanOrder, IsActive = task.IsActive
    };

    private static MeetingUpsertRequestDto ToUpsert(Kodvian.Core.Application.Meetings.Dtos.MeetingDto meeting) => new()
    {
        ProjectId = meeting.ProjectId, Title = meeting.Title, Description = meeting.Description, StartsAt = DateTime.SpecifyKind(meeting.StartsAt, DateTimeKind.Utc),
        EndsAt = DateTime.SpecifyKind(meeting.EndsAt, DateTimeKind.Utc), Priority = meeting.Priority, Status = meeting.Status, Link = meeting.Link, Location = meeting.Location,
        ParticipantIds = meeting.Participants.Select(x => x.Id).ToArray()
    };

    private static JsonObject TaskSnapshot(TaskUpsertRequestDto task) => new()
    {
        ["title"] = task.Title,
        ["startDate"] = task.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["dueDate"] = task.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["priority"] = task.Priority,
        ["developerId"] = task.DeveloperId?.ToString(),
        ["responsibleId"] = task.ResponsibleId?.ToString()
    };

    private static JsonObject MeetingSnapshot(MeetingUpsertRequestDto meeting) => new()
    {
        ["startsAt"] = DateTime.SpecifyKind(meeting.StartsAt, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["endsAt"] = DateTime.SpecifyKind(meeting.EndsAt, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
        ["title"] = meeting.Title,
        ["location"] = meeting.Location
    };

    private static DateOnly? ParseOptionalDate(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static Guid? ParseOptionalGuid(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, out var id) ? id : null;

    private static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), BuenosAiresTimeZone);

    private static string FormatRange(DateTime start, DateTime end) =>
        $"{start.ToString("ddd d/M HH:mm", Spanish)}–{end:HH:mm}" + (end.Date != start.Date ? $" del {end.ToString("d/M", Spanish)}" : string.Empty);

    private static string FormatOptionalDate(DateOnly? date) => date.HasValue ? FormatDate(date.Value) : "sin fecha";

    private static decimal? GetDecimal(JsonObject args, string property)
    {
        if (!args.TryGetPropertyValue(property, out var value) || value is null) return null;
        // Sirve tanto para valores que vienen del JSON del modelo como para valores numéricos creados en código (int, double, decimal).
        if (value is JsonValue number && number.GetValueKind() == System.Text.Json.JsonValueKind.Number
            && decimal.TryParse(number.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedNumber)) return Math.Round(parsedNumber, 2);
        if (value is JsonValue textValue && textValue.TryGetValue<string>(out var text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)) return Math.Round(parsed, 2);
        throw new AiToolRejectedException($"El parámetro {property} no es válido.");
    }

    private static JsonObject NumberSchema(decimal min, decimal max) => new() { ["type"] = "number", ["minimum"] = min, ["maximum"] = max };
}
