namespace Kodvian.Core.Application.Common.Security;

public static class RolePermissionMap
{
    public static IReadOnlyCollection<string> GetPermissions(IEnumerable<string> roles) =>
        roles.SelectMany(GetPermissions).Distinct().OrderBy(x => x).ToArray();

    public static readonly string[] AvailableRoles =
        [RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer, RoleNames.Operative, RoleNames.ReadOnly];

    public static string[] ValidateRoles(IEnumerable<string>? roles)
    {
        var result = roles?.Distinct().ToArray() ?? [];
        if (result.Length == 0 || result.Any(x => !AvailableRoles.Contains(x)))
            throw new ArgumentException("Selecciona al menos un rol válido");
        if (result.Contains(RoleNames.ReadOnly) && result.Length != 1)
            throw new ArgumentException("Solo lectura no se puede combinar con otros roles");
        return result;
    }

    public static IReadOnlyCollection<string> GetPermissions(string roleName)
    {
        return roleName switch
        {
            RoleNames.Administrator =>
            [
                PermissionCodes.DashboardRead,
                PermissionCodes.ClientsRead,
                PermissionCodes.ClientsWrite,
                PermissionCodes.ProjectsRead,
                PermissionCodes.ProjectsWrite,
                PermissionCodes.ProjectsDocumentsRead,
                PermissionCodes.ProjectsDocumentsWrite,
                PermissionCodes.ProjectsDocumentsDelete,
                PermissionCodes.TasksRead,
                PermissionCodes.TasksWrite,
                PermissionCodes.TeamRead,
                PermissionCodes.TeamWrite,
                PermissionCodes.FinancesRead,
                PermissionCodes.FinancesWrite,
                PermissionCodes.AdministrationRead,
                PermissionCodes.AdministrationWrite
            ],
            RoleNames.Operative =>
            [
                PermissionCodes.ClientsRead,
                PermissionCodes.ClientsWrite,
                PermissionCodes.ProjectsRead,
                PermissionCodes.ProjectsWrite,
                PermissionCodes.ProjectsDocumentsRead,
                PermissionCodes.ProjectsDocumentsWrite,
                PermissionCodes.ProjectsDocumentsDelete,
                PermissionCodes.TasksRead,
                PermissionCodes.TasksWrite,
                PermissionCodes.TeamRead,
                PermissionCodes.TeamWrite
            ],
            RoleNames.ReadOnly =>
            [
                PermissionCodes.ClientsRead,
                PermissionCodes.ProjectsRead,
                PermissionCodes.ProjectsDocumentsRead,
                PermissionCodes.TasksRead,
                PermissionCodes.TeamRead,
                PermissionCodes.AdministrationRead
            ],
            RoleNames.Analyst =>
            [
                PermissionCodes.ClientsRead,
                PermissionCodes.ClientsWrite,
                PermissionCodes.ProjectsRead,
                PermissionCodes.ProjectsWrite,
                PermissionCodes.ProjectsDocumentsRead,
                PermissionCodes.ProjectsDocumentsWrite,
                PermissionCodes.ProjectsDocumentsDelete,
                PermissionCodes.TasksRead,
                PermissionCodes.TasksWrite,
                PermissionCodes.TeamRead,
                PermissionCodes.TeamWrite
            ],
            RoleNames.Developer =>
            [
                PermissionCodes.DeveloperWorkRead,
                PermissionCodes.DeveloperTasksStatusWrite
            ],
            _ => Array.Empty<string>()
        };
    }
}
