using System.Linq.Expressions;
using System.Security.Cryptography;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Finances.Abstractions;
using Kodvian.Core.Application.Finances;
using Kodvian.Core.Application.Finances.Dtos;
using Kodvian.Core.Application.Finances.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Infrastructure.Services;

public class FinancialMovementService : IFinancialMovementService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly StorageOptions _storageOptions;
    private readonly IFinanceOverviewService _overview;

    public FinancialMovementService(
        KodvianDbContext dbContext,
        IFileStorageService fileStorageService,
        IOptions<StorageOptions> storageOptions, IFinanceOverviewService overview)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _storageOptions = storageOptions.Value;
        _overview = overview;
    }

    public async Task<PagedResultDto<FinancialMovementListItemDto>> GetPagedAsync(FinancialMovementListRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(request);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.MovementDate)
            .ThenByDescending(x => x.FechaCreacion)
            .ThenBy(x => x.Id)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new FinancialMovementListItemDto
            {
                Id = x.Id,
                MovementType = x.MovementType.ToString(),
                CategoryName = x.Category != null ? x.Category.Name : string.Empty,
                Description = x.Description,
                Amount = x.Amount,
                IndicatorAmount = request.View == null ? (decimal?)null : request.View == "OperationalResult" || request.View == "RecordedCash"
                    ? (x.MovementType == FinancialMovementType.Ingreso ? x.Amount : -x.Amount)
                    : request.View == "PartnerOutstanding" && x.Nature == "ReintegroSocio" ? -x.Amount : x.Amount,
                Currency = x.Currency, Nature = x.Nature, SettlementDate = x.SettlementDate, ExchangeId = x.ExchangeId,
                DeveloperPaymentId = x.DeveloperPayment != null ? x.DeveloperPayment.Id : null,
                MovementDate = x.MovementDate,
                DueDate = x.DueDate,
                Status = x.Status.ToString(),
                PaymentMethod = x.PaymentMethod,
                ClientName = x.Client != null ? x.Client.CommercialName : null,
                PartnerName = x.Partner == null ? null : x.Partner.Developer != null ? x.Partner.Developer.FullName
                    : x.Partner.User != null ? x.Partner.User.FullName : x.Partner.FullName,
                ProviderName = x.Provider != null ? x.Provider.Name : null
            })
            .ToListAsync(cancellationToken);

        return new PagedResultDto<FinancialMovementListItemDto>
        {
            Items = items,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<FinancialMovementDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FinancialMovements
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FinancialMovementDetailDto> CreateAsync(Guid createdById, FinancialMovementUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(_dbContext, cancellationToken);
        if (request.RequestId == Guid.Empty) throw new ArgumentException("Identificador de operación requerido");
        var previous = await _dbContext.FinancialMovements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId, cancellationToken);
        if (previous != null)
        {
            if (previous.CreatedById != createdById || previous.Amount != request.Amount || previous.Currency != request.Currency
                || previous.Nature != request.Nature || previous.Funding != request.Funding || previous.PartnerId != request.PartnerId
                || previous.CategoryId != request.CategoryId || previous.ProjectId != request.ProjectId || previous.ClientId != request.ClientId || previous.ProviderId != request.ProviderId
                || previous.MovementDate != request.MovementDate || previous.SettlementDate != request.SettlementDate || previous.Status.ToString() != request.Status
                || previous.MovementType.ToString() != request.MovementType || previous.Description != request.Description.Trim()
                || previous.Notes != Normalize(request.Notes) || previous.ReceiptNumber != Normalize(request.ReceiptNumber) || previous.PaymentMethod != Normalize(request.PaymentMethod))
                throw new ArgumentException("La operación ya se utilizó. Consulta el movimiento existente antes de volver a guardar.");
            return (await GetByIdAsync(previous.Id, cancellationToken))!;
        }
        await ValidateReferencesAsync(createdById, request, cancellationToken);
        await ValidateFinanceAsync(request, null, cancellationToken);

        var movement = new FinancialMovement
        {
            Id = request.RequestId,
            CreatedById = createdById
        };

        ApplyRequest(movement, request);

        _dbContext.FinancialMovements.Add(movement);
        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return await _dbContext.FinancialMovements
            .AsNoTracking()
            .Where(x => x.Id == movement.Id)
            .Select(ToDetailDto())
            .FirstAsync(cancellationToken);
    }

    public async Task<FinancialMovementDetailDto?> UpdateAsync(Guid id, FinancialMovementUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(_dbContext, cancellationToken);
        await ValidateReferencesAsync(null, request, cancellationToken);

        var movement = await _dbContext.FinancialMovements.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (movement is null)
        {
            return null;
        }

        if (movement.Version != request.ExpectedVersion) throw new ArgumentException("El movimiento cambió. Actualiza y vuelve a intentar.");
        if (movement.ExchangeId != null) throw new ArgumentException("Anula el cambio de moneda completo y registra la corrección");
        if (await _dbContext.DeveloperPayments.AnyAsync(x => x.FinancialMovementId == id, cancellationToken))
            throw new ArgumentException("Este egreso está vinculado a un pago. Edítalo o anúlalo desde el proyecto.");
        await ValidateFinanceAsync(request, movement, cancellationToken);
        ApplyRequest(movement, request);
        movement.Version = Guid.NewGuid();
        movement.FechaActualizacion = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return await _dbContext.FinancialMovements
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<FinanceMonthlySummaryDto> GetMonthlySummaryAsync(int? year, int? month, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var y = year ?? now.Year;
        var m = month ?? now.Month;
        var monthStart = new DateOnly(y, m, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var summary = await _overview.GetPeriodSummaryAsync(monthStart, monthEnd, cancellationToken);
        var ars = summary.Currencies.Single(x => x.Currency == "ARS");

        return new FinanceMonthlySummaryDto
        {
            Currencies = summary.Currencies,
            MonthlyIncome = ars.Income, MonthlyExpense = ars.Expense, MonthlyResult = ars.Result,
            PendingIncome = ars.PendingIncome, PendingExpense = ars.PendingExpense
        };
    }

    public async Task<FinanceLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _dbContext.FinancialCategories
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Take(300)
            .Select(x => new FinancialCategoryDto
            {
                Id = x.Id,
                Name = x.Name,
                MovementType = x.MovementType.ToString(),
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        var clients = await _dbContext.Clients
            .AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.CommercialName)
            .Take(300)
            .Select(x => new FinanceLookupItemDto { Id = x.Id, Name = x.CommercialName })
            .ToListAsync(cancellationToken);

        var projects = await _dbContext.Projects
            .AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.Nombre)
            .Take(300)
            .Select(x => new FinanceLookupItemDto { Id = x.Id, Name = x.Nombre })
            .ToListAsync(cancellationToken);

        var providers = await _dbContext.Providers
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Take(300)
            .Select(x => new FinanceLookupItemDto { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);

        return new FinanceLookupsDto
        {
            Categories = categories,
            Clients = clients,
            Projects = projects,
            Providers = providers
        };
    }

    public async Task<IReadOnlyCollection<FileMetadataDto>> GetReceiptsAsync(Guid movementId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentFiles
            .AsNoTracking()
            .Where(x => x.FinancialMovementId == movementId)
            .OrderByDescending(x => x.FechaCreacion)
            .Select(x => new FileMetadataDto
            {
                Id = x.Id,
                FileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeBytes = x.SizeBytes,
                UploadedAt = x.FechaCreacion,
                UploadedByName = x.UploadedBy != null ? x.UploadedBy.FullName : string.Empty
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<FileMetadataDto> AddReceiptAsync(Guid movementId, Guid uploadedById, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        await ValidateUploadAsync(movementId, uploadedById, fileName, contentType, content, cancellationToken);

        var storagePath = await _fileStorageService.SaveAsync(content, ".pdf", cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var file = new DocumentFile
        {
            FinancialMovementId = movementId,
            UploadedById = uploadedById,
            OriginalFileName = fileName.Trim(),
            StoredFileName = Path.GetFileName(storagePath),
            ContentType = "application/pdf",
            SizeBytes = content.LongLength,
            StoragePath = storagePath,
            Sha256 = hash
        };

        _dbContext.DocumentFiles.Add(file);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var uploader = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Id == uploadedById)
            .Select(x => x.FullName)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

        return new FileMetadataDto
        {
            Id = file.Id,
            FileName = file.OriginalFileName,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            UploadedAt = file.FechaCreacion,
            UploadedByName = uploader
        };
    }

    public async Task<FileDownloadDto?> GetReceiptContentAsync(Guid movementId, Guid receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _dbContext.DocumentFiles
            .AsNoTracking()
            .Where(x => x.Id == receiptId && x.FinancialMovementId == movementId)
            .Select(x => new { x.OriginalFileName, x.ContentType, x.StoragePath })
            .FirstOrDefaultAsync(cancellationToken);

        if (receipt is null)
        {
            return null;
        }

        var content = await _fileStorageService.ReadAsync(receipt.StoragePath, cancellationToken);
        return new FileDownloadDto
        {
            FileName = receipt.OriginalFileName,
            ContentType = receipt.ContentType,
            Content = content
        };
    }

    public async Task<bool> DeleteReceiptAsync(Guid movementId, Guid receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _dbContext.DocumentFiles
            .FirstOrDefaultAsync(x => x.Id == receiptId && x.FinancialMovementId == movementId, cancellationToken);

        if (receipt is null)
        {
            return false;
        }

        _dbContext.DocumentFiles.Remove(receipt);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _fileStorageService.DeleteAsync(receipt.StoragePath, cancellationToken);
        return true;
    }

    private IQueryable<FinancialMovement> BuildFilteredQuery(FinancialMovementListRequestDto request)
    {
        var query = _dbContext.FinancialMovements
            .AsNoTracking()
            .Where(x => x.Activo)
            .AsQueryable();
        if (request.PartnerId.HasValue) query = query.Where(x => x.PartnerId == request.PartnerId);
        var useSettlementDate = request.UseSettlementDate;
        if (!string.IsNullOrEmpty(request.View))
        {
            if (!FinanceRules.DetailViews.Contains(request.View)) throw new ArgumentException("Vista financiera inválida");
            FinanceRules.Currency(request.Currency);
            var pendingView = request.View is "PendingIncome" or "PendingExpense";
            useSettlementDate = !pendingView;
            if (pendingView)
                query = query.Where(x => x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido);
            else
            {
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                query = query.Where(x => x.SettlementDate.HasValue && x.SettlementDate <= today
                    && (x.MovementType == FinancialMovementType.Ingreso && x.Status == FinancialMovementStatus.Cobrado
                        || x.MovementType == FinancialMovementType.Egreso && x.Status == FinancialMovementStatus.Pagado));
            }
            query = request.View switch
            {
                "OperationalIncome" => query.Where(x => x.Nature == "Operacion" && x.MovementType == FinancialMovementType.Ingreso),
                "OperationalExpense" => query.Where(x => x.Nature == "Operacion" && x.MovementType == FinancialMovementType.Egreso),
                "OperationalResult" => query.Where(x => x.Nature == "Operacion"),
                "RecordedCash" => query.Where(x => x.Funding == "Empresa"),
                "PendingIncome" => query.Where(x => x.MovementType == FinancialMovementType.Ingreso),
                "PendingExpense" => query.Where(x => x.MovementType == FinancialMovementType.Egreso),
                "PartnerContributions" => query.Where(x => x.Nature == "AporteSocio" || x.Funding == "SocioAporte"),
                "PartnerWithdrawals" => query.Where(x => x.Nature == "RetiroSocio"),
                "PartnerReimbursableExpenses" => query.Where(x => x.Funding == "SocioReintegrable"),
                "PartnerReimbursements" => query.Where(x => x.Nature == "ReintegroSocio"),
                "PartnerOutstanding" => query.Where(x => x.Funding == "SocioReintegrable" || x.Nature == "ReintegroSocio"),
                _ => query
            };
        }
        if (!string.IsNullOrEmpty(request.Currency)) query = query.Where(x => x.Currency == request.Currency);
        if (!string.IsNullOrEmpty(request.Nature)) query = query.Where(x => x.Nature == request.Nature);
        if (request.ProjectId.HasValue) query = query.Where(x => x.ProjectId == request.ProjectId);
        if (request.ExactAmount.HasValue) query = query.Where(x => x.Amount == request.ExactAmount);
        if (request.UnlinkedOnly) query = query.Where(x => x.DeveloperPayment == null && x.ExchangeId == null && x.Activo && x.Nature == "Operacion" && x.Funding == "Empresa"
            && (x.Status == FinancialMovementStatus.Pendiente || x.Status == FinancialMovementStatus.Vencido || x.Status == FinancialMovementStatus.Pagado));

        if (request.DateFrom.HasValue)
        {
            query = useSettlementDate ? query.Where(x => x.SettlementDate >= request.DateFrom.Value) : query.Where(x => x.MovementDate >= request.DateFrom.Value);
        }

        if (request.DateTo.HasValue)
        {
            query = useSettlementDate ? query.Where(x => x.SettlementDate <= request.DateTo.Value) : query.Where(x => x.MovementDate <= request.DateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.MovementType) && Enum.TryParse<FinancialMovementType>(request.MovementType, true, out var type))
        {
            query = query.Where(x => x.MovementType == type);
        }

        if (request.CategoryId.HasValue)
        {
            query = query.Where(x => x.CategoryId == request.CategoryId);
        }

        if (request.ClientId.HasValue)
        {
            query = query.Where(x => x.ClientId == request.ClientId);
        }

        if (request.ProviderId.HasValue)
        {
            query = query.Where(x => x.ProviderId == request.ProviderId);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<FinancialMovementStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Status == status);
        }

        return query;
    }

    private static void ApplyRequest(FinancialMovement movement, FinancialMovementUpsertRequestDto request)
    {
        movement.MovementType = ParseMovementType(request.MovementType);
        movement.CategoryId = request.CategoryId;
        movement.ClientId = request.ClientId;
        movement.ProviderId = request.ProviderId;
        movement.ProjectId = request.ProjectId;
        movement.Description = request.Description.Trim();
        movement.Amount = request.Amount;
        movement.Currency = request.Currency; movement.Nature = request.Nature; movement.Funding = request.Funding;
        movement.PartnerId = request.PartnerId; movement.SettlementDate = request.SettlementDate; movement.SettlementDateEstimated = request.SettlementDateEstimated;
        movement.MovementDate = request.MovementDate;
        movement.DueDate = request.DueDate;
        movement.Status = ParseStatus(request.Status);
        movement.PaymentMethod = Normalize(request.PaymentMethod);
        movement.ReceiptNumber = Normalize(request.ReceiptNumber);
        movement.Notes = Normalize(request.Notes);
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static FinancialMovementType ParseMovementType(string type)
    {
        return Enum.TryParse<FinancialMovementType>(type, true, out var parsed)
            ? parsed
            : FinancialMovementType.Ingreso;
    }

    private static FinancialMovementStatus ParseStatus(string status)
    {
        return Enum.TryParse<FinancialMovementStatus>(status, true, out var parsed)
            ? parsed
            : FinancialMovementStatus.Pendiente;
    }

    private static Expression<Func<FinancialMovement, FinancialMovementDetailDto>> ToDetailDto()
    {
        return x => new FinancialMovementDetailDto
        {
            Id = x.Id,
            MovementType = x.MovementType.ToString(),
            CategoryId = x.CategoryId,
            CategoryName = x.Category != null ? x.Category.Name : string.Empty,
            ClientId = x.ClientId,
            ClientName = x.Client != null ? x.Client.CommercialName : null,
            ProviderId = x.ProviderId,
            ProviderName = x.Provider != null ? x.Provider.Name : null,
            ProjectId = x.ProjectId,
            ProjectName = x.Project != null ? x.Project.Nombre : null,
            Description = x.Description,
            Amount = x.Amount,
            Currency = x.Currency, Nature = x.Nature, Funding = x.Funding, PartnerId = x.PartnerId,
            SettlementDate = x.SettlementDate, SettlementDateEstimated = x.SettlementDateEstimated, Version = x.Version, ExchangeId = x.ExchangeId,
            DeveloperPaymentId = x.DeveloperPayment != null ? x.DeveloperPayment.Id : null,
            MovementDate = x.MovementDate,
            DueDate = x.DueDate,
            Status = x.Status.ToString(),
            PaymentMethod = x.PaymentMethod,
            ReceiptNumber = x.ReceiptNumber,
            Notes = x.Notes,
            CreatedById = x.CreatedById,
            CreatedByName = x.CreatedBy != null ? x.CreatedBy.FullName : string.Empty,
            CreatedAt = x.FechaCreacion,
            UpdatedAt = x.FechaActualizacion
        };
    }

    private async Task ValidateUploadAsync(Guid movementId, Guid uploadedById, string fileName, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("El comprobante debe tener nombre de archivo");
        }

        var movementExists = await _dbContext.FinancialMovements.AnyAsync(x => x.Id == movementId, cancellationToken);
        if (!movementExists)
        {
            throw new ArgumentException("El movimiento indicado no existe");
        }

        var userExists = await _dbContext.Users.AnyAsync(x => x.Id == uploadedById, cancellationToken);
        if (!userExists)
        {
            throw new ArgumentException("El usuario cargador no existe");
        }

        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Solo se admiten comprobantes PDF");
        }

        var maxBytes = Math.Max(_storageOptions.MaxPdfSizeMb, 1) * 1024 * 1024;
        if (content.Length == 0 || content.Length > maxBytes)
        {
            throw new ArgumentException($"El archivo debe tener entre 1 byte y {maxBytes / (1024 * 1024)} MB");
        }

        if (content.Length < 4 || content[0] != 0x25 || content[1] != 0x50 || content[2] != 0x44 || content[3] != 0x46)
        {
            throw new ArgumentException("El archivo no es un PDF válido");
        }
    }

    private async Task ValidateFinanceAsync(FinancialMovementUpsertRequestDto request, FinancialMovement? original, CancellationToken ct)
    {
        FinanceRules.Currency(request.Currency); FinanceRules.Money(request.Amount); FinanceRules.Date(request.MovementDate);
        if (!FinanceRules.Natures.Contains(request.Nature) || request.Nature == "CambioMoneda" || !FinanceRules.FundingSources.Contains(request.Funding))
            throw new ArgumentException("Clasificación inválida; usa la operación específica para cambios de moneda");
        var income = request.MovementType == "Ingreso";
        if (request.MovementType is not ("Ingreso" or "Egreso") || request.Status is not ("Pendiente" or "Cobrado" or "Pagado" or "Vencido" or "Anulado")
            || (income && request.Status == "Pagado") || (!income && request.Status == "Cobrado")) throw new ArgumentException("Estado incompatible con el tipo de movimiento");
        var settled = request.Status is "Cobrado" or "Pagado";
        if (settled && !request.SettlementDate.HasValue) throw new ArgumentException("Indica la fecha efectiva del cobro o pago");
        if (request.SettlementDate.HasValue)
        {
            FinanceRules.Date(request.SettlementDate.Value);
            if (request.SettlementDate > DateOnly.FromDateTime(DateTime.UtcNow)) throw new ArgumentException("Un cobro o pago efectivo no puede tener fecha futura");
        }
        if (!settled && request.Status != "Anulado" && request.SettlementDate.HasValue) throw new ArgumentException("Un pendiente no tiene fecha de cobro o pago efectivo");
        if (request.Nature != "Operacion" && request.Funding != "Empresa") throw new ArgumentException("Esta clasificación utiliza fondos de la empresa");
        if (request.Nature == "AporteSocio" && !income || (request.Nature is "RetiroSocio" or "ReintegroSocio") && income)
            throw new ArgumentException("Tipo incompatible con aporte, retiro o reintegro");
        if (request.Nature != "Operacion" && !settled && request.Status != "Anulado") throw new ArgumentException("Registra aportes y retiros cuando se hacen efectivos");
        if (request.Funding != "Empresa" && (income || request.Nature != "Operacion" || !settled && request.Status != "Anulado"))
            throw new ArgumentException("Un gasto afrontado por un socio debe ser un egreso operativo pagado");
        var needsPartner = request.Nature != "Operacion" || request.Funding != "Empresa";
        if (needsPartner != request.PartnerId.HasValue) throw new ArgumentException(needsPartner ? "Selecciona el socio" : "Este movimiento no requiere un socio");
        var previousPartnerId = original?.PartnerId;
        if (request.PartnerId.HasValue && !await _dbContext.Partners.AnyAsync(x => x.Id == request.PartnerId && (x.Activo || x.Id == previousPartnerId), ct)) throw new ArgumentException("Socio no disponible");
        var keys = new[] { (request.PartnerId, request.Currency), (original?.PartnerId, original?.Currency ?? request.Currency) }.Distinct();
        foreach (var (partnerId, currency) in keys.Where(x => x.Item1.HasValue))
        {
            var originalId = original?.Id;
            var debt = await _dbContext.FinancialMovements.Where(x => x.Id != originalId && x.Activo && x.PartnerId == partnerId && x.Currency == currency && x.Status == FinancialMovementStatus.Pagado)
                .SumAsync(x => x.Funding == "SocioReintegrable" ? x.Amount : x.Nature == "ReintegroSocio" ? -x.Amount : 0, ct);
            if (request.PartnerId == partnerId && request.Currency == currency && request.Status == "Pagado") debt += request.Funding == "SocioReintegrable" ? request.Amount : request.Nature == "ReintegroSocio" ? -request.Amount : 0;
            if (debt < 0) throw new ArgumentException("El reintegro supera los gastos registrados a devolver al socio");
        }
    }

    private async Task ValidateReferencesAsync(Guid? createdById, FinancialMovementUpsertRequestDto request, CancellationToken cancellationToken)
    {
        var type = ParseMovementType(request.MovementType);
        var categoryExists = await _dbContext.FinancialCategories.AnyAsync(x => x.Id == request.CategoryId && x.MovementType == type, cancellationToken);
        if (!categoryExists)
        {
            throw new ArgumentException("La categoría no existe o no corresponde al tipo de movimiento");
        }

        if (request.ClientId.HasValue)
        {
            var clientExists = await _dbContext.Clients.AnyAsync(x => x.Id == request.ClientId.Value, cancellationToken);
            if (!clientExists)
            {
                throw new ArgumentException("El cliente seleccionado no existe");
            }
        }

        if (request.ProviderId.HasValue)
        {
            var providerExists = await _dbContext.Providers.AnyAsync(x => x.Id == request.ProviderId.Value, cancellationToken);
            if (!providerExists)
            {
                throw new ArgumentException("El proveedor seleccionado no existe");
            }
        }

        if (request.ProjectId.HasValue)
        {
            var projectExists = await _dbContext.Projects.AnyAsync(x => x.Id == request.ProjectId.Value, cancellationToken);
            if (!projectExists)
            {
                throw new ArgumentException("El proyecto seleccionado no existe");
            }
        }

        if (createdById.HasValue)
        {
            var creatorExists = await _dbContext.Users.AnyAsync(x => x.Id == createdById.Value, cancellationToken);
            if (!creatorExists)
            {
                throw new ArgumentException("El usuario creador no existe");
            }
        }
    }
}
