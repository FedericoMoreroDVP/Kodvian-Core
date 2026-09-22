using System.Security.Cryptography;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Developers.Abstractions;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Infrastructure.Services;

public class DeveloperPaymentService : IDeveloperPaymentService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly StorageOptions _storageOptions;
    private readonly DeveloperPaymentAccountingService _accounting;

    public DeveloperPaymentService(
        KodvianDbContext dbContext,
        IFileStorageService fileStorageService,
        IOptions<StorageOptions> storageOptions, DeveloperPaymentAccountingService accounting)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _storageOptions = storageOptions.Value;
        _accounting = accounting;
    }

    public async Task<IReadOnlyCollection<DeveloperPaymentDto>> GetByContractAsync(Guid contractId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DeveloperPayments
            .AsNoTracking()
            .Where(x => x.ContractId == contractId)
            .OrderByDescending(x => x.PaymentDate)
            .ThenByDescending(x => x.FechaCreacion)
            .Select(x => new DeveloperPaymentDto
            {
                Id = x.Id,
                ContractId = x.ContractId,
                PaymentDate = x.PaymentDate,
                Amount = x.Amount,
                Currency = x.Currency, AppliedCurrency = x.AppliedCurrency, AppliedAmount = x.AppliedAmount,
                FinancialMovementId = x.FinancialMovementId, Version = x.Version, IsActive = x.Activo,
                PeriodYear = x.PeriodYear,
                PeriodMonth = x.PeriodMonth,
                Reference = x.Reference,
                Notes = x.Notes,
                Receipts = x.Documents
                    .OrderByDescending(d => d.FechaCreacion)
                    .Select(d => new FileMetadataDto
                    {
                        Id = d.Id,
                        FileName = d.OriginalFileName,
                        ContentType = d.ContentType,
                        SizeBytes = d.SizeBytes,
                        UploadedAt = d.FechaCreacion,
                        UploadedByName = d.UploadedBy != null ? d.UploadedBy.FullName : string.Empty
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);
    }

    public Task<DeveloperPaymentDto> CreateAsync(Guid contractId, DeveloperPaymentCreateRequestDto request, CancellationToken cancellationToken = default) => _accounting.SaveAsync(null, contractId, request, cancellationToken);
    public Task<DeveloperPaymentDto> UpdateAsync(Guid id, DeveloperPaymentCreateRequestDto request, CancellationToken cancellationToken = default) => _accounting.SaveAsync(id, null, request, cancellationToken);
    public Task CancelAsync(Guid id, Guid expectedVersion, CancellationToken cancellationToken = default) => _accounting.CancelAsync(id, expectedVersion, cancellationToken);

    public async Task<IReadOnlyCollection<FileMetadataDto>> GetReceiptsAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DocumentFiles
            .AsNoTracking()
            .Where(x => x.DeveloperPaymentId == paymentId)
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

    public async Task<FileMetadataDto> AddReceiptAsync(Guid paymentId, Guid uploadedById, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        await ValidateUploadAsync(paymentId, uploadedById, fileName, contentType, content, cancellationToken);

        var storagePath = await _fileStorageService.SaveAsync(content, ".pdf", cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var file = new DocumentFile
        {
            DeveloperPaymentId = paymentId,
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

    public async Task<FileDownloadDto?> GetReceiptContentAsync(Guid paymentId, Guid receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _dbContext.DocumentFiles
            .AsNoTracking()
            .Where(x => x.Id == receiptId && x.DeveloperPaymentId == paymentId)
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

    public async Task<bool> DeleteReceiptAsync(Guid paymentId, Guid receiptId, CancellationToken cancellationToken = default)
    {
        var receipt = await _dbContext.DocumentFiles
            .FirstOrDefaultAsync(x => x.Id == receiptId && x.DeveloperPaymentId == paymentId, cancellationToken);

        if (receipt is null)
        {
            return false;
        }

        _dbContext.DocumentFiles.Remove(receipt);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _fileStorageService.DeleteAsync(receipt.StoragePath, cancellationToken);
        return true;
    }

    private async Task ValidateUploadAsync(Guid paymentId, Guid uploadedById, string fileName, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("El comprobante debe tener nombre de archivo");
        }

        var paymentExists = await _dbContext.DeveloperPayments.AnyAsync(x => x.Id == paymentId, cancellationToken);
        if (!paymentExists)
        {
            throw new ArgumentException("El pago indicado no existe");
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

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
