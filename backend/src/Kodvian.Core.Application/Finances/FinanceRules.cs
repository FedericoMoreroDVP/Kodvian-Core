namespace Kodvian.Core.Application.Finances;

public static class FinanceRules
{
    public static readonly string[] Currencies = ["ARS", "USD"];
    public static readonly string[] Natures = ["Operacion", "AporteSocio", "RetiroSocio", "ReintegroSocio", "CambioMoneda"];
    public static readonly string[] FundingSources = ["Empresa", "SocioAporte", "SocioReintegrable"];
    public static readonly string[] DetailViews = ["OperationalIncome", "OperationalExpense", "OperationalResult", "RecordedCash", "PendingIncome", "PendingExpense",
        "PartnerContributions", "PartnerWithdrawals", "PartnerReimbursableExpenses", "PartnerReimbursements", "PartnerOutstanding"];
    public static string Currency(string? value) => Currencies.Contains(value) ? value! : throw new ArgumentException("Selecciona ARS o USD");
    public static decimal Money(decimal value)
    {
        if (value <= 0 || value > 9999999999999999.99m || decimal.Round(value, 2) != value)
            throw new ArgumentException("El importe debe ser positivo y tener como máximo dos decimales");
        return value;
    }
    public static void Date(DateOnly date)
    {
        if (date.Year is < 2000 or > 2100) throw new ArgumentException("La fecha debe estar entre 2000 y 2100");
    }
}
