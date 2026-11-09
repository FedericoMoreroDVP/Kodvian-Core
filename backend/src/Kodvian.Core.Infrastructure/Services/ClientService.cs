using System.Linq.Expressions;
using Kodvian.Core.Application.Clients.Abstractions;
using Kodvian.Core.Application.Clients.Dtos;
using Kodvian.Core.Application.Clients.Requests;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly KodvianDbContext _dbContext;

    public ClientService(KodvianDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResultDto<ClientListItemDto>> GetPagedAsync(ClientListRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Clients.AsNoTracking().AsQueryable();

        if (request.ActiveOnly) query = query.Where(x => x.Activo);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.CommercialName, search));
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<ClientStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.CommercialName)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ClientListItemDto
            {
                Id = x.Id,
                CommercialName = x.CommercialName,
                ContactName = x.ContactName,
                ContactEmail = x.ContactEmail,
                Status = x.Status.ToString(),
                MonthlyAmount = x.MonthlyAmount,
                IsActive = x.Activo
            })
            .ToListAsync(cancellationToken);

        return new PagedResultDto<ClientListItemDto>
        {
            Items = items,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ClientDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Clients
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClientDetailDto> CreateAsync(ClientUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        var client = new Client();
        ApplyRequest(client, request);

        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Clients
            .AsNoTracking()
            .Where(x => x.Id == client.Id)
            .Select(ToDetailDto())
            .FirstAsync(cancellationToken);
    }

    public async Task<ClientDetailDto?> UpdateAsync(Guid id, ClientUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        var client = await _dbContext.Clients.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (client is null)
        {
            return null;
        }

        ApplyRequest(client, request);
        client.FechaActualizacion = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Clients
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ClientDetailDto?> ChangeStatusAsync(Guid id, ChangeClientStatusRequestDto request, CancellationToken cancellationToken = default)
    {
        var client = await _dbContext.Clients.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (client is null)
        {
            return null;
        }

        client.Status = ParseStatus(request.Status);
        client.FechaActualizacion = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Clients
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static void ApplyRequest(Client client, ClientUpsertRequestDto request)
    {
        client.CommercialName = request.CommercialName.Trim();
        client.LegalName = Normalize(request.LegalName);
        client.TaxId = Normalize(request.TaxId);
        client.ContactName = Normalize(request.ContactName);
        client.ContactEmail = Normalize(request.ContactEmail)?.ToLower();
        client.ContactPhone = Normalize(request.ContactPhone);
        client.Address = Normalize(request.Address);
        client.City = Normalize(request.City);
        client.Province = Normalize(request.Province);
        client.Country = Normalize(request.Country);
        client.Status = ParseStatus(request.Status);
        client.ServiceType = Normalize(request.ServiceType);
        client.MonthlyAmount = request.MonthlyAmount;
        client.BillingDay = request.BillingDay;
        client.Notes = Normalize(request.Notes);
        client.Activo = request.IsActive;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static ClientStatus ParseStatus(string status)
    {
        return Enum.TryParse<ClientStatus>(status, true, out var parsed)
            ? parsed
            : ClientStatus.Prospecto;
    }

    private static Expression<Func<Client, ClientDetailDto>> ToDetailDto()
    {
        return x => new ClientDetailDto
        {
            Id = x.Id,
            CommercialName = x.CommercialName,
            LegalName = x.LegalName,
            TaxId = x.TaxId,
            ContactName = x.ContactName,
            ContactEmail = x.ContactEmail,
            ContactPhone = x.ContactPhone,
            Address = x.Address,
            City = x.City,
            Province = x.Province,
            Country = x.Country,
            Status = x.Status.ToString(),
            ServiceType = x.ServiceType,
            MonthlyAmount = x.MonthlyAmount,
            BillingDay = x.BillingDay,
            Notes = x.Notes,
            IsActive = x.Activo,
            CreatedAt = x.FechaCreacion,
            UpdatedAt = x.FechaActualizacion
        };
    }
}
