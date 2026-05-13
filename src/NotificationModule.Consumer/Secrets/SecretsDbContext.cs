using Microsoft.EntityFrameworkCore;

namespace NotificationModule.Consumer.Secrets;

public sealed class SecretsDbContext : DbContext
{
    public SecretsDbContext(DbContextOptions<SecretsDbContext> options)
        : base(options)
    {
    }

    public DbSet<ProviderSecretRecord> ProviderSecrets => Set<ProviderSecretRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderSecretRecord>(e =>
        {
            e.ToTable("provider_secrets");
            e.HasKey(x => x.Provider);
            e.Property(x => x.Provider).HasMaxLength(64);
            e.Property(x => x.EncryptedPayload).IsRequired();
            e.Property(x => x.Nonce).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();
        });
    }
}
