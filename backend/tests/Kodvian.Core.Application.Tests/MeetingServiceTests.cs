using Kodvian.Core.Application.Meetings.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Application.Tests;

public class MeetingServiceTests : IDisposable
{
    private readonly KodvianDbContext db = new(new DbContextOptionsBuilder<KodvianDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task CancelKeepsMeetingAndMarksItCancelled()
    {
        var client = new Client { CommercialName = "Cliente" };
        var creator = new User { FullName = "Coordinador", Email = "coordinador@kodvian.test", PasswordHash = "hash" };
        var project = new Project { Cliente = client, Nombre = "Proyecto", Responsable = creator };
        db.AddRange(client, creator, project);
        await db.SaveChangesAsync();
        var service = new MeetingService(db);
        var meeting = await service.CreateAsync(creator.Id, true, new MeetingUpsertRequestDto
        {
            ProjectId = project.Id,
            Title = "Seguimiento", StartsAt = DateTime.UtcNow.AddHours(1), EndsAt = DateTime.UtcNow.AddHours(2)
        });

        var cancelled = await service.CancelAsync(creator.Id, true, meeting.Id);

        Assert.NotNull(cancelled);
        Assert.Equal("Cancelada", cancelled!.Status);
        Assert.Single(await db.Meetings.ToListAsync());
    }

    [Fact]
    public async Task ParticipantCannotCancelMeetingItDoesNotManage()
    {
        var client = new Client { CommercialName = "Cliente" };
        var creator = new User { FullName = "Coordinador", Email = "coordinador@kodvian.test", PasswordHash = "hash" };
        var participant = new User { FullName = "Participante", Email = "participante@kodvian.test", PasswordHash = "hash" };
        var project = new Project { Cliente = client, Nombre = "Proyecto", Responsable = creator };
        db.AddRange(client, creator, participant, project);
        await db.SaveChangesAsync();
        var service = new MeetingService(db);
        var meeting = await service.CreateAsync(creator.Id, true, new MeetingUpsertRequestDto
        {
            ProjectId = project.Id,
            Title = "Seguimiento",
            StartsAt = DateTime.UtcNow.AddHours(1),
            EndsAt = DateTime.UtcNow.AddHours(2),
            ParticipantIds = [participant.Id]
        });

        var cancelled = await service.CancelAsync(participant.Id, false, meeting.Id);

        Assert.Null(cancelled);
        Assert.Equal("Programada", (await service.GetByIdAsync(creator.Id, true, meeting.Id))!.Status);
    }

    public void Dispose() => db.Dispose();
}
