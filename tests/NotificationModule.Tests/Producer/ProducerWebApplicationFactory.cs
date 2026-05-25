using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace NotificationModule.Tests.Producer;

public sealed class ProducerWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestApiKey = "integration-test-api-key";
    public const string DefaultOrganizationKey = "default";

    private readonly string _databaseName = Guid.NewGuid().ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NotificationDb:ConnectionString"] = "Host=unused;Database=unused",
                ["NotificationDb:TestingDatabaseName"] = _databaseName,
                ["ApiKeys:Seed:Default"] = TestApiKey,
                ["Organizations:Default:Key"] = DefaultOrganizationKey,
            });
        });
    }

    public HttpClient CreateAuthenticatedClient(string organizationKey = DefaultOrganizationKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        client.DefaultRequestHeaders.Add("X-Organization-Key", organizationKey);
        return client;
    }
}
