using Microsoft.AspNetCore.Mvc;
using NotificationModule.Producer.Models;
using NotificationModule.Producer.Security;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Persistence;

namespace NotificationModule.Producer.Controllers;

[ApiController]
[Route("api/organizations")]
[ServiceFilter(typeof(AppointmentApiKeyAuthFilter))]
public sealed class OrganizationsController : ControllerBase
{
    private readonly OrganizationProviderPolicyService _policyService;

    public OrganizationsController(OrganizationProviderPolicyService policyService)
    {
        _policyService = policyService;
    }

    [HttpPut("{organizationKey}/providers")]
    public async Task<IActionResult> PutProviders(
        string organizationKey,
        [FromBody] UpdateOrganizationProviderPolicyRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest("Body is required.");

        OrganizationRecord? organization;
        try
        {
            organization = await _policyService.UpdatePolicyAsync(
                organizationKey,
                request.PreferredProvider,
                request.FallbackProviders,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }

        if (organization is null)
            return NotFound(new { message = $"Organization '{organizationKey}' was not found." });

        return Ok(new
        {
            organizationKey = organization.Key,
            preferredProvider = organization.PreferredProvider,
            fallbackProviders = organization.FallbackProviders,
        });
    }
}
