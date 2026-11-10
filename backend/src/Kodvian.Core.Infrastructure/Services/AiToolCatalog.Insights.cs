using System.Text.Json.Nodes;
using Kodvian.Core.Application.Ai;
using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using DomainTaskStatus = Kodvian.Core.Domain.Enums.TaskStatus;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>Consultas de detalle: finanzas, "mi día" y fichas de proyecto y cliente.</summary>
public sealed partial class AiToolCatalog
{

    private DateOnly LocalToday(DateTime utcNow) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, BuenosAiresTimeZone));

    private async Task<(JsonObject, AiSourceReferenceDto)> FinanceMovementsAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var (from, to) = ResolvePeriod(args, "from", "to", now);
        var type = NormalizeOption(GetEnum(args, "type", MovementTypes), MovementTypes);
        var status = NormalizeOption(GetEnum(args, "status", MovementStatuses), MovementStatuses);
        var currency = NormalizeOption(GetEnum(args, "currency", CurrencyOptions), CurrencyOptions);
        var search = GetString(args, "search", 100)?.Trim().ToLower();
        var clientId = GetGuid(args, "clientId");
        var projectId = GetGuid(args, "projectId");
        var pageSize = GetPageSize(args);

        var query = dbContext.FinancialMovements.AsNoTracking().Where(x => x.Activo && x.MovementDate >= from && x.MovementDate <= to);
        if (type is not null) { var parsed = Enum.Parse<FinancialMovementType>(type); query = query.Where(x => x.MovementType == parsed); }
        if (status is not null) { var parsed = Enum.Parse<FinancialMovementStatus>(status); query = query.Where(x => x.Status == parsed); }
        else query = query.Where(x => x.Status != FinancialMovementStatus.Anulado);
        if (currency is not null) query = query.Where(x => x.Currency == currency);
        if (clientId.HasValue) query = query.Where(x => x.ClientId == clientId);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Description.ToLower().Contains(search)
                || (x.Client != null && x.Client.CommercialName.ToLower().Contains(search))
                || (x.Provider != null && x.Provider.Name.ToLower().Contains(search))
                || (x.Category != null && x.Category.Name.ToLower().Contains(search)));

        var totals = await query.GroupBy(x => new { x.Currency, x.MovementType })
            .Select(g => new { g.Key.Currency, g.Key.MovementType, Amount = g.Sum(x => x.Amount), Count = g.Count() }).ToListAsync(ct);
        var rows = await query.OrderByDescending(x => x.MovementDate).ThenByDescending(x => x.Amount).Take(pageSize)
            .Select(x => new
            {
                x.Id, x.MovementDate, x.MovementType, Category = x.Category != null ? x.Category.Name : null, x.Description, x.Amount, x.Currency, x.Status, x.DueDate,
                Client = x.Client != null ? x.Client.CommercialName : null, Provider = x.Provider != null ? x.Provider.Name : null, Project = x.Project != null ? x.Project.Nombre : null
            }).ToListAsync(ct);

        var output = new
        {
            totals = totals.OrderBy(x => x.Currency).ThenBy(x => x.MovementType)
                .Select(x => new { x.Currency, type = x.MovementType.ToString(), total = x.Amount, count = x.Count }),
            items = rows.Select(x => new
            {
                x.Id, date = x.MovementDate, type = x.MovementType.ToString(), x.Category, x.Description, x.Amount, x.Currency, status = x.Status.ToString(), x.DueDate, x.Client, x.Provider, x.Project
            })
        };
        var count = totals.Sum(x => x.Count);
        var warnings = count > rows.Count ? new[] { $"Se muestran {rows.Count} de {count} movimientos; los totales incluyen todos." } : [];
        return (Wrap("finance.list_movements", now, new { from, to, type, status, currency, search, clientId, projectId, pageSize }, output, warnings, null, null),
            Source("finance.list_movements", "Kodvian.Core.Application.Finances", now, from, to, currency, "/finanzas"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> FinancePendingAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var kind = NormalizeOption(GetEnum(args, "kind", PendingKinds), PendingKinds) ?? "todos";
        var overdueOnly = GetBool(args, "overdueOnly") ?? false;
        var dueUntil = GetDate(args, "dueUntil");
        var currency = NormalizeOption(GetEnum(args, "currency", CurrencyOptions), CurrencyOptions);
        var clientId = GetGuid(args, "clientId");
        var pageSize = GetPageSize(args);
        var today = LocalToday(now);

        var query = dbContext.FinancialMovements.AsNoTracking()
            .Where(x => x.Activo && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido));
        if (kind == "cobros") query = query.Where(x => x.MovementType == FinancialMovementType.Ingreso);
        if (kind == "pagos") query = query.Where(x => x.MovementType == FinancialMovementType.Egreso);
        if (currency is not null) query = query.Where(x => x.Currency == currency);
        if (clientId.HasValue) query = query.Where(x => x.ClientId == clientId);
        if (overdueOnly) query = query.Where(x => (x.DueDate ?? x.MovementDate) < today);
        if (dueUntil.HasValue) query = query.Where(x => (x.DueDate ?? x.MovementDate) <= dueUntil.Value);

        var rows = await query.OrderBy(x => x.DueDate ?? x.MovementDate).Take(500)
            .Select(x => new
            {
                x.Id, x.MovementType, x.Description, x.Amount, x.Currency, Due = x.DueDate ?? x.MovementDate, x.Status,
                Client = x.Client != null ? x.Client.CommercialName : null, Provider = x.Provider != null ? x.Provider.Name : null, Project = x.Project != null ? x.Project.Nombre : null
            }).ToListAsync(ct);

        var output = new
        {
            today,
            totals = rows.GroupBy(x => new { x.Currency, x.MovementType }).OrderBy(g => g.Key.Currency).Select(g => new
            {
                g.Key.Currency, type = g.Key.MovementType == FinancialMovementType.Ingreso ? "cobro" : "pago",
                total = g.Sum(x => x.Amount), overdue = g.Where(x => x.Due < today).Sum(x => x.Amount), count = g.Count()
            }),
            // "¿Quién me debe?": cobros pendientes agrupados por cliente, del más atrasado al más reciente.
            receivablesByClient = rows.Where(x => x.MovementType == FinancialMovementType.Ingreso)
                .GroupBy(x => new { Client = x.Client ?? "Sin cliente", x.Currency })
                .Select(g => new { client = g.Key.Client, g.Key.Currency, total = g.Sum(x => x.Amount), count = g.Count(), oldestDue = g.Min(x => x.Due), maxDaysOverdue = Math.Max(0, today.DayNumber - g.Min(x => x.Due).DayNumber) })
                .OrderByDescending(x => x.maxDaysOverdue).ThenByDescending(x => x.total).Take(15),
            items = rows.Take(pageSize).Select(x => new
            {
                x.Id, type = x.MovementType == FinancialMovementType.Ingreso ? "cobro" : "pago", x.Description, x.Amount, x.Currency, dueDate = x.Due,
                daysOverdue = Math.Max(0, today.DayNumber - x.Due.DayNumber), status = x.Status.ToString(), x.Client, x.Provider, x.Project
            })
        };
        var warnings = rows.Count > pageSize ? new[] { $"Se listan {pageSize} de {rows.Count} pendientes; los totales incluyen todos." } : [];
        return (Wrap("finance.list_pending", now, new { kind, overdueOnly, dueUntil, currency, clientId, pageSize }, output, warnings, null, null),
            Source("finance.list_pending", "Kodvian.Core.Application.Finances", now, null, dueUntil, currency, "/finanzas"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> FinanceCompareAsync(JsonObject args, DateTime now, CancellationToken ct)
    {
        var today = LocalToday(now);
        var year = GetInt(args, "year") ?? today.Year;
        var month = GetInt(args, "month") ?? today.Month;
        if (year is < 2000 or > 2100 || month is < 1 or > 12) throw new AiToolRejectedException("El mes solicitado no es válido.");
        var previous = new DateOnly(year, month, 1).AddMonths(-1);
        var compareYear = GetInt(args, "compareYear") ?? previous.Year;
        var compareMonth = GetInt(args, "compareMonth") ?? previous.Month;
        if (compareYear is < 2000 or > 2100 || compareMonth is < 1 or > 12) throw new AiToolRejectedException("El mes de comparación no es válido.");

        async Task<(List<Kodvian.Core.Application.Finances.Abstractions.CurrencyPeriodDto> Currencies, List<(string Currency, string Category, decimal Amount)> Categories)> PeriodAsync(int y, int m)
        {
            var from = new DateOnly(y, m, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var summary = await finance.GetPeriodSummaryAsync(from, to, ct);
            var categories = await dbContext.FinancialMovements.AsNoTracking()
                .Where(x => x.Activo && x.Nature == "Operacion" && x.MovementType == FinancialMovementType.Egreso && x.Status == FinancialMovementStatus.Pagado
                    && x.SettlementDate >= from && x.SettlementDate <= to)
                .GroupBy(x => new { x.Currency, Category = x.Category != null ? x.Category.Name : "Sin categoría" })
                .Select(g => new { g.Key.Currency, g.Key.Category, Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);
            return (summary.Currencies.ToList(), categories.Select(x => (x.Currency, x.Category, x.Amount)).ToList());
        }

        var (currentCurrencies, currentCategories) = await PeriodAsync(year, month);
        var (comparedCurrencies, comparedCategories) = await PeriodAsync(compareYear, compareMonth);

        static decimal? Percent(decimal now, decimal before) => before == 0 ? null : Math.Round((now - before) / Math.Abs(before) * 100, 1);
        var differences = CurrencyOptions.Select(c =>
        {
            var a = currentCurrencies.FirstOrDefault(x => x.Currency == c) ?? new() { Currency = c };
            var b = comparedCurrencies.FirstOrDefault(x => x.Currency == c) ?? new() { Currency = c };
            return new
            {
                currency = c,
                income = new { current = a.Income, compared = b.Income, change = a.Income - b.Income, changePercent = Percent(a.Income, b.Income) },
                expense = new { current = a.Expense, compared = b.Expense, change = a.Expense - b.Expense, changePercent = Percent(a.Expense, b.Expense) },
                result = new { current = a.Result, compared = b.Result, change = a.Result - b.Result }
            };
        });

        var categoryChanges = currentCategories.Select(x => (x.Currency, x.Category)).Union(comparedCategories.Select(x => (x.Currency, x.Category)))
            .Select(key =>
            {
                var a = currentCategories.Where(x => x.Currency == key.Currency && x.Category == key.Category).Sum(x => x.Amount);
                var b = comparedCategories.Where(x => x.Currency == key.Currency && x.Category == key.Category).Sum(x => x.Amount);
                return new { currency = key.Currency, category = key.Category, current = a, compared = b, change = a - b, changePercent = Percent(a, b) };
            })
            .OrderByDescending(x => Math.Abs(x.change)).Take(8).ToList();

        var output = new
        {
            period = new { year, month },
            comparedWith = new { year = compareYear, month = compareMonth },
            differences,
            expenseCategoryChanges = categoryChanges
        };
        return (Wrap("finance.compare_months", now, new { year, month, compareYear, compareMonth }, output, [], null, null),
            Source("finance.compare_months", "Kodvian.Core.Application.Finances", now, new DateOnly(compareYear, compareMonth, 1), new DateOnly(year, month, 1).AddMonths(1).AddDays(-1), null, "/vision-financiera"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> MyDayAsync(AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct)
    {
        var date = GetDate(args, "date") ?? LocalToday(now);
        var scope = NormalizeOption(GetEnum(args, "scope", DayScopes), DayScopes) ?? (IsAdministrator(actor) ? "todo" : "mio");
        if (!IsAdministrator(actor)) scope = "mio";
        var developerId = await dbContext.Users.AsNoTracking().Where(x => x.Id == actor.UserId).Select(x => x.DeveloperId).SingleOrDefaultAsync(ct);
        var result = new JsonObject { ["date"] = date.ToString("yyyy-MM-dd"), ["scope"] = scope };

        if (actor.Permissions.Contains(PermissionCodes.TasksRead) || actor.Permissions.Contains(PermissionCodes.DeveloperWorkRead))
        {
            var tasksQuery = dbContext.Tasks.AsNoTracking().Where(x => x.Activo && x.FechaVencimiento.HasValue && x.FechaVencimiento <= date
                && x.Estado != DomainTaskStatus.Finalizada && x.Estado != DomainTaskStatus.Cancelada);
            if (scope == "mio") tasksQuery = tasksQuery.Where(x => x.ResponsableId == actor.UserId || (developerId.HasValue && x.DeveloperId == developerId));
            var taskRows = await tasksQuery.OrderBy(x => x.FechaVencimiento).Take(40)
                .Select(x => new { x.Id, x.Titulo, Project = x.Proyecto != null ? x.Proyecto.Nombre : null, x.Estado, x.Prioridad, x.FechaVencimiento }).ToListAsync(ct);
            result["tasksDueToday"] = Node(taskRows.Where(x => x.FechaVencimiento == date).Select(x => new { x.Id, title = x.Titulo, project = x.Project, status = x.Estado.ToString(), priority = x.Prioridad.ToString() }));
            result["overdueTasks"] = Node(taskRows.Where(x => x.FechaVencimiento < date).Take(15).Select(x => new
            {
                x.Id, title = x.Titulo, project = x.Project, status = x.Estado.ToString(), priority = x.Prioridad.ToString(), dueDate = x.FechaVencimiento, daysOverdue = date.DayNumber - x.FechaVencimiento!.Value.DayNumber
            }));
        }

        if (actor.Permissions.Contains(PermissionCodes.MeetingsRead))
        {
            var start = BuenosAiresStartUtc(date);
            var end = BuenosAiresEndUtc(date);
            var meetingsQuery = dbContext.Meetings.AsNoTracking().Where(x => x.Activo && x.Estado != MeetingStatus.Cancelada && x.Inicio <= end && x.Fin >= start);
            if (scope == "mio") meetingsQuery = meetingsQuery.Where(x => x.CreadoPorId == actor.UserId || x.Participantes.Any(p => p.UserId == actor.UserId));
            var meetingRows = await meetingsQuery.OrderBy(x => x.Inicio).Take(20)
                .Select(x => new { x.Id, x.Titulo, Project = x.Proyecto != null ? x.Proyecto.Nombre : null, x.Inicio, x.Fin, x.Ubicacion }).ToListAsync(ct);
            result["meetings"] = Node(meetingRows.Select(x => new
            {
                x.Id, title = x.Titulo, project = x.Project,
                starts = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.Inicio, DateTimeKind.Utc), BuenosAiresTimeZone).ToString("HH:mm"),
                ends = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.Fin, DateTimeKind.Utc), BuenosAiresTimeZone).ToString("HH:mm")
            }));
        }

        if (actor.Permissions.Contains(PermissionCodes.FinancesRead))
        {
            var pending = await dbContext.FinancialMovements.AsNoTracking()
                .Where(x => x.Activo && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido) && (x.DueDate ?? x.MovementDate) <= date)
                .Select(x => new { x.Id, x.MovementType, x.Description, x.Amount, x.Currency, Due = x.DueDate ?? x.MovementDate, Client = x.Client != null ? x.Client.CommercialName : null, Provider = x.Provider != null ? x.Provider.Name : null })
                .ToListAsync(ct);
            result["financeDueToday"] = Node(pending.Where(x => x.Due == date).Take(15).Select(x => new
            {
                x.Id, type = x.MovementType == FinancialMovementType.Ingreso ? "cobro" : "pago", x.Description, x.Amount, x.Currency, x.Client, x.Provider
            }));
            result["financeOverdue"] = Node(pending.Where(x => x.Due < date).GroupBy(x => new { x.Currency, x.MovementType }).Select(g => new
            {
                g.Key.Currency, type = g.Key.MovementType == FinancialMovementType.Ingreso ? "cobros" : "pagos", total = g.Sum(x => x.Amount), count = g.Count()
            }));
        }

        return (Wrap("my_day.get", now, new { date, scope }, result, [], null, null), Source("my_day.get", "Kodvian.Core.Application.MyWork", now, date, date, null, "/mi-trabajo"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> ProjectDetailAsync(AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct)
    {
        var projectId = GetGuid(args, "projectId") ?? throw new AiToolRejectedException("Falta el proyecto. Buscalo con projects.list.");
        var today = LocalToday(now);
        var project = await dbContext.Projects.AsNoTracking().Where(x => x.Id == projectId)
            .Select(x => new
            {
                x.Id, x.Nombre, x.Descripcion, Client = x.Cliente != null ? x.Cliente.CommercialName : null, x.ClienteId, x.Estado, x.Prioridad,
                x.FechaInicio, x.FechaEntregaEstimada, x.FechaCierre, x.PorcentajeAvance, x.Activo,
                Responsible = x.Responsable != null ? x.Responsable.FullName : null,
                Developers = x.DeveloperAssignments.Where(a => a.Activo && a.Developer != null).Select(a => a.Developer!.FullName).ToList()
            }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("El proyecto indicado no existe. Buscalo con projects.list.");

        var tasks = await dbContext.Tasks.AsNoTracking().Where(x => x.ProyectoId == projectId && x.Activo)
            .Select(x => new { x.Id, x.Titulo, x.Estado, x.Prioridad, x.FechaVencimiento }).ToListAsync(ct);
        var open = tasks.Where(x => x.Estado != DomainTaskStatus.Finalizada && x.Estado != DomainTaskStatus.Cancelada).ToList();
        var startUtc = BuenosAiresStartUtc(today);
        var meetings = await dbContext.Meetings.AsNoTracking()
            .Where(x => x.ProyectoId == projectId && x.Activo && x.Estado != MeetingStatus.Cancelada && x.Inicio >= startUtc)
            .OrderBy(x => x.Inicio).Take(5).Select(x => new { x.Id, x.Titulo, x.Inicio }).ToListAsync(ct);

        var output = new JsonObject
        {
            ["project"] = Node(new
            {
                project.Id, name = project.Nombre, client = project.Client, project.ClienteId, status = project.Estado.ToString(), priority = project.Prioridad.ToString(),
                startDate = project.FechaInicio, estimatedDelivery = project.FechaEntregaEstimada, closedOn = project.FechaCierre, progress = project.PorcentajeAvance,
                responsible = project.Responsible, developers = project.Developers, isActive = project.Activo,
                description = project.Descripcion is { Length: > 400 } d ? d[..397] + "…" : project.Descripcion,
                daysToDelivery = project.FechaEntregaEstimada.HasValue ? project.FechaEntregaEstimada.Value.DayNumber - today.DayNumber : (int?)null
            }),
            ["taskCounts"] = Node(tasks.GroupBy(x => x.Estado).Select(g => new { status = g.Key.ToString(), count = g.Count() })),
            ["overdueTasks"] = Node(open.Where(x => x.FechaVencimiento < today).OrderBy(x => x.FechaVencimiento).Take(10)
                .Select(x => new { x.Id, title = x.Titulo, status = x.Estado.ToString(), dueDate = x.FechaVencimiento, daysOverdue = today.DayNumber - x.FechaVencimiento!.Value.DayNumber })),
            ["nextTasks"] = Node(open.Where(x => x.FechaVencimiento >= today).OrderBy(x => x.FechaVencimiento).Take(5)
                .Select(x => new { x.Id, title = x.Titulo, status = x.Estado.ToString(), dueDate = x.FechaVencimiento })),
            ["upcomingMeetings"] = Node(meetings.Select(x => new
            {
                x.Id, title = x.Titulo, starts = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.Inicio, DateTimeKind.Utc), BuenosAiresTimeZone).ToString("yyyy-MM-dd HH:mm")
            }))
        };

        if (actor.Permissions.Contains(PermissionCodes.FinancesRead))
        {
            var money = await dbContext.FinancialMovements.AsNoTracking().Where(x => x.ProjectId == projectId && x.Activo && x.Status != FinancialMovementStatus.Anulado)
                .GroupBy(x => new { x.Currency, x.MovementType })
                .Select(g => new
                {
                    g.Key.Currency, g.Key.MovementType,
                    Settled = g.Where(x => x.Status == FinancialMovementStatus.Cobrado || x.Status == FinancialMovementStatus.Pagado).Sum(x => x.Amount),
                    Pending = g.Where(x => x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido).Sum(x => x.Amount)
                }).ToListAsync(ct);
            output["finance"] = Node(money.Select(x => new { x.Currency, type = x.MovementType.ToString(), settled = x.Settled, pending = x.Pending }));
        }

        return (Wrap("projects.get", now, new { projectId }, output, [], null, null), Source("projects.get", "Kodvian.Core.Application.Projects", now, null, null, null, "/proyectos"));
    }

    private async Task<(JsonObject, AiSourceReferenceDto)> ClientDetailAsync(AiExecutionContext actor, JsonObject args, DateTime now, CancellationToken ct)
    {
        var clientId = GetGuid(args, "clientId") ?? throw new AiToolRejectedException("Falta el cliente. Buscalo con clients.list.");
        var today = LocalToday(now);
        // Igual que clients.list: sin datos de contacto, CUIT, montos acordados ni notas.
        var client = await dbContext.Clients.AsNoTracking().Where(x => x.Id == clientId)
            .Select(x => new { x.Id, x.CommercialName, x.Status, x.Activo, x.ServiceType }).SingleOrDefaultAsync(ct)
            ?? throw new AiToolRejectedException("El cliente indicado no existe. Buscalo con clients.list.");
        var projects = await dbContext.Projects.AsNoTracking().Where(x => x.ClienteId == clientId && x.Activo)
            .OrderBy(x => x.Nombre).Select(x => new { x.Id, x.Nombre, x.Estado, x.PorcentajeAvance, x.FechaEntregaEstimada }).ToListAsync(ct);
        var projectIds = projects.Select(x => x.Id).ToList();
        var startUtc = BuenosAiresStartUtc(today);
        var meetings = await dbContext.Meetings.AsNoTracking()
            .Where(x => projectIds.Contains(x.ProyectoId) && x.Activo && x.Estado != MeetingStatus.Cancelada && x.Inicio >= startUtc)
            .OrderBy(x => x.Inicio).Take(5).Select(x => new { x.Id, x.Titulo, x.Inicio }).ToListAsync(ct);

        var output = new JsonObject
        {
            ["client"] = Node(new { client.Id, name = client.CommercialName, status = client.Status.ToString(), isActive = client.Activo, serviceType = client.ServiceType }),
            ["projects"] = Node(projects.Select(x => new { x.Id, name = x.Nombre, status = x.Estado.ToString(), progress = x.PorcentajeAvance, estimatedDelivery = x.FechaEntregaEstimada })),
            ["upcomingMeetings"] = Node(meetings.Select(x => new
            {
                x.Id, title = x.Titulo, starts = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(x.Inicio, DateTimeKind.Utc), BuenosAiresTimeZone).ToString("yyyy-MM-dd HH:mm")
            }))
        };

        if (actor.Permissions.Contains(PermissionCodes.FinancesRead))
        {
            var receivables = await dbContext.FinancialMovements.AsNoTracking()
                .Where(x => x.ClientId == clientId && x.Activo && x.MovementType == FinancialMovementType.Ingreso
                    && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido))
                .Select(x => new { x.Currency, x.Amount, Due = x.DueDate ?? x.MovementDate }).ToListAsync(ct);
            var lastPayment = await dbContext.FinancialMovements.AsNoTracking()
                .Where(x => x.ClientId == clientId && x.Activo && x.MovementType == FinancialMovementType.Ingreso && x.Status == FinancialMovementStatus.Cobrado)
                .OrderByDescending(x => x.SettlementDate ?? x.MovementDate).Select(x => (DateOnly?)(x.SettlementDate ?? x.MovementDate)).FirstOrDefaultAsync(ct);
            output["receivables"] = Node(receivables.GroupBy(x => x.Currency).Select(g => new
            {
                currency = g.Key, total = g.Sum(x => x.Amount), overdue = g.Where(x => x.Due < today).Sum(x => x.Amount), count = g.Count(), oldestDue = g.Min(x => x.Due)
            }));
            output["lastPaymentDate"] = lastPayment?.ToString("yyyy-MM-dd");
        }

        return (Wrap("clients.get", now, new { clientId }, output, [], null, null), Source("clients.get", "Kodvian.Core.Application.Clients", now, null, null, null, "/clientes"));
    }

    private static JsonNode? Node(object value) => System.Text.Json.JsonSerializer.SerializeToNode(value);

    private static bool? GetBool(JsonObject args, string property)
    {
        if (!args.TryGetPropertyValue(property, out var value) || value is null) return null;
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var flag)) return flag;
        throw new AiToolRejectedException($"El parámetro {property} no es válido.");
    }

    private static JsonObject BooleanSchema() => new() { ["type"] = "boolean" };
}
