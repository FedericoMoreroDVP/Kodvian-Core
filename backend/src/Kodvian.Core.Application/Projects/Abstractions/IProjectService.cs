using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Projects.Dtos;
using Kodvian.Core.Application.Projects.Requests;

namespace Kodvian.Core.Application.Projects.Abstractions;

public interface IProjectService
{
    Task<PagedResultDto<ProjectListItemDto>> GetPagedAsync(ProjectListRequestDto request, CancellationToken cancellationToken = default);
    Task<ProjectDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectDetailDto> CreateAsync(ProjectUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<ProjectDetailDto?> UpdateAsync(Guid id, ProjectUpsertRequestDto request, CancellationToken cancellationToken = default);
    Task<ProjectDriveLinkDto?> GetDriveLinkAsync(Guid id, CancellationToken cancellationToken = default);
    Task<ProjectDriveLinkDto?> UpdateDriveLinkAsync(Guid id, ProjectDriveLinkRequestDto request, CancellationToken cancellationToken = default);
    Task<ProjectLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProjectDocumentTypeDto>> GetDocumentTypesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProjectDocumentListItemDto>> GetDocumentsAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task<ProjectDocumentListItemDto> AddDocumentAsync(Guid projectId, Guid uploadedById, string title, string type, string fileName, string contentType, byte[] content, string? notes, CancellationToken cancellationToken = default);
    Task<ProjectDocumentListItemDto> AddDocumentVersionAsync(Guid projectId, Guid documentId, Guid uploadedById, string fileName, string contentType, byte[] content, string? notes, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ProjectDocumentVersionDto>> GetDocumentVersionsAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken = default);
    Task<FileDownloadDto?> GetDocumentContentAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken = default);
    Task<FileDownloadDto?> GetDocumentVersionContentAsync(Guid projectId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<bool> DeleteDocumentAsync(Guid projectId, Guid documentId, Guid deletedById, CancellationToken cancellationToken = default);
}
