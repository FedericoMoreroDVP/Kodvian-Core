using Kodvian.Core.Application.Auth.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Auth;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

public sealed class PasswordHasherAndLoginTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly PasswordHasherService passwords = new();

    public PasswordHasherAndLoginTests() => db.Database.EnsureCreated();

    [Fact]
    public void HashAndVerificationAcceptOnlyTheOriginalPassword()
    {
        var hash = passwords.HashPassword("correct-password");

        Assert.NotEqual("correct-password", hash);
        Assert.True(passwords.VerifyPassword(hash, "correct-password"));
        Assert.False(passwords.VerifyPassword(hash, "incorrect-password"));
    }

    [Fact]
    public async Task LoginUsesPasswordHasherToAcceptCorrectPasswordAndRejectIncorrectPassword()
    {
        var user = new User
        {
            FullName = "Test User",
            Email = "user@example.test",
            PasswordHash = passwords.HashPassword("correct-password")
        };
        user.UserRoles.Add(new UserRole { Role = db.Roles.Single(role => role.Name == RoleNames.Administrator) });
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var auth = new AuthService(db, passwords, new TokenService(Options.Create(new JwtOptions
        {
            Issuer = "tests",
            Audience = "tests",
            Key = new string('k', 64),
            ExpirationMinutes = 10
        })));

        var rejected = await auth.LoginAsync(new LoginRequestDto { Email = user.Email, Password = "incorrect-password" });
        var accepted = await auth.LoginAsync(new LoginRequestDto { Email = user.Email.ToUpperInvariant(), Password = "correct-password" });

        Assert.Null(rejected);
        Assert.NotNull(accepted);
        Assert.Equal(user.Id, accepted.User.Id);
    }

    public void Dispose() => db.Dispose();
}
