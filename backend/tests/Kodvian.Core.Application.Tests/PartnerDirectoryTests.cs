using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Application.Tests;

public class PartnerDirectoryTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private PartnerDirectoryService Directory => new(db);

    [Fact]
    public async Task PeopleListShowsTeamProfilesAndUnlinkedAccountsWithoutDuplicatingAnalysts()
    {
        var developer = new Developer { FullName = "Desarrollador", Email = "dev@example.test" };
        var analyst = new Developer { FullName = "Analista", Email = "analyst@example.test" };
        var linkedUser = new User { FullName = "Analista", Email = analyst.Email!, Developer = analyst };
        var administrator = new User { FullName = "Administrador", Email = "admin@example.test" };
        db.AddRange(developer, analyst, linkedUser, administrator, new Developer { FullName = "Inactivo", Activo = false });
        await db.SaveChangesAsync();
        var result = await Directory.PeopleAsync(new(), default);
        Assert.Equal(3, result.TotalCount);
        Assert.Single(result.Items.Where(p => p.FullName == "Analista"));
        Assert.Contains(result.Items, p => p.Source == "User" && p.PersonId == administrator.Id);
        Assert.Contains(result.Items, p => p.Source == "Developer" && p.PersonId == analyst.Id);
        var search = await Directory.PeopleAsync(new() { Search = "ANALYST@" }, default);
        Assert.Single(search.Items);
        var pages = new List<Guid>();
        for (var page = 1; page <= 3; page++) pages.Add(Assert.Single((await Directory.PeopleAsync(new() { PageNumber = page, PageSize = 1 }, default)).Items).PersonId);
        Assert.Equal(3, pages.Distinct().Count());
        Assert.Empty((await Directory.PeopleAsync(new() { PageNumber = 4, PageSize = 1 }, default)).Items);
    }

    [Fact]
    public async Task LinkingUsesMasterDataAndPreventsDuplicateProfileOrAccountAlias()
    {
        var profile = new Developer { FullName = "Persona de Equipo", Email = "person@example.test" };
        var user = new User { FullName = "Persona", Email = profile.Email!, Developer = profile };
        db.AddRange(profile, user); await db.SaveChangesAsync();
        var partner = await Directory.SaveAsync(null, new() { Source = "Developer", PersonId = profile.Id, FullName = "No debe copiarse", Email = "wrong@example.test" }, default);
        Assert.Equal(profile.FullName, partner.FullName); Assert.Equal(profile.Email, partner.Email);
        Assert.Equal("Developer", partner.Source); Assert.Equal(profile.Id, partner.PersonId);
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "Developer", PersonId = profile.Id }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "User", PersonId = user.Id }, default));
        var person = Assert.Single((await Directory.PeopleAsync(new(), default)).Items);
        Assert.Equal(partner.Id, person.RegisteredPartnerId);
        await Directory.SaveAsync(partner.Id, new() { IsActive = false }, default);
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "Developer", PersonId = profile.Id }, default));
    }

    [Fact]
    public async Task ManualPartnerCanBeLinkedWithoutLosingMovementsAndFollowsTeamRenames()
    {
        var partner = await Directory.SaveAsync(null, new() { FullName = "Socio escrito a mano" }, default);
        var movement = new FinancialMovement { PartnerId = partner.Id, Amount = 250, Nature = "AporteSocio", Currency = "ARS",
            Status = FinancialMovementStatus.Cobrado, MovementType = FinancialMovementType.Ingreso, SettlementDate = new(2025, 1, 1), Description = "Aporte",
            Category = new FinancialCategory { Name = "Aportes", MovementType = FinancialMovementType.Ingreso },
            CreatedBy = new User { FullName = "Operador", Email = "operator@example.test", PasswordHash = "unused" } };
        var profile = new Developer { FullName = "Nombre real", Email = "real@example.test" };
        db.AddRange(movement, profile); await db.SaveChangesAsync();
        var linked = await Directory.SaveAsync(partner.Id, new() { Source = "Developer", PersonId = profile.Id }, default);
        Assert.Equal(partner.Id, linked.Id); Assert.Equal(partner.Id, movement.PartnerId); Assert.Single(db.Partners);
        profile.FullName = "Nombre actualizado"; profile.Email = "updated@example.test"; await db.SaveChangesAsync();
        var refreshed = Assert.Single(await Directory.ListAsync(default));
        Assert.Equal("Nombre actualizado", refreshed.FullName); Assert.Equal("updated@example.test", refreshed.Email);
        var overview = await new FinanceOverviewService(db, new NoActor()).GetAsync(null, null, default);
        var balance = Assert.Single(overview.Partners);
        Assert.Equal("Nombre actualizado", balance.PartnerName); Assert.Equal(250, balance.Contributions); Assert.Equal(partner.Id, balance.PartnerId);
    }

    [Fact]
    public async Task StandaloneUserCanBeLinkedAndRemainsTheSamePartnerAfterReceivingAProfile()
    {
        var user = new User { FullName = "Administrador", Email = "admin@example.test" };
        db.Users.Add(user); await db.SaveChangesAsync();
        var partner = await Directory.SaveAsync(null, new() { Source = "User", PersonId = user.Id }, default);
        Assert.Equal("User", partner.Source);
        var profile = new Developer { FullName = user.FullName, Email = user.Email }; db.Developers.Add(profile); user.Developer = profile;
        await db.SaveChangesAsync();
        var choice = Assert.Single((await Directory.PeopleAsync(new(), default)).Items);
        Assert.Equal("Developer", choice.Source); Assert.Equal(partner.Id, choice.RegisteredPartnerId);
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "Developer", PersonId = profile.Id }, default));
        var same = await Directory.SaveAsync(partner.Id, new() { IsActive = true }, default);
        Assert.Equal(user.Id, same.PersonId); Assert.Equal("User", same.Source);
    }

    [Fact]
    public async Task NewManualPersonDoesNotCreateAnAccountAndValidatesInput()
    {
        var partner = await Directory.SaveAsync(null, new() { Source = "Manual", FullName = " Persona nueva ", Email = " NEW@example.test " }, default);
        Assert.Equal("Persona nueva", partner.FullName); Assert.Equal("new@example.test", partner.Email); Assert.Equal("Manual", partner.Source);
        Assert.Null(partner.PersonId); Assert.Empty(db.Users); Assert.Empty(db.Developers);
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { FullName = " " }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { FullName = "Persona", Email = "invalid" }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "Other", FullName = "Persona" }, default));
    }

    [Fact]
    public async Task CannotTransferALinkedPartnersHistoryOrSelectMissingInactivePerson()
    {
        var profile = new Developer { FullName = "Persona" }; var inactive = new Developer { FullName = "Inactiva", Activo = false };
        db.AddRange(profile, inactive); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "Developer", PersonId = Guid.NewGuid() }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(null, new() { Source = "Developer", PersonId = inactive.Id }, default));
        var partner = await Directory.SaveAsync(null, new() { Source = "Developer", PersonId = profile.Id }, default);
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(partner.Id, new() { Source = "Developer", PersonId = inactive.Id }, default));
        await Assert.ThrowsAsync<ArgumentException>(() => Directory.SaveAsync(partner.Id, new() { Source = "Manual", FullName = "Otra" }, default));
        profile.Activo = false; await db.SaveChangesAsync();
        var updated = await Directory.SaveAsync(partner.Id, new() { IsActive = false }, default);
        Assert.False(updated.IsActive); Assert.Equal(profile.Id, updated.PersonId);
    }

    public void Dispose() => db.Dispose();
    private sealed class NoActor : ICurrentUser { public Guid? UserId => null; public Guid? SessionVersion => null; }
}
