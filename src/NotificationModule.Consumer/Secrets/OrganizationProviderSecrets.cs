namespace NotificationModule.Consumer.Secrets;

public sealed class OrganizationProviderSecrets
{
    public required SwiftSendSecretPayload SwiftSend { get; init; }
    public required SecurePostSecretPayload SecurePost { get; init; }
    public required LegacyLinkSecretPayload LegacyLink { get; init; }
    public required AsyncFlowSecretPayload AsyncFlow { get; init; }
}
