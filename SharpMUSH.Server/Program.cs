using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Core.Arango.Serilog;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.ArangoDB;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Server.ProtocolHandlers;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main(params string[] args)
	{
		var builder = WebApplication.CreateBuilder(args);

		var container = new ArangoDbBuilder()
			// .WithReuse(true)
			.WithLabel("reuse-id", "SharpMUSH")
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await container.StartAsync();

		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};

		builder.Logging.AddSerilog(new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.WriteTo.Sink(new PeriodicBatchingSink(
				new ArangoSerilogSink(new ArangoContext(config),
					"logs",
					"logs",
					ArangoSerilogSink.LoggingRenderStrategy.StoreTemplate,
					true,
					true,
					true),
				new PeriodicBatchingSinkOptions
				{
					BatchSizeLimit = 1000,
					QueueLimit = 100000,
					Period = TimeSpan.FromSeconds(2),
					EagerlyEmitFirstEvent = true,
				}
			))
			.MinimumLevel.Debug()
			.CreateLogger());

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");

		if (!File.Exists(configFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {configFile}");
		}

		builder.WebHost.ConfigureKestrel((_, options) =>
		{
			options.ListenAnyIP(4203, listenOptions => { listenOptions.UseConnectionHandler<TelnetServer>(); });
			options.ListenAnyIP(5117);
			options.ListenAnyIP(7296, o => o.UseHttps());
		});

		ConfigureServices(builder.Services, config, configFile);

		var app = builder.Build();

		await ConfigureApp(app).RunAsync();
	}

	private static WebApplication ConfigureApp(WebApplication app)
	{
		app.UseDefaultFiles();
		app.MapStaticAssets();
		app.UseHttpsRedirection();
		app.UseAuthorization();
		app.MapControllers();
		app.MapFallbackToFile("/index.html");

		return app;
	}

	private static void ConfigureServices(IServiceCollection services, ArangoConfiguration config, string configFile)
	{
		services.AddLogging(logging =>
		{
			logging.ClearProviders();
			logging.AddSerilog(new LoggerConfiguration()
				.MinimumLevel.Override("ZiggyCreatures.Caching.Fusion", LogEventLevel.Verbose)
				.MinimumLevel.Debug()
				.CreateLogger());
		});

		services.AddSingleton<ISharpDatabase, ArangoDatabase>();
		services.AddSingleton<PasswordHasher<string>, PasswordHasher<string>>(_ => new PasswordHasher<string>()
			/*
			 * TODO: PasswordHasher may not be compatible with PennMUSH Passwords.
			 * PBKDF2 with HMAC-SHA512, 128-bit salt, 256-bit subkey, 100000 iterations.
			 *
			 * PennMUSH uses SHA1 in password_hash.
			 * It is stored as: V:ALGO:HASH:TIMESTAMP
			 *
			 * V is the version number (Currently 2), ALGO is the digest algorithm
			 * used (Default is SHA1), HASH is the hashed password. TIMESTAMP is
			 * when it was set. If fields are added, the version gets bumped.
			 *
			 * HASH is salted; the first two characters of the hashed password are
			 * randomly chosen characters that are added to the start of the
			 * plaintext password before it's hashed. This way two characters with
			 * the same password will have different hashed ones.
			 *
			 * Salt: abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789
			 * [0-61][0-61]
			 *
			 * This is not considered secure enough for SharpMUSH purposes, so should
			 * be reset after import. But this is not something that can be done in an
			 * automated way, so SHA1 and 512 should both be supported for Check_Password.
			 * Only 512 should be used for Set.
			 */
		);
		services.AddSingleton<IPasswordService, PasswordService>();
		services.AddSingleton<IPermissionService, PermissionService>();
		services.AddSingleton<INotifyService, NotifyService>();
		services.AddSingleton<ILocateService, LocateService>();
		services.AddSingleton<IExpandedObjectDataService, ExpandedObjectDataService>();
		services.AddSingleton<IAttributeService, AttributeService>();
		services.AddSingleton<ITaskScheduler, Library.Services.TaskScheduler>();
		services.AddSingleton<IConnectionService, ConnectionService>();
		services.AddSingleton<ILockService, LockService>();
		services.AddSingleton<IBooleanExpressionParser, BooleanExpressionParser>();
		services.AddSingleton<ICommandDiscoveryService, CommandDiscoveryService>();
		services.AddSingleton<IOptionsFactory<PennMUSHOptions>, ReadPennMushConfig>(_ => new ReadPennMushConfig(configFile));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(QueryCachingBehavior<,>));
		services.AddSingleton(new ArangoHandle("CurrentSharpMUSHWorld"));
		services.AddSingleton<IMUSHCodeParser, MUSHCodeParser>();
		services.AddOptions<PennMUSHOptions>();
		services.AddMediator();
		services.AddFusionCache();
		services.AddArango(_ => config.ConnectionString);
		services.AddQuartz(x =>  x.UseInMemoryStore() );
		services.AddQuartzHostedService();
		services.AddControllers();
	}
}