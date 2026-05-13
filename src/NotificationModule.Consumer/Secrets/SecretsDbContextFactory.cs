using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NotificationModule.Consumer.Secrets;

/// <summary>Design-time factory for <c>dotnet ef</c> migrations (local Postgres defaults).</summary>
public sealed class SecretsDbContextFactory : IDesignTimeDbContextFactory<SecretsDbContext>
{
    public SecretsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SecretsDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=notification;Username=notification;Password=notification");
        return new SecretsDbContext(optionsBuilder.Options);
    }
}
