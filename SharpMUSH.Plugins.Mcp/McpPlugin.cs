using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Server.Mcp;

namespace SharpMUSH.Plugins.Mcp;

/// <summary>Optional boot-time transport; the host owns identity, authorization and analysis.</summary>
[SharpPlugin]
public sealed class McpPlugin : PluginBase, IServiceRegistrar, IEndpointContributor
{
	public override string Id => "mcp";

	public void RegisterServices(IServiceCollection services)
	{
		services.AddOptions<McpOptions>().BindConfiguration(McpOptions.Section);
		services.AddHttpContextAccessor();
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton<McpDocumentStore>();
		services.AddSingleton<McpHttpAdmission>();
		services.AddSingleton(provider => new McpAnalysisExecutor(provider.GetRequiredService<AnalysisExecutor>()));
		services.AddMcpServer().WithHttpTransport(options => options.Stateless = true).WithTools<MushTools>();
	}

	public void MapEndpoints(IEndpointRouteBuilder endpoints)
	{
		_ = endpoints.ServiceProvider.GetRequiredService<IAnalysisSessionFactory>();
		var options = endpoints.ServiceProvider.GetRequiredService<IOptions<McpOptions>>().Value;
		var logger = endpoints.ServiceProvider.GetRequiredService<ILogger<McpPlugin>>();
		if (!options.Enabled)
		{
			logger.LogInformation("[MCP] Transport disabled by configuration.");
			return;
		}
		endpoints.MapMcp(options.Path).WithMcpAdmission()
			.RequireRateLimiting(AnalysisTransportSecurity.RateLimitPolicy)
			.RequireAuthorization(AnalysisTransportSecurity.Policy);
		// Stateless transport has no server-initiated SSE stream. An explicit endpoint avoids
		// the host's reserved-path fallback hiding method-not-allowed as 404.
		endpoints.MapGet(options.Path, () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed))
			.RequireRateLimiting(AnalysisTransportSecurity.RateLimitPolicy)
			.RequireAuthorization(AnalysisTransportSecurity.Policy);
		logger.LogInformation("[MCP] Transport mapped at {Path} with host analysis policy.", options.Path);
	}
}
