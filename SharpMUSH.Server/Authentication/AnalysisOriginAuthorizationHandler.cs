using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SharpMUSH.Server.Authentication;

/// <summary>Host-owned browser-origin policy for optional analysis transports.</summary>
public sealed class AnalysisTransportOptions
{
	public const string Section = "AnalysisTransports";
	/// <summary>Exact HTTP(S) origins. Empty denies all requests carrying Origin; native clients may omit it.</summary>
	public string[] AllowedOrigins { get; set; } = [];
}

public sealed class AnalysisOriginRequirement : IAuthorizationRequirement;

public sealed class AnalysisOriginAuthorizationHandler(IOptions<AnalysisTransportOptions> options)
	: AuthorizationHandler<AnalysisOriginRequirement>
{
	protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AnalysisOriginRequirement requirement)
	{
		if (context.Resource is not HttpContext http) return Task.CompletedTask;
		if (!http.Request.Headers.TryGetValue("Origin", out var values))
		{
			context.Succeed(requirement);
			return Task.CompletedTask;
		}
		if (values.Count == 1 && Parse(values[0]) is { } origin && options.Value.AllowedOrigins.Any(value =>
			Parse(value) is { } allowed && origin.Scheme == allowed.Scheme &&
			origin.IdnHost.Equals(allowed.IdnHost, StringComparison.OrdinalIgnoreCase) && origin.Port == allowed.Port))
			context.Succeed(requirement);
		return Task.CompletedTask;
	}

	private static Uri? Parse(string? value)
		=> Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" &&
			uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
			? uri : null;
}
