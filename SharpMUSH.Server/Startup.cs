using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpMUSH.Server.Registration;
using SharpMUSH.Server.Services;
using SharpMUSH.Library.Plugins;

namespace SharpMUSH.Server;

public class Startup(string colorFile, string natsUrl)
{
	// Cache name for the dedicated compiled boolean-lock expression cache.
	// Must match the [FromKeyedServices] key used in BooleanExpressionParser.
	public const string CompiledExpressionsCacheName = "compiled-expressions";

	/// <summary>
	/// Rate-limiting policy carrying PennMUSH's <c>http_per_second</c> quota. Named here because
	/// both the policy registration and the <c>/http/{**path}</c> route that opts into it have to
	/// agree, and so does the rejection handler that adds this policy's <c>Retry-After</c>.
	/// </summary>
	public const string SoftcodeHttpPolicy = "softcode-http";

	/// <summary>
	/// Registration is grouped by concern under <c>Registration/</c>, one extension method per band.
	/// The bands were already contiguous and non-interleaved inside the 667-line method this
	/// replaced; naming them is what lets a reader follow one without reading the rest, and lets a
	/// test assert on one.
	/// </summary>
	public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment,
		string? worldPath = null, PluginDirectories? pluginDirectories = null)
	{
		services.TryAddSingleton<IHostEnvironment>(environment);
		services.AddSharpMushHttpPipeline(configuration, environment);
		services.AddSharpMushDatabase(configuration, worldPath, pluginDirectories);
		services.AddSharpMushEngine(configuration, natsUrl);
		services.AddSharpMushOptions(colorFile);
		services.AddSharpMushLogging(configuration);
		services.AddSharpMushMessaging(natsUrl);
		services.AddSharpMushCachingAndScheduling();
		services.AddSharpMushDataProtection();
		services.AddSharpMushAuthentication(configuration, environment);
		services.AddSharpMushWebApi(configuration);
		services.AddScoped<WikiLandingReaderResolver>();
		services.AddScoped<WikiLandingReadService>();
		services.AddSharpMushHostedServices();
		services.AddSharpMushObservability();
	}
}
