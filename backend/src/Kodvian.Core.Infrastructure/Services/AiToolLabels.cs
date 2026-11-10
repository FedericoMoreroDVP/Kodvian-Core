namespace Kodvian.Core.Infrastructure.Services;

/// <summary>Textos de progreso que ve el usuario mientras el asistente usa cada herramienta.</summary>
public static class AiToolLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["dashboard.get_snapshot"] = "Revisando los indicadores…",
        ["finance.get_summary"] = "Consultando finanzas…",
        ["finance.list_movements"] = "Buscando movimientos…",
        ["finance.list_pending"] = "Revisando cobros y pagos pendientes…",
        ["finance.compare_months"] = "Comparando meses…",
        ["my_day.get"] = "Armando tu día…",
        ["projects.list"] = "Buscando proyectos…",
        ["projects.get"] = "Revisando el proyecto…",
        ["tasks.list"] = "Consultando tareas…",
        ["clients.list"] = "Buscando clientes…",
        ["clients.get"] = "Revisando el cliente…",
        ["meetings.list"] = "Revisando la agenda…",
        ["people.list"] = "Buscando personas…",
        ["tasks.create"] = "Preparando la tarea…",
        ["tasks.update_status"] = "Preparando el cambio de estado…",
        ["tasks.update"] = "Preparando el cambio de la tarea…",
        ["meetings.create"] = "Preparando la reunión…",
        ["meetings.update"] = "Preparando el cambio de la reunión…",
        ["meetings.cancel"] = "Preparando la cancelación…",
        ["finance.create_movement"] = "Preparando el movimiento…",
        ["clients.create_prospect"] = "Preparando el prospecto…"
    };

    public static string Progress(string toolName) => Labels.TryGetValue(toolName, out var label) ? label : "Consultando datos…";
}
