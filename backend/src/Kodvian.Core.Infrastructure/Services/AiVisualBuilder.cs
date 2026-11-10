using System.Globalization;
using System.Text.Json.Nodes;

namespace Kodvian.Core.Infrastructure.Services;

/// <summary>
/// Arma el gráfico "ingresos vs gastos" que acompaña a las respuestas financieras.
/// Un grupo por moneda (ARS y USD nunca se mezclan en el mismo gráfico).
/// Formato: { title, groups: [{ currency, labels: ["Ingresos","Gastos"], datasets: [{ label, values: [ingresos, gastos] }] }] }
/// </summary>
public static class AiVisualBuilder
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-AR");
    private static readonly string[] Labels = ["Ingresos", "Gastos"];

    public static JsonObject? TryBuild(string toolName, JsonObject toolData)
    {
        var result = toolData["result"];
        try
        {
            return toolName switch
            {
                "finance.get_summary" => FromCurrencies(result?["currencies"] as JsonArray, PeriodLabel(toolData["scope"]?["from"], toolData["scope"]?["to"]), "Ingresos y gastos del período"),
                "dashboard.get_snapshot" => FromCurrencies(result?["kpis"]?["currencies"] as JsonArray, MonthLabel(result?["period"]?["year"], result?["period"]?["month"]), "Ingresos y gastos del mes"),
                "finance.compare_months" => FromComparison(result),
                _ => null
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static JsonObject? FromCurrencies(JsonArray? currencies, string periodLabel, string title)
    {
        if (currencies is null) return null;
        var groups = new JsonArray();
        foreach (var currency in currencies.OfType<JsonObject>())
        {
            var income = Number(currency["Income"] ?? currency["income"]);
            var expense = Number(currency["Expense"] ?? currency["expense"]);
            if (income == 0 && expense == 0) continue;
            groups.Add(Group(Text(currency["Currency"] ?? currency["currency"]), new JsonObject { ["label"] = periodLabel, ["values"] = new JsonArray(income, expense) }));
        }
        return groups.Count == 0 ? null : new JsonObject { ["title"] = title, ["groups"] = groups };
    }

    private static JsonObject? FromComparison(JsonNode? result)
    {
        if (result?["differences"] is not JsonArray differences) return null;
        var currentLabel = MonthLabel(result["period"]?["year"], result["period"]?["month"]);
        var comparedLabel = MonthLabel(result["comparedWith"]?["year"], result["comparedWith"]?["month"]);
        var groups = new JsonArray();
        foreach (var item in differences.OfType<JsonObject>())
        {
            var values = new[] { Number(item["income"]?["current"]), Number(item["expense"]?["current"]), Number(item["income"]?["compared"]), Number(item["expense"]?["compared"]) };
            if (values.All(x => x == 0)) continue;
            groups.Add(Group(Text(item["currency"]),
                new JsonObject { ["label"] = comparedLabel, ["values"] = new JsonArray(values[2], values[3]) },
                new JsonObject { ["label"] = currentLabel, ["values"] = new JsonArray(values[0], values[1]) }));
        }
        return groups.Count == 0 ? null : new JsonObject { ["title"] = $"{comparedLabel} vs {currentLabel}", ["groups"] = groups };
    }

    private static JsonObject Group(string currency, params JsonObject[] datasets) => new()
    {
        ["currency"] = currency,
        ["labels"] = new JsonArray(Labels.Select(x => (JsonNode?)x).ToArray()),
        ["datasets"] = new JsonArray(datasets.Select(x => (JsonNode?)x).ToArray())
    };

    private static decimal Number(JsonNode? node) =>
        node is JsonValue value && decimal.TryParse(value.ToJsonString().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Math.Round(number, 2) : 0;

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "ARS";

    private static string MonthLabel(JsonNode? year, JsonNode? month) =>
        year is JsonValue y && month is JsonValue m && y.TryGetValue<int>(out var yy) && m.TryGetValue<int>(out var mm)
            ? new DateTime(yy, mm, 1).ToString("MMM yyyy", Spanish)
            : "Período";

    private static string PeriodLabel(JsonNode? from, JsonNode? to) =>
        from is JsonValue f && to is JsonValue t && f.TryGetValue<string>(out var a) && t.TryGetValue<string>(out var b) ? $"{a} a {b}" : "Período";
}
