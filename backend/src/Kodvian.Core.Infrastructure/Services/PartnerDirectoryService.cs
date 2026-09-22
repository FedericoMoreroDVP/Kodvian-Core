using System.Linq.Expressions;
using System.Net.Mail;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public sealed class PartnerDirectoryService(KodvianDbContext db)
{
    private static Expression<Func<Partner, PartnerDto>> Projection() => p => new PartnerDto(p.Id,
        p.Developer != null ? p.Developer.FullName : p.User != null ? p.User.FullName : p.FullName, p.Activo)
    {
        Email = p.Developer != null ? p.Developer.Email : p.User != null ? p.User.Email : p.Email,
        Source = p.DeveloperId != null ? "Developer" : p.UserId != null ? "User" : "Manual",
        PersonId = p.DeveloperId ?? p.UserId
    };

    public async Task<IReadOnlyCollection<PartnerDto>> ListAsync(CancellationToken ct) => await db.Partners.AsNoTracking()
        .OrderBy(p => p.Developer != null ? p.Developer.FullName : p.User != null ? p.User.FullName : p.FullName)
        .ThenBy(p => p.Id).Select(Projection()).ToListAsync(ct);

    public async Task<PagedResultDto<PartnerPersonDto>> PeopleAsync(PartnerPeopleRequest request, CancellationToken ct)
    {
        var developers = db.Developers.AsNoTracking().Where(x => x.Activo);
        // Profiles cover both developers and analysts. Accounts without a profile
        // (for example a standalone administrator) appear once in the second group.
        var users = db.Users.AsNoTracking().Where(x => x.Activo && x.DeveloperId == null);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLowerInvariant();
            developers = developers.Where(x => x.FullName.ToLower().Contains(term) || x.Email != null && x.Email.ToLower().Contains(term));
            users = users.Where(x => x.FullName.ToLower().Contains(term) || x.Email.ToLower().Contains(term));
        }
        var developerCount = await developers.CountAsync(ct);
        var userCount = await users.CountAsync(ct);
        var skip = (request.PageNumber - 1) * request.PageSize;
        var items = await developers.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip(skip).Take(request.PageSize)
            .Select(x => new PartnerPersonDto("Developer", x.Id, x.FullName, x.Email,
                db.Partners.Where(p => p.DeveloperId == x.Id || p.UserId != null && p.User!.DeveloperId == x.Id)
                    .OrderBy(p => p.Id).Select(p => (Guid?)p.Id).FirstOrDefault())).ToListAsync(ct);
        if (items.Count < request.PageSize)
            items.AddRange(await users.OrderBy(x => x.FullName).ThenBy(x => x.Id)
                .Skip(Math.Max(0, skip - developerCount)).Take(request.PageSize - items.Count)
                .Select(x => new PartnerPersonDto("User", x.Id, x.FullName, x.Email,
                    db.Partners.Where(p => p.UserId == x.Id).Select(p => (Guid?)p.Id).FirstOrDefault())).ToListAsync(ct));
        return new() { Items = items, TotalCount = developerCount + userCount, PageNumber = request.PageNumber, PageSize = request.PageSize };
    }

    public async Task<PartnerDto> SaveAsync(Guid? id, PartnerRequest request, CancellationToken ct)
    {
        await using var transaction = await FinanceWriteScope.BeginAsync(db, ct);
        var entity = id.HasValue ? await db.Partners.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException("Socio no encontrado") : new Partner();
        var currentSource = entity.DeveloperId.HasValue ? "Developer" : entity.UserId.HasValue ? "User" : "Manual";
        var currentPersonId = entity.DeveloperId ?? entity.UserId;
        var source = request.Source ?? currentSource;
        var personId = request.PersonId ?? (request.Source == null ? currentPersonId : null);
        if (source is not ("Manual" or "Developer" or "User")) throw new ArgumentException("Origen de la persona inválido");
        if (currentPersonId.HasValue && (source != currentSource || personId != currentPersonId))
            throw new ArgumentException("El socio ya está vinculado. Modifica sus datos desde Equipo; no se puede transferir su historial a otra persona.");

        Guid? developerId = null, userId = null, relatedDeveloperId = null;
        string name; string? email;
        if (source == "Manual")
        {
            if (personId.HasValue) throw new ArgumentException("Una persona nueva no debe incluir un identificador existente");
            name = request.FullName?.Trim() ?? "";
            email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
            if (name.Length is 0 or > 160) throw new ArgumentException("Indica el nombre de la persona (hasta 160 caracteres)");
            if (email != null && (email.Length > 120 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
                throw new ArgumentException("Indica un correo electrónico válido");
        }
        else
        {
            if (!personId.HasValue || personId == Guid.Empty) throw new ArgumentException("Selecciona una persona existente");
            if (source == "User")
            {
                var person = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == personId, ct)
                    ?? throw new ArgumentException("La persona seleccionada no existe");
                if (!person.Activo && !currentPersonId.HasValue) throw new ArgumentException("La cuenta seleccionada está inactiva");
                relatedDeveloperId = person.DeveloperId;
                if (!currentPersonId.HasValue && person.DeveloperId.HasValue)
                    developerId = person.DeveloperId; // Canonical profile when an account already belongs to Equipo.
                else userId = person.Id;
                name = person.FullName; email = person.Email;
            }
            else { developerId = personId; name = ""; email = null; }

            if (developerId.HasValue)
            {
                var profile = await db.Developers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == developerId, ct)
                    ?? throw new ArgumentException("El perfil de Equipo no existe");
                if (!profile.Activo && !currentPersonId.HasValue) throw new ArgumentException("La persona seleccionada está inactiva en Equipo");
                name = profile.FullName; email = profile.Email; relatedDeveloperId = profile.Id;
            }
            var duplicate = await db.Partners.AnyAsync(p => p.Id != entity.Id && (
                userId.HasValue && p.UserId == userId ||
                relatedDeveloperId.HasValue && (p.DeveloperId == relatedDeveloperId || p.UserId != null && p.User!.DeveloperId == relatedDeveloperId)), ct);
            if (duplicate) throw new ArgumentException("Esta persona ya está registrada como socio. Edita o reactiva el socio existente.");
        }
        if (!id.HasValue) db.Partners.Add(entity);
        entity.DeveloperId = developerId; entity.UserId = userId; entity.FullName = name; entity.Email = email;
        entity.Activo = request.IsActive; entity.FechaActualizacion = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
        return await db.Partners.AsNoTracking().Where(x => x.Id == entity.Id).Select(Projection()).SingleAsync(ct);
    }
}
