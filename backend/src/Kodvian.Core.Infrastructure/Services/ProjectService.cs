using System.Linq.Expressions;
using System.Security.Cryptography;
using Kodvian.Core.Application.Common.Files;
using Kodvian.Core.Application.Common.Models;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Application.Projects.Abstractions;
using Kodvian.Core.Application.Projects;
using Kodvian.Core.Application.Projects.Dtos;
using Kodvian.Core.Application.Projects.Requests;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Kodvian.Core.Infrastructure.Persistence;
using Kodvian.Core.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Kodvian.Core.Infrastructure.Services;

public class ProjectService : IProjectService
{
    private readonly KodvianDbContext _dbContext;
    private readonly IFileStorageService _fileStorageService;
    private readonly StorageOptions _storageOptions;

    public ProjectService(KodvianDbContext dbContext, IFileStorageService fileStorageService, IOptions<StorageOptions> storageOptions)
    {
        _dbContext = dbContext;
        _fileStorageService = fileStorageService;
        _storageOptions = storageOptions.Value;
    }

    public async Task<PagedResultDto<ProjectListItemDto>> GetPagedAsync(ProjectListRequestDto request, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Projects
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = $"%{request.Search.Trim()}%";
            query = query.Where(x => EF.Functions.ILike(x.Nombre, search));
        }

