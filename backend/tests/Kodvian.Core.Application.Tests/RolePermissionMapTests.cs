using Kodvian.Core.Application.Common.Security;

namespace Kodvian.Core.Application.Tests;

public class RolePermissionMapTests
{
    [Fact]
    public void DeveloperRoleOnlyReceivesMyWorkPermissions()
    {
        var permissions = RolePermissionMap.GetPermissions(RoleNames.Developer);

        Assert.Contains(PermissionCodes.DeveloperWorkRead, permissions);
        Assert.Contains(PermissionCodes.DeveloperTasksStatusWrite, permissions);
        Assert.DoesNotContain(PermissionCodes.ProjectsRead, permissions);
        Assert.DoesNotContain(PermissionCodes.TasksWrite, permissions);
        Assert.DoesNotContain(PermissionCodes.FinancesRead, permissions);
        Assert.Contains(PermissionCodes.MeetingsRead, permissions);
        Assert.DoesNotContain(PermissionCodes.MeetingsWrite, permissions);
        Assert.Equal(3, permissions.Count);
    }

    [Fact]
    public void AnalystRoleReceivesOperationalPermissionsWithoutFinances()
    {
        var permissions = RolePermissionMap.GetPermissions(RoleNames.Analyst);

        Assert.Contains(PermissionCodes.ClientsRead, permissions);
        Assert.Contains(PermissionCodes.ClientsWrite, permissions);
        Assert.Contains(PermissionCodes.ProjectsRead, permissions);
        Assert.Contains(PermissionCodes.ProjectsWrite, permissions);
        Assert.Contains(PermissionCodes.ProjectsDocumentsRead, permissions);
        Assert.Contains(PermissionCodes.ProjectsDocumentsWrite, permissions);
        Assert.Contains(PermissionCodes.ProjectsDocumentsDelete, permissions);
        Assert.Contains(PermissionCodes.TasksRead, permissions);
        Assert.Contains(PermissionCodes.TasksWrite, permissions);
        Assert.Contains(PermissionCodes.TeamRead, permissions);
        Assert.Contains(PermissionCodes.TeamWrite, permissions);
        Assert.DoesNotContain(PermissionCodes.FinancesRead, permissions);
        Assert.DoesNotContain(PermissionCodes.FinancesWrite, permissions);
        Assert.DoesNotContain(PermissionCodes.AdministrationRead, permissions);
        Assert.Contains(PermissionCodes.MeetingsRead, permissions);
        Assert.Contains(PermissionCodes.MeetingsWrite, permissions);
    }

    [Theory]
    [InlineData(RoleNames.Operative)]
    [InlineData(RoleNames.ReadOnly)]
    [InlineData(RoleNames.Analyst)]
    [InlineData(RoleNames.Developer)]
    public void NonAdministratorRolesDoNotReceiveFinancialDashboardOrFinancePermissions(string roleName)
    {
        var permissions = RolePermissionMap.GetPermissions(roleName);

        Assert.DoesNotContain(PermissionCodes.DashboardRead, permissions);
        Assert.DoesNotContain(PermissionCodes.FinancesRead, permissions);
        Assert.DoesNotContain(PermissionCodes.FinancesWrite, permissions);
    }
}
