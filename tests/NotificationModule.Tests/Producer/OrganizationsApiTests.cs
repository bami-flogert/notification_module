using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace NotificationModule.Tests.Producer;

public sealed class OrganizationsApiTests : IClassFixture<ProducerWebApplicationFactory>
{
    private readonly ProducerWebApplicationFactory _factory;

    public OrganizationsApiTests(ProducerWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PutProviders_returns_unauthorized_without_api_key()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync(
            $"/api/organizations/{ProducerWebApplicationFactory.DefaultOrganizationKey}/providers",
            new { preferredProvider = "SwiftSend", fallbackProviders = "LegacyLink" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutProviders_returns_unauthorized_when_organization_does_not_exist()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ProducerWebApplicationFactory.TestApiKey);

        using var response = await client.PutAsJsonAsync(
            "/api/organizations/missing-org/providers",
            new { preferredProvider = "SwiftSend", fallbackProviders = "LegacyLink" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PutProviders_returns_bad_request_for_invalid_provider()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PutAsJsonAsync(
            $"/api/organizations/{ProducerWebApplicationFactory.DefaultOrganizationKey}/providers",
            new { preferredProvider = "NotAProvider", fallbackProviders = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutProviders_updates_policy_for_valid_request()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.PutAsJsonAsync(
            $"/api/organizations/{ProducerWebApplicationFactory.DefaultOrganizationKey}/providers",
            new { preferredProvider = "LegacyLink", fallbackProviders = "AsyncFlow,SecurePost" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LegacyLink", json.GetProperty("preferredProvider").GetString());
        Assert.Equal("AsyncFlow,SecurePost", json.GetProperty("fallbackProviders").GetString());
        Assert.Equal(
            ProducerWebApplicationFactory.DefaultOrganizationKey,
            json.GetProperty("organizationKey").GetString());
    }
}