        if (request.ClientId.HasValue)
        {
            query = query.Where(x => x.ClienteId == request.ClientId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status) && Enum.TryParse<ProjectStatus>(request.Status, true, out var status))
        {
            query = query.Where(x => x.Estado == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Priority) && Enum.TryParse<ProjectPriority>(request.Priority, true, out var priority))
        {
            query = query.Where(x => x.Prioridad == priority);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.FechaCreacion)
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new ProjectListItemDto
            {
                Id = x.Id,
                Name = x.Nombre,
                ClientId = x.ClienteId,
                ClientName = x.Cliente != null ? x.Cliente.CommercialName : string.Empty,
                ResponsibleId = x.ResponsableId,
                ResponsibleName = x.Responsable != null ? x.Responsable.FullName : null,
                Status = x.Estado.ToString(),
                Priority = x.Prioridad.ToString(),
                StartDate = x.FechaInicio,
                EstimatedDeliveryDate = x.FechaEntregaEstimada,
                ProgressPercentage = x.PorcentajeAvance,
                IsActive = x.Activo
            })
            .ToListAsync(cancellationToken);

        return new PagedResultDto<ProjectListItemDto>
        {
            Items = items,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }

    public async Task<ProjectDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Projects
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectDetailDto> CreateAsync(ProjectUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await ValidateReferencesAsync(request, cancellationToken);

        var project = new Project();
        ApplyRequest(project, request);

        _dbContext.Projects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await _dbContext.Projects
            .AsNoTracking()
            .Where(x => x.Id == project.Id)
            .Select(ToDetailDto())
            .FirstAsync(cancellationToken);
    }

    public async Task<ProjectDriveLinkDto?> GetDriveLinkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Projects.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new ProjectDriveLinkDto { GoogleDriveFolderUrl = x.GoogleDriveFolderUrl })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectDriveLinkDto?> UpdateDriveLinkAsync(Guid id, ProjectDriveLinkRequestDto request, CancellationToken cancellationToken = default)
    {
        var link = GoogleDriveLinkRules.Normalize(request.GoogleDriveFolderUrl);
        var project = await _dbContext.Projects.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null) return null;

        project.GoogleDriveFolderUrl = link;
        project.FechaActualizacion = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new ProjectDriveLinkDto { GoogleDriveFolderUrl = link };
    }

    public async Task<ProjectDetailDto?> UpdateAsync(Guid id, ProjectUpsertRequestDto request, CancellationToken cancellationToken = default)
    {
        await using var tx = await FinanceWriteScope.BeginAsync(_dbContext, cancellationToken);
        var project = await _dbContext.Projects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null)
        {
            return null;
        }

        await ValidateReferencesAsync(request, cancellationToken, project.ResponsableId);
        var becomingCanceled = project.Estado != ProjectStatus.Cancelado && ParseStatus(request.Status) == ProjectStatus.Cancelado;
        ApplyRequest(project, request);
        project.FechaActualizacion = DateTime.UtcNow;
        if (becomingCanceled)
        {
            var now = DateTime.UtcNow;
            var activeContracts = await _dbContext.ProjectDeveloperContracts.Where(x => x.ProjectId == id && x.Activo).ToListAsync(cancellationToken);
            foreach (var contract in activeContracts)
            {
                contract.Activo = false;
                contract.FechaActualizacion = now;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (tx != null) await tx.CommitAsync(cancellationToken);

        return await _dbContext.Projects
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(ToDetailDto())
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ProjectLookupsDto> GetLookupsAsync(CancellationToken cancellationToken = default)
    {
        var clients = await _dbContext.Clients
            .AsNoTracking()
            .Where(x => x.Activo)
            .OrderBy(x => x.CommercialName)
            .Take(300)
            .Select(x => new ProjectLookupItemDto
            {
                Id = x.Id,
                Name = x.CommercialName
            })
            .ToListAsync(cancellationToken);

        var responsibles = await _dbContext.Users
            .AsNoTracking()
            .Where(x => x.Activo && x.DeveloperId != null && x.UserRoles.Any(r => r.Role.Name == RoleNames.Analyst && r.Role.Activo))
            .OrderBy(x => x.FullName)
            .Take(300)
            .Select(x => new ProjectLookupItemDto
            {
                Id = x.Id,
                Name = x.FullName,
                DeveloperId = x.DeveloperId
            })
            .ToListAsync(cancellationToken);

        return new ProjectLookupsDto
        {
            Clients = clients,
            Responsibles = responsibles
        };
    }

    public Task<IReadOnlyCollection<ProjectDocumentTypeDto>> GetDocumentTypesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ProjectDocumentTypeDto> types = Enum
            .GetValues<ProjectDocumentType>()
            .Select(x => new ProjectDocumentTypeDto
            {
                Value = x.ToString(),
                Label = GetTypeLabel(x)
            })
            .ToArray();

        return Task.FromResult(types);
    }

    public async Task<IReadOnlyCollection<ProjectDocumentListItemDto>> GetDocumentsAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ProjectDocuments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Activo)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Type,
                LatestVersion = x.Versions
                    .Where(v => v.Activo)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new
                    {
                        v.Id,
                        v.VersionNumber,
                        v.FechaCreacion,
                        v.DocumentFile!.OriginalFileName,
                        v.DocumentFile.ContentType,
                        v.DocumentFile.SizeBytes,
                        UploadedByName = v.UploadedBy != null ? v.UploadedBy.FullName : string.Empty
                    })
                    .FirstOrDefault()
            })
            .Where(x => x.LatestVersion != null)
            .OrderByDescending(x => x.LatestVersion!.FechaCreacion)
            .Select(x => new ProjectDocumentListItemDto
            {
                Id = x.Id,
                Title = x.Title,
                Type = x.Type.ToString(),
                TypeLabel = GetTypeLabel(x.Type),
                CurrentVersionId = x.LatestVersion!.Id,
                CurrentVersionNumber = x.LatestVersion.VersionNumber,
                FileName = x.LatestVersion.OriginalFileName,
                ContentType = x.LatestVersion.ContentType,
                SizeBytes = x.LatestVersion.SizeBytes,
                UploadedByName = x.LatestVersion.UploadedByName,
                UploadedAt = x.LatestVersion.FechaCreacion
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<ProjectDocumentListItemDto> AddDocumentAsync(
        Guid projectId,
        Guid uploadedById,
        string title,
        string type,
        string fileName,
        string contentType,
        byte[] content,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await ValidateDocumentUploadAsync(projectId, uploadedById, fileName, contentType, content, cancellationToken);

        var normalizedTitle = NormalizeTitle(title);
        var parsedType = ParseDocumentType(type);

        var storagePath = await _fileStorageService.SaveAsync(content, ".pdf", cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var file = new DocumentFile
        {
            ProjectId = projectId,
            UploadedById = uploadedById,
            OriginalFileName = NormalizeFileName(fileName),
            StoredFileName = Path.GetFileName(storagePath),
            ContentType = "application/pdf",
            SizeBytes = content.LongLength,
            StoragePath = storagePath,
            Sha256 = hash
        };

        var document = new ProjectDocument
        {
            ProjectId = projectId,
            Title = normalizedTitle,
            Type = parsedType,
            CreatedById = uploadedById
        };

        var version = new ProjectDocumentVersion
        {
            ProjectDocument = document,
            DocumentFile = file,
            VersionNumber = 1,
            Notes = NormalizeNotes(notes),
            UploadedById = uploadedById
        };

        _dbContext.DocumentFiles.Add(file);
        _dbContext.ProjectDocuments.Add(document);
        _dbContext.ProjectDocumentVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetDocumentListItemAsync(projectId, document.Id, cancellationToken)
            ?? throw new ArgumentException("No se pudo crear el documento");
    }

    public async Task<ProjectDocumentListItemDto> AddDocumentVersionAsync(
        Guid projectId,
        Guid documentId,
        Guid uploadedById,
        string fileName,
        string contentType,
        byte[] content,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        await ValidateDocumentUploadAsync(projectId, uploadedById, fileName, contentType, content, cancellationToken);

        var document = await _dbContext.ProjectDocuments
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == documentId && x.ProjectId == projectId && x.Activo, cancellationToken);

        if (document is null)
        {
            throw new ArgumentException("Documento no encontrado");
        }

        var storagePath = await _fileStorageService.SaveAsync(content, ".pdf", cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var nextVersion = document.Versions.Count == 0
            ? 1
            : document.Versions.Max(x => x.VersionNumber) + 1;

        var file = new DocumentFile
        {
            ProjectId = projectId,
            UploadedById = uploadedById,
            OriginalFileName = NormalizeFileName(fileName),
            StoredFileName = Path.GetFileName(storagePath),
            ContentType = "application/pdf",
            SizeBytes = content.LongLength,
            StoragePath = storagePath,
            Sha256 = hash
        };

        var version = new ProjectDocumentVersion
        {
            ProjectDocumentId = document.Id,
            DocumentFile = file,
            VersionNumber = nextVersion,
            Notes = NormalizeNotes(notes),
            UploadedById = uploadedById
        };

        document.FechaActualizacion = DateTime.UtcNow;

        _dbContext.DocumentFiles.Add(file);
        _dbContext.ProjectDocumentVersions.Add(version);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetDocumentListItemAsync(projectId, document.Id, cancellationToken)
            ?? throw new ArgumentException("No se pudo crear la versión del documento");
    }

    public async Task<IReadOnlyCollection<ProjectDocumentVersionDto>> GetDocumentVersionsAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var documentExists = await _dbContext.ProjectDocuments
            .AsNoTracking()
            .AnyAsync(x => x.Id == documentId && x.ProjectId == projectId && x.Activo, cancellationToken);

        if (!documentExists)
        {
            throw new ArgumentException("Documento no encontrado");
        }

        return await _dbContext.ProjectDocumentVersions
            .AsNoTracking()
            .Where(x => x.ProjectDocumentId == documentId && x.Activo)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new ProjectDocumentVersionDto
            {
                Id = x.Id,
                VersionNumber = x.VersionNumber,
                FileName = x.DocumentFile != null ? x.DocumentFile.OriginalFileName : string.Empty,
                ContentType = x.DocumentFile != null ? x.DocumentFile.ContentType : "application/pdf",
                SizeBytes = x.DocumentFile != null ? x.DocumentFile.SizeBytes : 0,
                UploadedByName = x.UploadedBy != null ? x.UploadedBy.FullName : string.Empty,
                UploadedAt = x.FechaCreacion,
                Notes = x.Notes
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<FileDownloadDto?> GetDocumentContentAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.ProjectDocumentVersions
            .AsNoTracking()
            .Where(x => x.ProjectDocumentId == documentId && x.ProjectDocument != null && x.ProjectDocument.ProjectId == projectId && x.ProjectDocument.Activo && x.Activo)
            .OrderByDescending(x => x.VersionNumber)
            .Select(x => new
            {
                x.DocumentFile!.OriginalFileName,
                x.DocumentFile.ContentType,
                x.DocumentFile.StoragePath
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return null;
        }

        var content = await _fileStorageService.ReadAsync(version.StoragePath, cancellationToken);
        return new FileDownloadDto
        {
            FileName = version.OriginalFileName,
            ContentType = version.ContentType,
            Content = content
        };
    }

    public async Task<FileDownloadDto?> GetDocumentVersionContentAsync(Guid projectId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        var version = await _dbContext.ProjectDocumentVersions
            .AsNoTracking()
            .Where(x => x.Id == versionId
                        && x.ProjectDocumentId == documentId
                        && x.ProjectDocument != null
                        && x.ProjectDocument.ProjectId == projectId
                        && x.ProjectDocument.Activo
                        && x.Activo)
            .Select(x => new
            {
                x.DocumentFile!.OriginalFileName,
                x.DocumentFile.ContentType,
                x.DocumentFile.StoragePath
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (version is null)
        {
            return null;
        }

        var content = await _fileStorageService.ReadAsync(version.StoragePath, cancellationToken);
        return new FileDownloadDto
        {
            FileName = version.OriginalFileName,
            ContentType = version.ContentType,
            Content = content
        };
    }

    public async Task<bool> DeleteDocumentAsync(Guid projectId, Guid documentId, Guid deletedById, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.ProjectDocuments
            .FirstOrDefaultAsync(x => x.Id == documentId && x.ProjectId == projectId && x.Activo, cancellationToken);

        if (document is null)
        {
            return false;
        }

        var userExists = await _dbContext.Users.AnyAsync(x => x.Id == deletedById, cancellationToken);
        if (!userExists)
        {
            throw new ArgumentException("El usuario actual no existe");
        }

        document.Activo = false;
        document.DeletedAt = DateTime.UtcNow;
        document.DeletedById = deletedById;
        document.FechaActualizacion = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void ApplyRequest(Project project, ProjectUpsertRequestDto request)
    {
        project.ClienteId = request.ClientId;
        project.Nombre = request.Name.Trim();
        project.Descripcion = Normalize(request.Description);
        project.ResponsableId = request.ResponsibleId;
        project.Estado = ParseStatus(request.Status);
        project.Prioridad = ParsePriority(request.Priority);
        project.FechaInicio = request.StartDate;
        project.FechaEntregaEstimada = request.EstimatedDeliveryDate;
        project.FechaCierre = request.ClosingDate;
        project.Presupuesto = request.Budget;
        project.PorcentajeAvance = request.ProgressPercentage;
        project.Activo = request.IsActive;
    }

    private static string? Normalize(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string? NormalizeNotes(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length <= 500
            ? normalized
            : normalized[..500];
    }

    private static ProjectStatus ParseStatus(string status)
    {
        return Enum.TryParse<ProjectStatus>(status, true, out var parsed)
            ? parsed
            : ProjectStatus.Planificacion;
    }

    private static ProjectPriority ParsePriority(string priority)
    {
        return Enum.TryParse<ProjectPriority>(priority, true, out var parsed)
            ? parsed
            : ProjectPriority.Media;
    }

    private static ProjectDocumentType ParseDocumentType(string type)
    {
        if (!Enum.TryParse<ProjectDocumentType>(type, true, out var parsed))
        {
            throw new ArgumentException("Tipo de documento inválido");
        }

        return parsed;
    }

    private static string GetTypeLabel(ProjectDocumentType type)
    {
        return type switch
        {
            ProjectDocumentType.Contract => "Contrato",
            ProjectDocumentType.Scope => "Alcance",
            ProjectDocumentType.Proposal => "Propuesta",
            ProjectDocumentType.Deliverable => "Entregable",
            ProjectDocumentType.Legal => "Legal",
            ProjectDocumentType.Invoice => "Factura",
            _ => "General"
        };
    }

    private static string NormalizeFileName(string fileName)
    {
        var normalized = fileName.Trim();
        if (normalized.Length <= 260)
        {
            return normalized;
        }

        return normalized[..260];
    }

    private static string NormalizeTitle(string title)
    {
        var normalized = title.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("El documento debe tener un título");
        }

        return normalized.Length <= 200
            ? normalized
            : normalized[..200];
    }

    private static Expression<Func<Project, ProjectDetailDto>> ToDetailDto()
    {
        return x => new ProjectDetailDto
        {
            Id = x.Id,
            ClientId = x.ClienteId,
            ClientName = x.Cliente != null ? x.Cliente.CommercialName : string.Empty,
            Name = x.Nombre,
            Description = x.Descripcion,
            ResponsibleId = x.ResponsableId,
            ResponsibleName = x.Responsable != null ? x.Responsable.FullName : null,
            Status = x.Estado.ToString(),
            Priority = x.Prioridad.ToString(),
            StartDate = x.FechaInicio,
            EstimatedDeliveryDate = x.FechaEntregaEstimada,
            ClosingDate = x.FechaCierre,
            Budget = x.Presupuesto,
            ProgressPercentage = x.PorcentajeAvance,
            IsActive = x.Activo,
            CreatedAt = x.FechaCreacion,
            UpdatedAt = x.FechaActualizacion
        };
    }

    private async Task ValidateReferencesAsync(ProjectUpsertRequestDto request, CancellationToken cancellationToken, Guid? currentResponsibleId = null)
    {
        var clientExists = await _dbContext.Clients.AnyAsync(x => x.Id == request.ClientId, cancellationToken);
        if (!clientExists)
        {
            throw new ArgumentException("El cliente seleccionado no existe");
        }

        if (request.ResponsibleId.HasValue && request.ResponsibleId != currentResponsibleId)
        {
            var analystExists = await _dbContext.Users.AnyAsync(
                x => x.Id == request.ResponsibleId.Value && x.Activo && x.DeveloperId != null && x.UserRoles.Any(r => r.Role.Name == RoleNames.Analyst && r.Role.Activo),
                cancellationToken);
            if (!analystExists)
            {
                throw new ArgumentException("El analista a cargo seleccionado no existe, no está activo o no tiene perfil remunerable");
            }
        }
    }

    private async Task ValidateDocumentUploadAsync(Guid projectId, Guid uploadedById, string fileName, string contentType, byte[] content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("El documento debe tener nombre de archivo");
        }

        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Solo se admiten documentos PDF");
        }

        var projectExists = await _dbContext.Projects.AnyAsync(x => x.Id == projectId, cancellationToken);
        if (!projectExists)
        {
            throw new ArgumentException("El proyecto indicado no existe");
        }

        var userExists = await _dbContext.Users.AnyAsync(x => x.Id == uploadedById, cancellationToken);
        if (!userExists)
        {
            throw new ArgumentException("El usuario cargador no existe");
        }

        if (!string.IsNullOrWhiteSpace(contentType)
            && !string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(contentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Solo se admiten documentos PDF");
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

    private async Task<ProjectDocumentListItemDto?> GetDocumentListItemAsync(Guid projectId, Guid documentId, CancellationToken cancellationToken)
    {
        return await _dbContext.ProjectDocuments
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.Id == documentId && x.Activo)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Type,
                LatestVersion = x.Versions
                    .Where(v => v.Activo)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new
                    {
                        v.Id,
                        v.VersionNumber,
                        v.FechaCreacion,
                        v.DocumentFile!.OriginalFileName,
                        v.DocumentFile.ContentType,
                        v.DocumentFile.SizeBytes,
                        UploadedByName = v.UploadedBy != null ? v.UploadedBy.FullName : string.Empty
                    })
                    .FirstOrDefault()
            })
            .Where(x => x.LatestVersion != null)
            .Select(x => new ProjectDocumentListItemDto
            {
                Id = x.Id,
                Title = x.Title,
                Type = x.Type.ToString(),
                TypeLabel = GetTypeLabel(x.Type),
                CurrentVersionId = x.LatestVersion!.Id,
                CurrentVersionNumber = x.LatestVersion.VersionNumber,
                FileName = x.LatestVersion.OriginalFileName,
                ContentType = x.LatestVersion.ContentType,
                SizeBytes = x.LatestVersion.SizeBytes,
                UploadedByName = x.LatestVersion.UploadedByName,
                UploadedAt = x.LatestVersion.FechaCreacion
            })
            .FirstOrDefaultAsync(cancellationToken);
    }
}
