using System.Web;
using Kodvian.Core.Application.Meetings.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.GoogleCalendar;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Application.Tests;

/// <summary>Integración con Google Calendar con un cliente de Google falso (sin red).</summary>
public sealed class GoogleCalendarIntegrationTests
{
    private static readonly GoogleCalendarOptions Configured = new() { ClientId = "client-id", ClientSecret = "client-secret", RedirectUri = "http://localhost:4200/api/integrations/google-calendar/callback" };

    [Fact]
    public async Task WithoutConfigurationTheIntegrationIsDisabledAndMeetingsStillWork()
    {
        await using var db = CreateDatabase();
        var (organizer, project, _) = Seed(db);
        var google = new FakeGoogleClient();
        var calendar = CreateService(db, google, new GoogleCalendarOptions());

        var status = await calendar.GetStatusAsync(organizer.Id, CancellationToken.None);
        Assert.False(status.Enabled);
        Assert.Throws<InvalidOperationException>(() => calendar.BuildAuthorizationUrl(organizer.Id));

        var meeting = await new MeetingService(db, calendar).CreateAsync(organizer.Id, true, Request(project.Id), CancellationToken.None);
        Assert.Null(meeting.GoogleSyncStatus);
        Assert.Empty(google.Created);
    }

    [Fact]
    public async Task ConnectFlowUsesPkceAndStoresTheRefreshTokenEncrypted()
    {
        await using var db = CreateDatabase();
        var (organizer, _, _) = Seed(db);
        var google = new FakeGoogleClient();
        var calendar = CreateService(db, google, Configured);

        var url = new Uri(calendar.BuildAuthorizationUrl(organizer.Id));
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("accounts.google.com", url.Host);
        Assert.Equal("client-id", query["client_id"]);
        Assert.Equal(Configured.RedirectUri, query["redirect_uri"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("offline", query["access_type"]);
        Assert.Contains("https://www.googleapis.com/auth/calendar.events", query["scope"]);

        var result = await calendar.HandleCallbackAsync("auth-code", query["state"], null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(google.LastVerifier));
        var connection = await db.GoogleCalendarConnections.SingleAsync();
        Assert.Equal("nico@gmail.com", connection.GoogleEmail);
        Assert.DoesNotContain("refresh-token", connection.ProtectedRefreshToken);
        var status = await calendar.GetStatusAsync(organizer.Id, CancellationToken.None);
        Assert.True(status.Connected);
        Assert.Equal("nico@gmail.com", status.Email);
    }

    [Theory]
    [InlineData(null, "access_denied", "denied")]
    [InlineData("tampered-state", null, "invalid_state")]
    public async Task InvalidCallbacksAreRejected(string? state, string? error, string reason)
    {
        await using var db = CreateDatabase();
        Seed(db);
        var calendar = CreateService(db, new FakeGoogleClient(), Configured);

        var result = await calendar.HandleCallbackAsync("code", state, error, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(reason, result.Reason);
        Assert.Empty(db.GoogleCalendarConnections);
    }

    [Fact]
    public async Task MeetingsAreCreatedMovedAndCancelledInTheOrganizerCalendar()
    {
        var name = Guid.NewGuid().ToString();
        var protection = new EphemeralDataProtectionProvider();
        await using var db = CreateDatabase(name);
        var (organizer, project, participant) = Seed(db);
        var google = new FakeGoogleClient();
        var calendar = CreateService(db, google, Configured, protection);
        await Connect(calendar, organizer.Id);
        var meetings = new MeetingService(db, calendar);

        var created = await meetings.CreateAsync(organizer.Id, true, Request(project.Id, participant.Id), CancellationToken.None);
        Assert.Equal("Synced", created.GoogleSyncStatus);
        var sent = Assert.Single(google.Created);
        Assert.Equal("Revisión", sent.Summary);
        Assert.Equal(["pedro@example.test"], sent.AttendeeEmails);
        Assert.Equal("America/Argentina/Buenos_Aires", sent.TimeZone);
        Assert.Contains("Proyecto: Portal", sent.Description);
        Assert.Equal("evt-1", db.Meetings.AsNoTracking().Single().GoogleEventId);

        var moved = Request(project.Id, participant.Id);
        moved.StartsAt = moved.StartsAt.AddDays(1);
        moved.EndsAt = moved.EndsAt.AddDays(1);
        // Cada paso usa un DbContext nuevo, como cada solicitud HTTP en la API.
        await using (var second = CreateDatabase(name))
            await new MeetingService(second, CreateService(second, google, Configured, protection)).UpdateAsync(organizer.Id, true, created.Id, moved, CancellationToken.None);
        Assert.Equal("evt-1", Assert.Single(google.Updated).EventId);

        await using (var third = CreateDatabase(name))
            await new MeetingService(third, CreateService(third, google, Configured, protection)).CancelAsync(organizer.Id, true, created.Id, CancellationToken.None);
        Assert.Equal(["evt-1"], google.Deleted);
    }

    [Fact]
    public async Task GoogleFailuresNeverBlockTheMeetingAndCanBeRetried()
    {
        await using var db = CreateDatabase();
        var (organizer, project, _) = Seed(db);
        var google = new FakeGoogleClient { FailCreate = true };
        var calendar = CreateService(db, google, Configured);
        await Connect(calendar, organizer.Id);

        var created = await new MeetingService(db, calendar).CreateAsync(organizer.Id, true, Request(project.Id), CancellationToken.None);

        Assert.Single(db.Meetings);
        Assert.Equal("Failed", created.GoogleSyncStatus);
        Assert.Contains("Google", created.GoogleSyncError);

        google.FailCreate = false;
        var retry = await calendar.RetryMeetingAsync(organizer.Id, false, created.Id, CancellationToken.None);
        Assert.Equal("Synced", retry!.Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => calendar.RetryMeetingAsync(Guid.NewGuid(), false, created.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RevokedAccessMarksTheConnectionAndDisconnectRemovesIt()
    {
        await using var db = CreateDatabase();
        var (organizer, project, _) = Seed(db);
        var google = new FakeGoogleClient();
        var calendar = CreateService(db, google, Configured);
        await Connect(calendar, organizer.Id);
        google.RevokedByUser = true;

        var created = await new MeetingService(db, calendar).CreateAsync(organizer.Id, true, Request(project.Id), CancellationToken.None);

        Assert.Equal("Failed", created.GoogleSyncStatus);
        Assert.Contains("volvé a conectar", (await calendar.GetStatusAsync(organizer.Id, CancellationToken.None)).LastError);

        await calendar.DisconnectAsync(organizer.Id, CancellationToken.None);
        Assert.Empty(db.GoogleCalendarConnections);
        Assert.Equal(1, google.Revokes);
    }

    private static async Task Connect(GoogleCalendarService calendar, Guid userId)
    {
        var query = HttpUtility.ParseQueryString(new Uri(calendar.BuildAuthorizationUrl(userId)).Query);
        Assert.True((await calendar.HandleCallbackAsync("auth-code", query["state"], null, CancellationToken.None)).Success);
    }

    private static MeetingUpsertRequestDto Request(Guid projectId, params Guid[] participants)
    {
        var start = DateTime.UtcNow.AddDays(2).Date.AddHours(13);
        return new MeetingUpsertRequestDto { ProjectId = projectId, Title = "Revisión", StartsAt = DateTime.SpecifyKind(start, DateTimeKind.Utc), EndsAt = DateTime.SpecifyKind(start.AddHours(1), DateTimeKind.Utc), ParticipantIds = participants };
    }

    private static (User Organizer, Project Project, User Participant) Seed(KodvianDbContext db)
    {
        var organizer = new User { FullName = "Nicolás", Email = "nico@gmail.com" };
        var participant = new User { FullName = "Pedro", Email = "pedro@example.test" };
        var client = new Client { CommercialName = "Acme" };
        var project = new Project { ClienteId = client.Id, Nombre = "Portal", Estado = ProjectStatus.EnCurso };
        db.AddRange(organizer, participant, client, project);
        db.SaveChanges();
        return (organizer, project, participant);
    }

    private static GoogleCalendarService CreateService(KodvianDbContext db, IGoogleCalendarClient google, GoogleCalendarOptions options, IDataProtectionProvider? protection = null) =>
        new(db, google, Options.Create(options), protection ?? new EphemeralDataProtectionProvider(), NullLogger<GoogleCalendarService>.Instance);

    private static KodvianDbContext CreateDatabase(string? name = null)
    {
        var db = new KodvianDbContext(new DbContextOptionsBuilder<KodvianDbContext>().UseInMemoryDatabase(name ?? Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private sealed class FakeGoogleClient : IGoogleCalendarClient
    {
        public List<GoogleCalendarEvent> Created { get; } = [];
        public List<(string EventId, GoogleCalendarEvent Event)> Updated { get; } = [];
        public List<string> Deleted { get; } = [];
        public string? LastVerifier { get; private set; }
        public bool FailCreate { get; set; }
        public bool RevokedByUser { get; set; }
        public int Revokes { get; private set; }

        public Task<GoogleTokenResult> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
        {
            LastVerifier = codeVerifier;
            return Task.FromResult(new GoogleTokenResult("access", "refresh-token", "nico@gmail.com"));
        }

        public Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct) =>
            RevokedByUser ? throw new GoogleCalendarException("Google rechazó el acceso: volvé a conectar Google Calendar.", authorizationRevoked: true)
                : Task.FromResult(refreshToken == "refresh-token" ? "access" : throw new InvalidOperationException("token inesperado"));

        public Task<string> CreateEventAsync(string accessToken, GoogleCalendarEvent calendarEvent, CancellationToken ct)
        {
            if (FailCreate) throw new GoogleCalendarException("No se pudo conectar con Google Calendar.");
            Created.Add(calendarEvent);
            return Task.FromResult($"evt-{Created.Count}");
        }

        public Task UpdateEventAsync(string accessToken, string eventId, GoogleCalendarEvent calendarEvent, CancellationToken ct)
        {
            Updated.Add((eventId, calendarEvent));
            return Task.CompletedTask;
        }

        public Task DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
        {
            Deleted.Add(eventId);
            return Task.CompletedTask;
        }

        public Task RevokeAsync(string refreshToken, CancellationToken ct)
        {
            Revokes++;
            return Task.CompletedTask;
        }
    }
}
