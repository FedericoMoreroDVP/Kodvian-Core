using System.Net.Mail;
using Kodvian.Core.Application.Clients.Requests;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Application.Finances.Requests;
using Kodvian.Core.Application.Meetings.Requests;
using Kodvian.Core.Application.Projects.Requests;
using Kodvian.Core.Application.Tasks.Requests;

namespace Kodvian.Core.Api.Validation;

internal static class RequestValidation
{
    public static string? Validate(ClientUpsertRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.CommercialName))
        {
            return "El nombre comercial es obligatorio";
        }

        if (!string.IsNullOrWhiteSpace(request.ContactEmail) && !IsValidEmail(request.ContactEmail))
        {
            return "Ingresa un correo electrónico válido";
        }

        if (!IsAllowed(request.Status, "Prospecto", "Activo", "Pausado", "Finalizado", "Presupuestado"))
        {
            return "El estado del cliente no es válido";
        }

        if (request.BillingDay is < 1 or > 31)
        {
            return "El día de cobro debe estar entre 1 y 31";
        }

        if (request.MonthlyAmount < 0)
        {
            return "El monto mensual no puede ser negativo";
        }

        return null;
    }

    public static string? Validate(ProjectUpsertRequestDto request)
    {
        if (request.ClientId == Guid.Empty)
        {
            return "El cliente es obligatorio";
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "El nombre del proyecto es obligatorio";
        }

        if (!string.IsNullOrWhiteSpace(request.CalendarColor) && !System.Text.RegularExpressions.Regex.IsMatch(request.CalendarColor, "^#[0-9A-Fa-f]{6}$"))
        {
            return "El color de calendario no es válido";
        }

        if (!IsAllowed(request.Status, "Planificacion", "EnCurso", "Pausado", "Finalizado", "Cancelado", "Presupuestado"))
        {
            return "El estado del proyecto no es válido";
        }

        if (!IsAllowed(request.Priority, "Baja", "Media", "Alta", "Urgente"))
        {
            return "La prioridad del proyecto no es válida";
        }

        if (request.ProgressPercentage is < 0 or > 100)
        {
            return "El porcentaje de avance debe estar entre 0 y 100";
        }

        if (request.Budget < 0)
        {
            return "El presupuesto no puede ser negativo";
        }

        if (request.StartDate.HasValue && request.EstimatedDeliveryDate.HasValue && request.EstimatedDeliveryDate < request.StartDate)
        {
            return "La fecha estimada de entrega no puede ser anterior a la fecha de inicio";
        }

        if (request.ClosingDate.HasValue && request.StartDate.HasValue && request.ClosingDate < request.StartDate)
        {
            return "La fecha de cierre no puede ser anterior a la fecha de inicio";
        }

        return null;
    }

    public static string? Validate(TaskUpsertRequestDto request)
    {
        if (request.ProjectId == Guid.Empty)
        {
            return "El proyecto es obligatorio";
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return "El título es obligatorio";
        }

        if (!IsAllowed(request.Status, "Pendiente", "EnCurso", "Bloqueada", "Finalizada", "Cancelada"))
        {
            return "El estado de la tarea no es válido";
        }

        if (!IsAllowed(request.Priority, "Baja", "Media", "Alta", "Urgente"))
        {
            return "La prioridad de la tarea no es válida";
        }

        if (request.EstimatedHours < 0)
        {
            return "Las horas estimadas no pueden ser negativas";
        }

        if (request.RealHours < 0)
        {
            return "Las horas reales no pueden ser negativas";
        }

        if (request.DueDate.HasValue && request.StartDate.HasValue && request.DueDate < request.StartDate)
        {
            return "La fecha de vencimiento no puede ser anterior a la fecha de inicio";
        }

        return null;
    }

    public static string? Validate(TaskStatusUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return "El estado es obligatorio";
        }

        if (!IsAllowed(request.Status, "Pendiente", "EnCurso", "Bloqueada", "Finalizada", "Cancelada"))
        {
            return "El estado de la tarea no es válido";
        }

        if (request.KanbanOrder < 0)
        {
            return "El orden del tablero no puede ser negativo";
        }

        return null;
    }

    public static string? Validate(ChangeClientStatusRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return "El estado es obligatorio";
        }

        if (!IsAllowed(request.Status, "Prospecto", "Activo", "Pausado", "Finalizado", "Presupuestado"))
        {
            return "El estado del cliente no es válido";
        }

        return null;
    }

    public static string? Validate(FinancialMovementUpsertRequestDto request)
    {
        if (request.CategoryId == Guid.Empty)
        {
            return "La categoría es obligatoria";
        }

        if (request.Amount <= 0)
        {
            return "El monto debe ser mayor a cero";
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return "La descripción es obligatoria";
        }

        if (!IsAllowed(request.MovementType, "Ingreso", "Egreso"))
        {
            return "El tipo de movimiento no es válido";
        }

        if (!IsAllowed(request.Status, "Pendiente", "Cobrado", "Pagado", "Vencido", "Anulado"))
        {
            return "El estado del movimiento no es válido";
        }

        if (request.DueDate.HasValue && request.DueDate < request.MovementDate)
        {
            return "La fecha de vencimiento no puede ser anterior a la fecha de movimiento";
        }

        return null;
    }

    public static string? Validate(FinancialCategoryUpsertRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "La categoría es obligatoria";
        }

        if (!IsAllowed(request.MovementType, "Ingreso", "Egreso"))
        {
            return "El tipo de movimiento no es válido";
        }

        return null;
    }

    public static string? Validate(ProviderUpsertRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "El nombre del proveedor es obligatorio";
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !IsValidEmail(request.Email))
        {
            return "Ingresa un correo electrónico válido";
        }

        return null;
    }

    public static string? Validate(MeetingUpsertRequestDto request)
    {
        if (request.ProjectId == Guid.Empty) return "El proyecto es obligatorio";
        if (string.IsNullOrWhiteSpace(request.Title)) return "El título es obligatorio";
        if (request.Title.Trim().Length > 200) return "El título no puede superar los 200 caracteres";
        if (request.Description?.Length > 2000) return "La descripción no puede superar los 2000 caracteres";
        if (request.Link?.Length > 2048) return "El enlace no puede superar los 2048 caracteres";
        if (request.Location?.Length > 300) return "La ubicación no puede superar los 300 caracteres";
        if (request.EndsAt <= request.StartsAt) return "La hora de finalización debe ser posterior al inicio";
        if (!IsAllowed(request.Priority, "Baja", "Media", "Alta", "Urgente")) return "La prioridad de la reunión no es válida";
        if (!IsAllowed(request.Status, "Programada", "Confirmada", "Realizada")) return "El estado de la reunión no es válido";
        if (request.ParticipantIds.Any(x => x == Guid.Empty) || request.ParticipantIds.Distinct().Count() != request.ParticipantIds.Count)
            return "Los participantes de la reunión no son válidos";
        return null;
    }

    public static string? Validate(DeveloperUpsertRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return "El nombre del desarrollador es obligatorio";
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !IsValidEmail(request.Email))
        {
            return "Ingresa un correo electrónico válido";
        }

        if (request.EnableSystemAccess && string.IsNullOrWhiteSpace(request.Email))
        {
            return "El email es obligatorio para habilitar el acceso al sistema";
        }

        if (!string.IsNullOrWhiteSpace(request.AccessPassword) && request.AccessPassword.Length < 8)
        {
            return "La contraseña debe tener al menos 8 caracteres";
        }

        return null;
    }

    public static string? Validate(ProjectDeveloperContractUpsertRequestDto request)
    {
        if (request.DeveloperId == Guid.Empty)
        {
            return "El desarrollador es obligatorio";
        }

        if (!IsAllowed(request.PaymentMode, "Percentage", "FixedAmount"))
        {
            return "La modalidad de cobro no es válida";
        }

        if (string.Equals(request.PaymentMode, "Percentage", StringComparison.OrdinalIgnoreCase)
            && (!request.Percentage.HasValue || request.Percentage <= 0 || request.Percentage > 100))
        {
            return "El porcentaje debe estar entre 0 y 100";
        }

        if (string.Equals(request.PaymentMode, "FixedAmount", StringComparison.OrdinalIgnoreCase)
            && (!request.AgreedAmount.HasValue || request.AgreedAmount <= 0))
        {
            return "El monto acordado debe ser mayor a 0";
        }

        if (request.EndDate.HasValue && request.EndDate < request.StartDate)
        {
            return "La fecha de finalización no puede ser anterior a la fecha de inicio";
        }

        return null;
    }

    public static string? Validate(ProjectDeveloperAssignmentCreateRequestDto request)
    {
        if (request.DeveloperId == Guid.Empty)
        {
            return "El desarrollador es obligatorio";
        }

        return null;
    }

    public static string? Validate(DeveloperPaymentCreateRequestDto request)
    {
        if (request.Amount <= 0)
        {
            return "El monto debe ser mayor a 0";
        }

        if (request.PeriodYear is < 2000 or > 2100)
        {
            return "El período anual no es válido";
        }

        if (request.PeriodMonth is < 1 or > 12)
        {
            return "El período mensual no es válido";
        }

        return null;
    }

    private static bool IsAllowed(string value, params string[] allowed)
        => allowed.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase));

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
