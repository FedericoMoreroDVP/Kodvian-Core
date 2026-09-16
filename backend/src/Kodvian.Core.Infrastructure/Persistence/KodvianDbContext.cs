using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Domain.Entities;
using Kodvian.Core.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Kodvian.Core.Infrastructure.Persistence;

public class KodvianDbContext : DbContext
{
    public static readonly Guid AdministratorRoleId = Guid.Parse("95e52dc4-44f5-4b0b-aabf-4044f28cc55a");
    public static readonly Guid OperativeRoleId = Guid.Parse("a77176f8-d33a-4c23-b613-f2e73093e1b7");
    public static readonly Guid ReadOnlyRoleId = Guid.Parse("24b2ab35-0c84-4fa8-9c35-eecf5f476bb8");
    public static readonly Guid AnalystRoleId = Guid.Parse("7bbad1f2-2c26-4dc0-bf74-2213871f7d52");
    public static readonly Guid DeveloperRoleId = Guid.Parse("58df40de-1019-4dcf-81dc-55a8a2d06235");

    public KodvianDbContext(DbContextOptions<KodvianDbContext> options) : base(options)
    {
    }

    public DbSet<Client> Clients => Set<Client>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<FinancialCategory> FinancialCategories => Set<FinancialCategory>();
    public DbSet<FinancialMovement> FinancialMovements => Set<FinancialMovement>();
    public DbSet<Provider> Providers => Set<Provider>();
    public DbSet<Developer> Developers => Set<Developer>();
    public DbSet<ProjectDeveloperAssignment> ProjectDeveloperAssignments => Set<ProjectDeveloperAssignment>();
    public DbSet<ProjectDeveloperContract> ProjectDeveloperContracts => Set<ProjectDeveloperContract>();
    public DbSet<DeveloperPayment> DeveloperPayments => Set<DeveloperPayment>();
    public DbSet<DocumentFile> DocumentFiles => Set<DocumentFile>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<ProjectDocumentVersion> ProjectDocumentVersions => Set<ProjectDocumentVersion>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TaskAttachment>(entity =>
        {
            entity.ToTable("TaskAttachments");
            entity.Property(a => a.FileName).HasMaxLength(255).IsRequired();
            entity.Property(a => a.ContentType).HasMaxLength(150).IsRequired();
            entity.Property(a => a.StoragePath).HasMaxLength(1024).IsRequired();
            entity.HasIndex(a => new { a.TaskId, a.UploadId }).IsUnique();
            entity.HasOne(a => a.Task).WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.UploadedBy).WithMany().HasForeignKey(a => a.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("Clients");
            entity.Property(x => x.CommercialName).IsRequired().HasMaxLength(200);
            entity.Property(x => x.LegalName).HasMaxLength(220);
            entity.Property(x => x.TaxId).HasMaxLength(20);
            entity.Property(x => x.ContactName).HasMaxLength(120);
            entity.Property(x => x.ContactEmail).HasMaxLength(120);
            entity.Property(x => x.ContactPhone).HasMaxLength(40);
            entity.Property(x => x.Address).HasMaxLength(180);
            entity.Property(x => x.City).HasMaxLength(120);
            entity.Property(x => x.Province).HasMaxLength(120);
            entity.Property(x => x.Country).HasMaxLength(120);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.ServiceType).HasMaxLength(120);
            entity.Property(x => x.MonthlyAmount).HasColumnType("numeric(18,2)");
            entity.Property(x => x.BillingDay);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.CommercialName);
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.ToTable("Projects");
            entity.Property(x => x.Nombre).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Descripcion).HasMaxLength(2000);
            entity.Property(x => x.GoogleDriveFolderUrl).HasMaxLength(2048);
            entity.Property(x => x.Estado).HasConversion<int>();
            entity.Property(x => x.Prioridad).HasConversion<int>();
            entity.Property(x => x.Presupuesto).HasColumnType("numeric(18,2)");
            entity.Property(x => x.PorcentajeAvance).HasDefaultValue(0);
            entity.HasIndex(x => x.ClienteId);
            entity.HasIndex(x => x.Estado);
            entity.HasIndex(x => x.Prioridad);
            entity.HasIndex(x => new { x.ClienteId, x.FechaCreacion });
            entity.HasIndex(x => new { x.Estado, x.Prioridad, x.FechaCreacion });

            entity.HasOne(x => x.Cliente)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.ClienteId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Responsable)
                .WithMany(x => x.Projects)
                .HasForeignKey(x => x.ResponsableId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.ToTable("Tasks");
            entity.Property(x => x.Titulo).IsRequired().HasMaxLength(200);
            entity.Property(x => x.Descripcion).HasMaxLength(2000);
            entity.Property(x => x.Estado).HasConversion<int>();
            entity.Property(x => x.Prioridad).HasConversion<int>();
            entity.Property(x => x.HorasEstimadas).HasColumnType("numeric(8,2)");
            entity.Property(x => x.HorasReales).HasColumnType("numeric(8,2)");
            entity.Property(x => x.OrdenKanban).HasDefaultValue(0);
            entity.HasIndex(x => x.ProyectoId);
            entity.HasIndex(x => x.DeveloperId);
            entity.HasIndex(x => x.ResponsableId);
            entity.HasIndex(x => x.CreadoPorId);
            entity.HasIndex(x => x.Estado);
            entity.HasIndex(x => x.Prioridad);
            entity.HasIndex(x => x.FechaVencimiento);
            entity.HasIndex(x => new { x.Estado, x.OrdenKanban, x.FechaCreacion });
            entity.HasIndex(x => new { x.ProyectoId, x.Estado });

            entity.HasOne(x => x.Proyecto)
                .WithMany(x => x.Tareas)
                .HasForeignKey(x => x.ProyectoId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Developer)
                .WithMany(x => x.Tasks)
                .HasForeignKey(x => x.DeveloperId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Responsable)
                .WithMany(x => x.Tasks)
                .HasForeignKey(x => x.ResponsableId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.CreadoPor)
                .WithMany(x => x.CreatedTasks)
                .HasForeignKey(x => x.CreadoPorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<FinancialCategory>(entity =>
        {
            entity.ToTable("FinancialCategories");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(120);
            entity.Property(x => x.MovementType).HasConversion<int>();
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.MovementType);
        });

        modelBuilder.Entity<Provider>(entity =>
        {
            entity.ToTable("Providers");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(160);
            entity.Property(x => x.TaxId).HasMaxLength(20);
            entity.Property(x => x.Email).HasMaxLength(120);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Developer>(entity =>
        {
            entity.ToTable("Developers");
            entity.Property(x => x.FullName).IsRequired().HasMaxLength(160);
            entity.Property(x => x.Email).HasMaxLength(120);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.TaxId).HasMaxLength(20);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.FullName);
            entity.HasIndex(x => x.Email);
            entity.HasIndex(x => x.TaxId);
        });

        modelBuilder.Entity<ProjectDeveloperContract>(entity =>
        {
            entity.ToTable("ProjectDeveloperContracts");
            entity.Property(x => x.PaymentMode).HasConversion<int>();
            entity.Property(x => x.Percentage).HasColumnType("numeric(5,2)");
            entity.Property(x => x.AgreedAmount).HasColumnType("numeric(18,2)");
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.DeveloperId);
            entity.HasIndex(x => new { x.ProjectId, x.DeveloperId, x.Activo });
            entity.HasIndex(x => new { x.DeveloperId, x.FechaCreacion });

            entity.HasCheckConstraint("CK_ProjectDeveloperContracts_Percentage", "\"Percentage\" IS NULL OR (\"Percentage\" >= 0 AND \"Percentage\" <= 100)");
            entity.HasCheckConstraint("CK_ProjectDeveloperContracts_FixedAmount", "\"AgreedAmount\" IS NULL OR \"AgreedAmount\" > 0");

            entity.HasOne(x => x.Project)
                .WithMany(x => x.DeveloperContracts)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Developer)
                .WithMany(x => x.Contracts)
                .HasForeignKey(x => x.DeveloperId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectDeveloperAssignment>(entity =>
        {
            entity.ToTable("ProjectDeveloperAssignments");
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.DeveloperId);
            entity.HasIndex(x => new { x.ProjectId, x.DeveloperId }).IsUnique();

            entity.HasOne(x => x.Project)
                .WithMany(x => x.DeveloperAssignments)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Developer)
                .WithMany(x => x.ProjectAssignments)
                .HasForeignKey(x => x.DeveloperId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DeveloperPayment>(entity =>
        {
            entity.ToTable("DeveloperPayments");
            entity.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            entity.Property(x => x.Reference).HasMaxLength(120);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.ContractId);
            entity.HasIndex(x => x.PaymentDate);
            entity.HasIndex(x => new { x.ContractId, x.PeriodYear, x.PeriodMonth });

            entity.HasOne(x => x.Contract)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.ContractId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FinancialMovement>(entity =>
        {
            entity.ToTable("FinancialMovements");
            entity.Property(x => x.MovementType).HasConversion<int>();
            entity.Property(x => x.Description).IsRequired().HasMaxLength(500);
            entity.Property(x => x.Amount).HasColumnType("numeric(18,2)");
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.PaymentMethod).HasMaxLength(80);
            entity.Property(x => x.ReceiptNumber).HasMaxLength(80);
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.HasIndex(x => x.MovementDate);
            entity.HasIndex(x => x.DueDate);
            entity.HasIndex(x => x.MovementType);
            entity.HasIndex(x => x.Status);
            entity.HasIndex(x => x.CategoryId);
            entity.HasIndex(x => x.ClientId);
            entity.HasIndex(x => x.ProviderId);
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => new { x.MovementType, x.Status, x.DueDate });
            entity.HasIndex(x => new { x.MovementDate, x.FechaCreacion });

            entity.HasOne(x => x.Category)
                .WithMany(x => x.FinancialMovements)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Client)
                .WithMany()
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Provider)
                .WithMany(x => x.FinancialMovements)
                .HasForeignKey(x => x.ProviderId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.Project)
                .WithMany()
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.CreatedBy)
                .WithMany(x => x.FinancialMovementsCreated)
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentFile>(entity =>
        {
            entity.ToTable("DocumentFiles");
            entity.Property(x => x.OriginalFileName).IsRequired().HasMaxLength(260);
            entity.Property(x => x.StoredFileName).IsRequired().HasMaxLength(160);
            entity.Property(x => x.ContentType).IsRequired().HasMaxLength(80);
            entity.Property(x => x.StoragePath).IsRequired().HasMaxLength(260);
            entity.Property(x => x.Sha256).IsRequired().HasMaxLength(64);
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.FinancialMovementId);
            entity.HasIndex(x => x.DeveloperPaymentId);
            entity.HasIndex(x => x.UploadedById);
            entity.HasIndex(x => x.Sha256);
            entity.HasCheckConstraint("CK_DocumentFiles_Owner", "((\"ProjectId\" IS NOT NULL)::int + (\"FinancialMovementId\" IS NOT NULL)::int + (\"DeveloperPaymentId\" IS NOT NULL)::int) = 1");

            entity.HasOne(x => x.Project)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(x => x.FinancialMovement)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.FinancialMovementId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.DeveloperPayment)
                .WithMany(x => x.Documents)
                .HasForeignKey(x => x.DeveloperPaymentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.UploadedBy)
                .WithMany(x => x.UploadedDocuments)
                .HasForeignKey(x => x.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProjectDocument>(entity =>
        {
            entity.ToTable("ProjectDocuments");
            entity.Property(x => x.Type).HasConversion<int>();
            entity.Property(x => x.Title).IsRequired().HasMaxLength(200);
            entity.HasIndex(x => x.ProjectId);
            entity.HasIndex(x => x.Type);
            entity.HasIndex(x => new { x.ProjectId, x.Activo });

            entity.HasOne(x => x.Project)
                .WithMany(x => x.ProjectDocuments)
                .HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.CreatedBy)
                .WithMany(x => x.ProjectDocumentsCreated)
                .HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.DeletedBy)
                .WithMany(x => x.ProjectDocumentsDeleted)
                .HasForeignKey(x => x.DeletedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ProjectDocumentVersion>(entity =>
        {
            entity.ToTable("ProjectDocumentVersions");
            entity.Property(x => x.VersionNumber).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => x.ProjectDocumentId);
            entity.HasIndex(x => x.DocumentFileId).IsUnique();
            entity.HasIndex(x => x.UploadedById);
            entity.HasIndex(x => new { x.ProjectDocumentId, x.VersionNumber }).IsUnique();

            entity.HasOne(x => x.ProjectDocument)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.ProjectDocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.DocumentFile)
                .WithMany()
                .HasForeignKey(x => x.DocumentFileId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.UploadedBy)
                .WithMany(x => x.ProjectDocumentVersionsUploaded)
                .HasForeignKey(x => x.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("Roles");
            entity.Property(x => x.Name).IsRequired().HasMaxLength(80);
            entity.Property(x => x.Description).HasMaxLength(200);
            entity.HasIndex(x => x.Name).IsUnique();

            entity.HasData(
                new Role
                {
                    Id = AdministratorRoleId,
                    Name = RoleNames.Administrator,
                    Description = "Acceso total al sistema",
                    FechaCreacion = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Activo = true
                },
                new Role
                {
                    Id = OperativeRoleId,
                    Name = RoleNames.Operative,
                    Description = "Acceso operativo a clientes, proyectos y tareas",
                    FechaCreacion = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Activo = true
                },
                new Role
                {
                    Id = ReadOnlyRoleId,
                    Name = RoleNames.ReadOnly,
                    Description = "Acceso de solo lectura",
                    FechaCreacion = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Activo = true
                },
                new Role
                {
                    Id = AnalystRoleId,
                    Name = RoleNames.Analyst,
                    Description = "Gestion operativa de clientes, equipo, proyectos y tareas",
                    FechaCreacion = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Activo = true
                },
                new Role
                {
                    Id = DeveloperRoleId,
                    Name = RoleNames.Developer,
                    Description = "Acceso limitado a proyectos y tareas asignadas",
                    FechaCreacion = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    Activo = true
                }
            );
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(x => x.FullName).IsRequired().HasMaxLength(160);
            entity.Property(x => x.Email).IsRequired().HasMaxLength(120);
            entity.Property(x => x.PasswordHash).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.DeveloperId);

            entity.HasOne(x => x.Role)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(x => x.Developer)
                .WithMany(x => x.Users)
                .HasForeignKey(x => x.DeveloperId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
