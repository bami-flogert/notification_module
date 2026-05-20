using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Consumer.Secrets;

/// <summary>Design-time factory for <c>dotnet ef</c> migrations (local Postgres defaults).</summary>
public sealed class SecretsDbContextFactory : IDesignTimeDbContextFactory<SecretsDbContext>
{
    public SecretsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SecretsDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=notification;Username=notification;Password=notification",
            npgsql => npgsql.MigrationsAssembly(typeof(NotificationDbContext).Assembly.FullName));
        return new SecretsDbContext(optionsBuilder.Options);
    }
}
