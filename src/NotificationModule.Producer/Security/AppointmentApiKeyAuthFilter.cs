using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NotificationModule.Producer.Services;
using NotificationModule.Shared.Models;

namespace NotificationModule.Producer.Security;

public sealed class AppointmentApiKeyAuthFilter : IAsyncActionFilter
{
    private readonly OrganizationApiKeyService _apiKeys;
    private readonly IConfiguration _configuration;

    public AppointmentApiKeyAuthFilter(OrganizationApiKeyService apiKeys, IConfiguration configuration)
    {
        _apiKeys = apiKeys;
        _configuration = configuration;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;

        var message = context.ActionArguments.Values.OfType<AppointmentMessage>().FirstOrDefault();
        var routeOrgKey = context.ActionArguments.TryGetValue("organizationKey", out var routeValue)
            ? routeValue as string
            : null;

        var headerOrgKey = http.Request.Headers.TryGetValue("X-Organization-Key", out var headerValues)
            ? headerValues.FirstOrDefault()
            : null;

        var orgKey = FirstNonEmpty(
            routeOrgKey,
            headerOrgKey,
            message?.OrganizationKey,
            _configuration["Organizations:Default:Key"]);

        var apiKey = http.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyValues)
            ? apiKeyValues.FirstOrDefault()
            : null;

        if (string.IsNullOrWhiteSpace(orgKey) || string.IsNullOrWhiteSpace(apiKey))
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Missing organization key or API key." });
            return;
        }

        var (success, forbidden, _) = await _apiKeys.ValidateAsync(orgKey, apiKey, http.RequestAborted);
        if (forbidden)
        {
            context.Result = new ObjectResult(new { message = "Organization disabled." }) { StatusCode = 403 };
            return;
        }

        if (!success)
        {
            context.Result = new UnauthorizedObjectResult(new { message = "Invalid API key." });
            return;
        }

        await next();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}

