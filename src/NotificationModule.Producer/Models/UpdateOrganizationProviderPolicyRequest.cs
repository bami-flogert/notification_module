namespace NotificationModule.Producer.Models;

public sealed class UpdateOrganizationProviderPolicyRequest
{
    public string PreferredProvider { get; set; } = null!;

    public string? FallbackProviders { get; set; }
}
