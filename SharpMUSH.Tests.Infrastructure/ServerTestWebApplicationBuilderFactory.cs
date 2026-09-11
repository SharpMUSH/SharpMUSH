using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quartz;
using Serilog;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using TUnit.AspNetCore;
using ZiggyCreatures.Caching.Fusion;

namespace SharpMUSH.Tests;

public class ServerTestWebApplicationBuilderFactory<TProgram>(
	string sqlConnectionString,
	string configFile,
	INotifyService? notifier,
	string sqlPlatform = "mysql",
	IServiceProvider? sharedWorldServices = null) :
	TestWebApplicationFactory<TProgram> where TProgram : class
{
	private readonly string _schedulerName = $"sharpmush-tests-{Guid.NewGuid():N}";

	/// <summary>
	/// Lifecycle Step 4: Runs BEFORE Program.cs startup.
	/// Use this for configuration that Program.cs needs during its initialization.
	/// </summary>
	protected override void ConfigureStartupConfiguration(IConfigurationBuilder configurationBuilder)
	{
		// Map the in-server MCP endpoint in integration tests regardless of which appsettings
		// the test host resolves, so the Explicit MCP tests always have an endpoint to hit.
		configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Mcp:Enabled"] = "true"
		});
	}

	/// <summary>
	/// Lifecycle Step 3: Shared configuration for all tests (runs once per test session).
	/// Use ConfigureServices for startup configuration and ConfigureTestServices for final overrides.
	/// </summary>
	protected override void ConfigureWebHost(IWebHostBuilder builder)
	{
		// Concurrent integration tests hammer the auth-gated endpoints far past the
		// production "public-api" rate limit (30 req/min), which flakes full runs with 429s.
		// Raise the limits so the real pipeline's rate limiter never throttles test traffic.
		// NOTE: UseSetting (not ConfigureStartupConfiguration) is required here — for
		// minimal-hosting apps (WebApplication.CreateBuilder) the factory's startup
		// configuration hook is never applied; UseSetting flows into host configuration,
		// which WebApplicationFactory forwards to Program.Main as command-line arguments.
		builder.UseSetting("RateLimiting:PublicApi:PermitLimit", "100000");
		builder.UseSetting("RateLimiting:PublicApi:WindowSeconds", "60");
		builder.UseSetting("RateLimiting:PublicApi:QueueLimit", "100000");

		Log.Logger = TestDiagnostics.CreateLogger();
		TestDiagnostics.ConfigureHost(builder);

		if (sharedWorldServices is not null)
		{
			builder.ConfigureTestServices(services =>
			{
				// Opening a second LMDB environment on one path in the same process is unsafe.
				// Reuse every registered provider contract as an externally owned instance, so
				// secondary-host disposal cannot close the primary host's database through an alias.
				var database = sharedWorldServices.GetRequiredService<ISharpDatabase>();
				var providerTypes = database.GetType().GetInterfaces().Append(database.GetType())
					.Where(type => type != typeof(IApplicationRegistryService)
						&& services.Any(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == type))
					.ToArray();
				foreach (var type in providerTypes)
				{
					services.RemoveAll(type);
					services.AddSingleton(type, database);
				}
				// Keep the application-registry decorator host-local, wrapping the shared provider.
				// All Mediator readers/writers must use one cache and invalidation version ledger.
				services.RemoveAll<IFusionCache>();
				services.AddSingleton(sharedWorldServices.GetRequiredService<IFusionCache>());
				services.RemoveAll<ObjectVersions>();
				services.AddSingleton(sharedWorldServices.GetRequiredService<ObjectVersions>());
			});
		}

		var colorFile = Path.Combine(AppContext.BaseDirectory, "colors.json");
		if (!File.Exists(colorFile))
		{
			var tempColorFile = Path.Combine(Path.GetTempPath(), "colors.json");
			File.WriteAllText(tempColorFile, "{}");
			try
			{
				Directory.CreateDirectory(AppContext.BaseDirectory);
				File.Copy(tempColorFile, colorFile, true);
			}
			catch
			{
				// If we can't create it in the base directory, that's OK
				// The startup will handle the missing file
			}
		}

		// Configure the existing shared host before startup resolves these services.
		builder.ConfigureServices(sc =>
			{
				// Quartz registers schedulers by name process-wide. Each existing session host
				// must own its scheduler so disposing one cannot stop another host's queue.
				sc.AddQuartz(options => options.SchedulerName = _schedulerName);

				var substitute = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
				var config = ReadPennMushConfig.Create(configFile);

				var sqlOptionsMonitor = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();

				var sqlConfigOverride = config with
				{
					Net = config.Net with
					{
						SqlHost = ExtractSqlHost(sqlConnectionString),
						SqlDatabase = ExtractSqlDatabase(sqlConnectionString),
						SqlUsername = ExtractSqlUsername(sqlConnectionString),
						SqlPassword = ExtractSqlPassword(sqlConnectionString),
						SqlPlatform = sqlPlatform
					}
				};

				// Read through TestOptionsOverride so a test can scope a config change to its own
				// async flow; with no scope active this hands back `config` unchanged.
				substitute.CurrentValue.Returns(_ => TestOptionsOverride.Apply(config));
				sqlOptionsMonitor.CurrentValue.Returns(sqlConfigOverride);

				sc.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
				sc.AddSingleton(substitute);

				// Services that take IOptionsMonitor<SharpMUSHOptions> directly (PermissionService is
				// the notable one) never see the wrapper, so the scoped override is applied here too.
				sc.AddSingleton<IOptionsMonitor<SharpMUSHOptions>>(sp =>
					new TestOverridableOptionsMonitor(
						ActivatorUtilities.CreateInstance<OptionsMonitor<SharpMUSHOptions>>(sp)));

				if (notifier is not null)
				{
					sc.RemoveAll<INotifyService>();
					sc.AddSingleton(notifier);
				}

				sc.RemoveAll<ISqlService>();
				sc.AddSingleton<ISqlService>(new SqlService(sqlOptionsMonitor));
			}
		);
	}

	private static string ExtractSqlHost(string connectionString)
	{
		var parts = connectionString.Split(';');
		string? host = null;
		string? port = null;

		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Server=", StringComparison.OrdinalIgnoreCase))
				host = trimmedPart.Substring(7);
			else if (trimmedPart.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
				host = trimmedPart.Substring(5);
			else if (trimmedPart.StartsWith("Port=", StringComparison.OrdinalIgnoreCase))
				port = trimmedPart.Substring(5);
		}

		if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(port))
			return $"{host}:{port}";

		return host ?? "localhost";
	}

	private static string ExtractSqlDatabase(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
			if (trimmedPart.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(12);
		}
		return "";
	}

	private static string ExtractSqlUsername(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Uid=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(4);
			if (trimmedPart.StartsWith("User Id=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(8);
			if (trimmedPart.StartsWith("Username=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
			if (trimmedPart.StartsWith("User=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(5);
		}
		return "";
	}

	private static string ExtractSqlPassword(string connectionString)
	{
		var parts = connectionString.Split(';');
		foreach (var trimmedPart in parts.Select(part => part.Trim()))
		{
			if (trimmedPart.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(4);
			if (trimmedPart.StartsWith("Password=", StringComparison.OrdinalIgnoreCase))
				return trimmedPart.Substring(9);
		}
		return "";
	}
}
