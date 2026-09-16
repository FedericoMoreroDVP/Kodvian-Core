using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Kodvian.Core.Application.Administration;
using Kodvian.Core.Application.Administration.Dtos;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Auth.Dtos;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Auth;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

public class UserRolesTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Actor actor = new();
    private readonly Passwords passwords = new();
    private readonly User administrator;
    private UserAccessGuard Guard => new(db, actor);
    private UserAdministrationService Service => new(db, Guard);

    public UserRolesTests()
    {
        db.Database.EnsureCreated();
        administrator = AddUser(RoleNames.Administrator);
        ActAs(administrator);
    }

    private User AddUser(params string[] roles)
    {
        var user = new User { FullName = "Persona", Email = $"{Guid.NewGuid():N}@example.test", PasswordHash = passwords.HashPassword("password123") };
        foreach (var name in roles) user.UserRoles.Add(new UserRole { Role = db.Roles.Single(r => r.Name == name) });
        db.Users.Add(user); db.SaveChanges(); return user;
    }
    private void ActAs(User user) { actor.UserId = user.Id; actor.SessionVersion = user.SessionVersion; }
    private Task<UserListItemDto?> Change(User user, params string[] roles) =>
        Service.UpdateRolesAsync(user.Id, new UserRolesUpdateRequestDto { Roles = roles, ExpectedVersion = user.SessionVersion }, default);

    [Theory]
    [InlineData(RoleNames.Administrator, RoleNames.Developer)]
    [InlineData(RoleNames.Administrator, RoleNames.Analyst)]
    [InlineData(RoleNames.Analyst, RoleNames.Developer)]
    public void CombinedRolesUnionPermissionsWithoutDuplicates(string first, string second)
    {
        var result = RolePermissionMap.GetPermissions(new[] { first, second });
        Assert.All(RolePermissionMap.GetPermissions(first), p => Assert.Contains(p, result));
        Assert.All(RolePermissionMap.GetPermissions(second), p => Assert.Contains(p, result));
        Assert.Equal(result.Count, result.Distinct().Count());
    }

    [Fact]
    public void RejectsEmptyUnknownAndReadOnlyCombinations()
    {
        Assert.Throws<ArgumentException>(() => RolePermissionMap.ValidateRoles([]));
        Assert.Throws<ArgumentException>(() => RolePermissionMap.ValidateRoles(["SuperAdmin"]));
        Assert.Throws<ArgumentException>(() => RolePermissionMap.ValidateRoles([RoleNames.ReadOnly, RoleNames.Administrator]));
        Assert.Single(RolePermissionMap.ValidateRoles([RoleNames.ReadOnly]));
    }

    [Fact]
    public async Task DeveloperToAnalystPreservesIdentityAssignmentsAndPassword()
    {
        var user = AddUser(RoleNames.Developer);
        user.Developer = new Developer { FullName = user.FullName, Email = user.Email };
        db.Developers.Add(user.Developer);
        db.SaveChanges();
        var profileId = user.DeveloperId;
        var task = new TaskItem { Titulo = "Trabajo", DeveloperId = profileId, CreadoPorId = user.Id };
        var assignment = new ProjectDeveloperAssignment { DeveloperId = profileId!.Value, ProjectId = Guid.NewGuid() };
        db.Tasks.Add(task); db.ProjectDeveloperAssignments.Add(assignment); db.SaveChanges();
        var oldVersion = user.SessionVersion;
        var result = await Change(user, RoleNames.Analyst);
        Assert.Equal(new[] { RoleNames.Analyst }, result!.Roles);
        Assert.Equal(profileId, result.DeveloperId);
        Assert.Equal(profileId, (await db.Tasks.SingleAsync()).DeveloperId);
        Assert.Equal(profileId, (await db.ProjectDeveloperAssignments.SingleAsync()).DeveloperId);
        Assert.Equal(passwords.HashPassword("password123"), user.PasswordHash);
        Assert.NotEqual(oldVersion, user.SessionVersion);
        var validator = new SessionValidator(db);
        Assert.False(await validator.IsValidAsync(user.Id, oldVersion, default));
        Assert.True(await validator.IsValidAsync(user.Id, user.SessionVersion, default));
    }

    [Fact]
    public async Task AdministratorCanHaveAllThreeRolesAndOneProfile()
    {
        var result = await Change(administrator, RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer);
        Assert.Equal(3, result!.Roles.Count);
        Assert.NotNull(result.DeveloperId);
        Assert.Single(db.Developers);
        ActAs(administrator);
        await Change(administrator, RoleNames.Administrator, RoleNames.Analyst);
        Assert.Single(db.Developers);
    }

    [Fact]
    public async Task LinksMatchingExternalProfileAndRejectsAmbiguousMatch()
    {
        var user = AddUser(RoleNames.Operative);
        var profile = new Developer { Email = user.Email, FullName = user.FullName };
        db.Developers.Add(profile); db.SaveChanges();
        Assert.Equal(profile.Id, (await Change(user, RoleNames.Developer))!.DeveloperId);
        var second = AddUser(RoleNames.Operative);
        db.Developers.AddRange(new Developer { Email = second.Email }, new Developer { Email = second.Email }); db.SaveChanges();
        await Assert.ThrowsAsync<ArgumentException>(() => Change(second, RoleNames.Analyst));
        Assert.Null(second.DeveloperId);
    }

    [Fact]
    public async Task NonAdministratorCannotListOrChangeRoles()
    {
        var analyst = AddUser(RoleNames.Analyst); ActAs(analyst);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service.GetAsync(new(), default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Change(analyst, RoleNames.Administrator));
        Assert.DoesNotContain(analyst.UserRoles, r => r.Role.Name == RoleNames.Administrator);
    }

    [Fact]
    public async Task CannotRemoveLastAdministratorOrUseStaleVersion()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Change(administrator, RoleNames.Developer));
        var target = AddUser(RoleNames.Developer);
        await Assert.ThrowsAsync<ArgumentException>(() => Service.UpdateRolesAsync(target.Id,
            new() { Roles = [RoleNames.Analyst], ExpectedVersion = Guid.NewGuid() }, default));
        Assert.Contains(target.UserRoles, r => r.Role.Name == RoleNames.Developer);
    }

    [Fact]
    public async Task RevokedAdministratorCannotPerformSubsequentMutation()
    {
        var second = AddUser(RoleNames.Administrator);
        var oldVersion = second.SessionVersion;
        await Change(second, RoleNames.Analyst);
        actor.UserId = second.Id; actor.SessionVersion = oldVersion;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Change(administrator, RoleNames.Developer));
    }

    [Fact]
    public async Task NonAdministratorCannotEditAdministratorThroughEitherTeamService()
    {
        var target = AddUser(RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer);
        target.Developer = new Developer { FullName = target.FullName, Email = target.Email }; db.Developers.Add(target.Developer); db.SaveChanges();
        ActAs(AddUser(RoleNames.Analyst));
        var teams = new TeamUserService(db, passwords, Guard);
        var developers = new DeveloperService(db, passwords, Guard);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => teams.UpdateAnalystAsync(target.Id,
            new() { FullName = "Alterado", Email = "another@example.test", Password = "password456", IsActive = false }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => developers.UpdateAsync(target.DeveloperId!.Value,
            new() { FullName = "Alterado", Email = "another@example.test", AccessPassword = "password456", IsActive = false }));
        Assert.Equal("Persona", target.FullName); Assert.True(target.Activo);
    }

    [Fact]
    public async Task TeamEditsPreserveAllRolesAndSynchronizeProfile()
    {
        var target = AddUser(RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer);
        target.Developer = new Developer { FullName = target.FullName, Email = target.Email }; db.Developers.Add(target.Developer); db.SaveChanges();
        var profileId = target.DeveloperId!.Value;
        await new DeveloperService(db, passwords, Guard).UpdateAsync(profileId,
            new() { FullName = "Nombre nuevo", Email = target.Email, EnableSystemAccess = true, IsActive = true, IsSystemAccessActive = true });
        Assert.Equal(3, target.UserRoles.Count); Assert.Equal("Nombre nuevo", target.FullName);
        await new TeamUserService(db, passwords, Guard).UpdateAnalystAsync(target.Id,
            new() { FullName = "Nombre analista", Email = target.Email, IsActive = true });
        Assert.Equal(3, target.UserRoles.Count); Assert.Equal("Nombre analista", target.Developer!.FullName);
        Assert.Equal(profileId, target.DeveloperId);
        Assert.Contains(await new DeveloperService(db, passwords, Guard).GetAllAsync(), d => d.Id == profileId);
        Assert.Contains(await new TeamUserService(db, passwords, Guard).GetAnalystsAsync(), u => u.Id == target.Id);
    }

    [Fact]
    public async Task LastAdministratorCannotBeDeactivatedFromTeam()
    {
        await Change(administrator, RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer);
        ActAs(administrator);
        await Assert.ThrowsAsync<ArgumentException>(() => new DeveloperService(db, passwords, Guard).UpdateAsync(administrator.DeveloperId!.Value,
            new() { FullName = administrator.FullName, Email = administrator.Email, EnableSystemAccess = false, IsActive = true }));
        db.ChangeTracker.Clear();
        var persisted = await db.Users.SingleAsync(x => x.Id == administrator.Id);
        Assert.True(persisted.Activo);
    }

    [Fact]
    public async Task LastAdministratorCannotBeDeactivatedFromAnalystsEvenWithInactiveAdminPresent()
    {
        await Change(administrator, RoleNames.Administrator, RoleNames.Analyst);
        var inactive = AddUser(RoleNames.Administrator); inactive.Activo = false; db.SaveChanges();
        ActAs(administrator);
        await Assert.ThrowsAsync<ArgumentException>(() => new TeamUserService(db, passwords, Guard).UpdateAnalystAsync(administrator.Id,
            new() { FullName = administrator.FullName, Email = administrator.Email, IsActive = false }));
        db.ChangeTracker.Clear();
        Assert.True((await db.Users.SingleAsync(x => x.Id == administrator.Id)).Activo);
    }

    [Fact]
    public async Task DisablingDeveloperAccessSynchronizesEmailWithoutChangingRoles()
    {
        var target = AddUser(RoleNames.Analyst, RoleNames.Developer);
        target.Developer = new Developer { FullName = target.FullName, Email = target.Email };
        db.Developers.Add(target.Developer); db.SaveChanges();
        await new DeveloperService(db, passwords, Guard).UpdateAsync(target.DeveloperId!.Value,
            new() { FullName = "Actualizado", Email = "changed@example.test", EnableSystemAccess = false, IsActive = true });
        Assert.False(target.Activo);
        Assert.Equal("changed@example.test", target.Email);
        Assert.Equal(target.Email, target.Developer.Email);
        Assert.Equal(2, target.UserRoles.Count);
    }

    [Fact]
    public async Task AnalystsWithOtherRolesAreEligibleAndExistingAssignmentsSurviveRoleRemoval()
    {
        var target = AddUser(RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer);
        target.Developer = new Developer { FullName = target.FullName }; db.Developers.Add(target.Developer); db.SaveChanges();
        var client = new Client { CommercialName = "Cliente" };
        var project = new Project { Cliente = client, ResponsableId = target.Id, Nombre = "Proyecto" };
        db.Projects.Add(project); db.SaveChanges();
        var service = new ProjectService(db, new NoStorage(), Options.Create(new StorageOptions()));
        Assert.Contains((await service.GetLookupsAsync()).Responsibles, r => r.Id == target.Id);
        await Change(target, RoleNames.Developer);
        Assert.DoesNotContain((await service.GetLookupsAsync()).Responsibles, r => r.Id == target.Id);
        await service.UpdateAsync(project.Id, new() { Name = "Editado", ClientId = client.Id, ResponsibleId = target.Id });
        Assert.Equal(target.Id, project.ResponsableId);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(new() { Name = "Nuevo", ClientId = client.Id, ResponsibleId = target.Id }));
    }

    [Fact]
    public async Task LoginAndTokenContainAllRolesAndSessionVersion()
    {
        await Change(administrator, RoleNames.Administrator, RoleNames.Analyst, RoleNames.Developer);
        var tokens = new TokenService(Options.Create(new JwtOptions { Issuer = "tests", Audience = "tests", Key = new string('k', 64), ExpirationMinutes = 10 }));
        var auth = new AuthService(db, passwords, tokens);
        var login = await auth.LoginAsync(new LoginRequestDto { Email = administrator.Email, Password = "password123" });
        Assert.NotNull(login);
        Assert.Equal(3, login.User.Roles.Count);
        Assert.Contains(PermissionCodes.FinancesWrite, login.User.Permissions);
        Assert.Contains(PermissionCodes.DeveloperWorkRead, login.User.Permissions);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal(3, jwt.Claims.Count(c => c.Type == ClaimTypes.Role));
        Assert.Contains(jwt.Claims, c => c.Type == CustomClaimTypes.SessionVersion && c.Value == administrator.SessionVersion.ToString());
        Assert.Equal(3, (await auth.GetCurrentUserAsync(administrator.Id))!.Roles.Count);
    }

    [Fact]
    public async Task ListingIsPaginatedAndSearchable()
    {
        AddUser(RoleNames.Developer); var target = AddUser(RoleNames.Analyst);
        var result = await Service.GetAsync(new() { PageNumber = 1, PageSize = 1 }, default);
        Assert.Single(result.Items); Assert.Equal(3, result.TotalCount);
        Assert.Single((await Service.GetAsync(new() { Search = target.Email }, default)).Items);
    }

    public void Dispose() => db.Dispose();
    private sealed class Actor : ICurrentUser { public Guid? UserId { get; set; } public Guid? SessionVersion { get; set; } }
    private sealed class Passwords : IPasswordHasher
    {
        public string HashPassword(string password) => "hashed:" + password;
        public bool VerifyPassword(string hash, string password) => hash == HashPassword(password);
    }
    private sealed class NoStorage : IFileStorageService
    {
        public Task<string> SaveAsync(byte[] content, string extension, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> ReadAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
