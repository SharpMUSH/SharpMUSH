using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Plugins.Lsp;

[SharpPlugin]
public sealed class LspPlugin : PluginBase, IServiceRegistrar, IEndpointContributor
{
	public override string Id => "lsp";
	public void RegisterServices(IServiceCollection services)
	{
		services.AddOptions<LspOptions>().BindConfiguration("Lsp");
		services.AddSingleton<LspResources>();
		services.AddSingleton<LspEndpoint>();
	}
	public void MapEndpoints(IEndpointRouteBuilder endpoints)
	{
		var options = endpoints.ServiceProvider.GetRequiredService<IOptions<LspOptions>>().Value;
		if (!options.Enabled) return;
		endpoints.Map(options.Path, context => context.RequestServices.GetRequiredService<LspEndpoint>().InvokeAsync(context))
			.RequireAuthorization(AnalysisTransportSecurity.Policy).RequireRateLimiting(AnalysisTransportSecurity.RateLimitPolicy);
	}
}
