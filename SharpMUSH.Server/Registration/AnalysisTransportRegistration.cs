using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using SharpMUSH.Server.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Server.Registration;

/// <summary>Host security and reserved-route behavior independent of transport SDKs.</summary>
public static class AnalysisTransportRegistration
{
	public static IServiceCollection AddAnalysisTransportPolicy(this IServiceCollection services, IConfiguration? configuration = null)
	{
		services.AddOptions<AnalysisTransportOptions>();
		if (configuration is not null) services.Configure<AnalysisTransportOptions>(configuration.GetSection(AnalysisTransportOptions.Section));
		services.AddSingleton<IAuthorizationHandler, AnalysisOriginAuthorizationHandler>();
		services.AddAuthorization(options => options.AddPolicy(AnalysisTransportSecurity.Policy,
			policy => policy.AddAuthenticationSchemes(AnalysisTransportSecurity.AuthenticationScheme).RequireAuthenticatedUser().AddRequirements(new AnalysisOriginRequirement())));
		return services;
	}

	public static void MapAnalysisTransportFallback(this IEndpointRouteBuilder endpoints, IConfiguration configuration)
	{
		// A missing/disabled/failed plugin must never appear healthy via a successful SPA response.
		foreach (var path in new[] { configuration["Mcp:Path"] ?? "/mcp", configuration["Lsp:Path"] ?? "/lsp" })
		{
			endpoints.MapFallback(path, context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; });
			endpoints.MapFallback(path.TrimEnd('/') + "/{**path}", context => { context.Response.StatusCode = StatusCodes.Status404NotFound; return Task.CompletedTask; });
		}
	}
}
