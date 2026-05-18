using Microsoft.EntityFrameworkCore;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Consumer.Secrets;

public sealed class SecretsDbContext : NotificationDbContext
{
    public SecretsDbContext(DbContextOptions<SecretsDbContext> options)
        : base(options)
    {
    }
}
