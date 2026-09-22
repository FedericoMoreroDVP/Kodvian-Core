using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Developers.Dtos;
using Kodvian.Core.Application.Developers.Requests;

namespace Kodvian.Core.Application.Developers.Abstractions;

public interface IDeveloperPaymentService
{
    Task<IReadOnlyCollection<DeveloperPaymentDto>> GetByContractAsync(Guid contractId, CancellationToken cancellationToken = default);
    Task<DeveloperPaymentDto> CreateAsync(Guid contractId, DeveloperPaymentCreateRequestDto request, CancellationToken cancellationToken = default);
    Task<DeveloperPaymentDto> UpdateAsync(Guid id, DeveloperPaymentCreateRequestDto request, CancellationToken cancellationToken = default);
    Task CancelAsync(Guid id, Guid expectedVersion, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<FileMetadataDto>> GetReceiptsAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<FileMetadataDto> AddReceiptAsync(Guid paymentId, Guid uploadedById, string fileName, string contentType, byte[] content, CancellationToken cancellationToken = default);
    Task<FileDownloadDto?> GetReceiptContentAsync(Guid paymentId, Guid receiptId, CancellationToken cancellationToken = default);
    Task<bool> DeleteReceiptAsync(Guid paymentId, Guid receiptId, CancellationToken cancellationToken = default);
}
