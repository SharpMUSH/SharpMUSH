using Asp.Versioning;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.CodeAnalysis;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Mcp;
using SharpMUSH.Server.Middleware;
using SharpMUSH.Server.RateLimiting;
using SharpMUSH.Server.Registration;
using SharpMUSH.Server.Services;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using System.Globalization;
using System.Threading.RateLimiting;
using OpenTelemetry.ResourceDetectors.Container;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using SharpMUSH.Messaging.NATS;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;

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
	public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
	{
		services.AddSharpMushHttpPipeline(configuration, environment);
		services.AddSharpMushDatabase(configuration);
		services.AddSharpMushEngine(configuration, natsUrl);
		services.AddSharpMushOptions(colorFile);
		services.AddSharpMushLogging(configuration);
		services.AddSharpMushMessaging(natsUrl);
		services.AddSharpMushCachingAndScheduling();
		services.AddSharpMushAuthentication(configuration, environment);
		services.AddSharpMushWebApi(configuration);
		services.AddSharpMushHostedServices();
		services.AddSharpMushObservability();
	}
}
