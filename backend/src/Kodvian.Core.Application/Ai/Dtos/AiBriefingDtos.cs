namespace Kodvian.Core.Application.Ai.Dtos;

/// <summary>Resumen que se muestra al abrir Oficina Inteligente. Se calcula sin IA (no consume tokens).</summary>
public sealed class AiBriefingDto
{
    public DateOnly Date { get; init; }
    public string? Greeting { get; init; }
    public AiBriefingTasksDto? Tasks { get; init; }
    public AiBriefingMeetingsDto? Meetings { get; init; }
    public IReadOnlyCollection<AiBriefingMoneyDto>? Collections { get; init; }
    public IReadOnlyCollection<AiBriefingProjectDto>? ProjectsAtRisk { get; init; }
    public IReadOnlyCollection<AiAlertDto> Alerts { get; init; } = [];
}

public sealed class AiBriefingTasksDto
{
    public int Overdue { get; init; }
    public int DueToday { get; init; }
    public int DueThisWeek { get; init; }
    public IReadOnlyCollection<AiBriefingItemDto> TopOverdue { get; init; } = [];
}

public sealed class AiBriefingMeetingsDto
{
    public int Today { get; init; }
    public int ThisWeek { get; init; }
    public IReadOnlyCollection<AiBriefingItemDto> Next { get; init; } = [];
}

public sealed class AiBriefingMoneyDto
{
    public string Currency { get; init; } = "ARS";
    public decimal Pending { get; init; }
    public decimal Overdue { get; init; }
    public int Count { get; init; }
}

public sealed class AiBriefingProjectDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Progress { get; init; }
    public DateOnly? EstimatedDelivery { get; init; }
    public int? DaysToDelivery { get; init; }
}

public sealed class AiBriefingItemDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Detail { get; init; }
}

public sealed class AiAlertDto
{
    /// <summary>overdue_tasks, project_risk, expense_spike u overdue_collections.</summary>
    public string Kind { get; init; } = string.Empty;
    /// <summary>info, warning o critical.</summary>
    public string Severity { get; init; } = "warning";
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    /// <summary>Consulta sugerida para pedirle al asistente que ayude con la alerta.</summary>
    public string Prompt { get; init; } = string.Empty;
    public string? Link { get; init; }
}
