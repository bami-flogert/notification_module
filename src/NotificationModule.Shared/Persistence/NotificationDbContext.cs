using Microsoft.EntityFrameworkCore;

namespace NotificationModule.Shared.Persistence;

public class NotificationDbContext : DbContext
{
    public NotificationDbContext(DbContextOptions options)
        : base(options)
    {
    }

    public DbSet<OrganizationRecord> Organizations => Set<OrganizationRecord>();
    public DbSet<ProviderSecretRecord> ProviderSecrets => Set<ProviderSecretRecord>();
    public DbSet<AppointmentRecord> Appointments => Set<AppointmentRecord>();
    public DbSet<ScheduledNotificationRecord> ScheduledNotifications => Set<ScheduledNotificationRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OrganizationRecord>(e =>
        {
            e.ToTable("organizations");
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TimeZone).HasMaxLength(100).IsRequired();
            e.Property(x => x.OpenMrsBaseUrl).HasMaxLength(500);
            e.Property(x => x.IsEnabled).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        modelBuilder.Entity<ProviderSecretRecord>(e =>
        {
            e.ToTable("provider_secrets");
            e.HasKey(x => new { x.OrganizationId, x.Provider });
            e.Property(x => x.Provider).HasMaxLength(64).IsRequired();
            e.Property(x => x.EncryptedPayload).IsRequired();
            e.Property(x => x.Nonce).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasOne(x => x.Organization)
                .WithMany(x => x.ProviderSecrets)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppointmentRecord>(e =>
        {
            e.ToTable("appointments");
            e.HasKey(x => x.Id);
            e.Property(x => x.AppointmentUuid).HasMaxLength(128).IsRequired();
            e.Property(x => x.PatientUuid).HasMaxLength(128).IsRequired();
            e.Property(x => x.PatientName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PatientPhone).HasMaxLength(64).IsRequired();
            e.Property(x => x.PatientEmail).HasMaxLength(320).IsRequired();
            e.Property(x => x.StartDateTime).IsRequired();
            e.Property(x => x.Status).HasMaxLength(64).IsRequired();
            e.Property(x => x.Location).HasMaxLength(250);
            e.Property(x => x.Instructions).HasMaxLength(1000);
            e.Property(x => x.SourceSystem).HasMaxLength(64).IsRequired();
            e.Property(x => x.RawSourcePayload).HasColumnType("jsonb");
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => new { x.OrganizationId, x.AppointmentUuid }).IsUnique();
            e.HasIndex(x => new { x.OrganizationId, x.StartDateTime });
            e.HasOne(x => x.Organization)
                .WithMany(x => x.Appointments)
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScheduledNotificationRecord>(e =>
        {
            e.ToTable("scheduled_notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.ReminderType).HasMaxLength(32).IsRequired();
            e.Property(x => x.ScheduledSendAt).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
            e.HasIndex(x => new { x.OrganizationId, x.Status, x.ScheduledSendAt });
            e.HasOne(x => x.Organization)
                .WithMany()
                .HasForeignKey(x => x.OrganizationId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Appointment)
                .WithMany(x => x.ScheduledNotifications)
                .HasForeignKey(x => x.AppointmentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
