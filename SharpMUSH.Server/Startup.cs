using Core.Arango;
using Core.Arango.Serilog;
using SharpMUSH.Messaging.Abstractions;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Quartz;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.PeriodicBatching;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database;
using SharpMUSH.Database.ArangoDB;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Commands;
using SharpMUSH.Implementation.Functions;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Extensions;
using SharpMUSH.Messaging.Kafka;
using SharpMUSH.Server.Strategy.ArangoDB;
using SharpMUSH.Server.Strategy.Prometheus;
using SharpMUSH.Server.Strategy.Redis;
using ZiggyCreatures.Caching.Fusion;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Server;

public class Startup(ArangoConfiguration arangoConfig, string colorFile, PrometheusStrategy prometheusStrategy, RedisStrategy redisStrategy)
{
	// This method gets called by the runtime. Use this method to add services to the container.
	// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
	public void ConfigureServices(IServiceCollection services)
	{
		services.AddCors(options =>
		{
			options.AddDefaultPolicy(builder => builder
				.SetIsOriginAllowed(o => true)
				// .WithOrigins("https://localhost:7102")
				.AllowAnyMethod()
				.AllowAnyHeader());
		});

		services.AddSingleton<ISharpDatabase, ArangoDatabase>(x =>
		{
			var logger = x.GetRequiredService<ILogger<ArangoDatabase>>();
			var context = x.GetRequiredService<IArangoContext>();
			var handle = x.GetRequiredService<ArangoHandle>();
			var mediator = x.GetRequiredService<IMediator>();
			var password = x.GetRequiredService<IPasswordService>();
			var db = new ArangoDatabase(logger, context, handle, mediator, password);
			db.Migrate().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
			return db;
		});
		services.AddSingleton<PasswordHasher<string>, PasswordHasher<string>>(_ => new PasswordHasher<string>()
			/*
			 * PennMUSH Password Compatibility - IMPLEMENTED
			 * 
			 * SharpMUSH uses PBKDF2 with HMAC-SHA512, 128-bit salt, 256-bit subkey, 100000 iterations
			 * for new passwords.
			 *
			 * PennMUSH uses SHA1 in password_hash, stored as: V:ALGO:HASH:TIMESTAMP
			 * - V: Version number (Currently 2)
			 * - ALGO: Digest algorithm (Default is SHA1)
			 * - HASH: Salted hash (first 2 chars are salt prepended to plaintext before hashing)
			 * - TIMESTAMP: Unix timestamp when password was set
			 *
			 * Salt characters: abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789
			 *
			 * The PasswordService now supports both formats:
			 * - Verification: Detects PennMUSH format and uses SHA1/SHA256 verification as needed
			 * - New passwords: Always use modern PBKDF2 (more secure)
			 * 
			 * Users with imported PennMUSH passwords should reset their passwords for better security,
			 * but can still log in with their old passwords until they do.
			 */
		);
		services.AddSingleton<IPasswordService, PasswordService>();
		services.AddSingleton<IPermissionService, PermissionService>();
		services.AddSingleton<ITelemetryService, TelemetryService>();
		services.AddSingleton<IPrometheusQueryService>(sp =>
		{
			var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
			var logger = sp.GetRequiredService<ILogger<PrometheusQueryService>>();
			var prometheusUrl = prometheusStrategy.GetPrometheusUrl();
			return new PrometheusQueryService(httpClient, logger, prometheusUrl);
		});
		
		// Configure Redis connection using strategy pattern
		services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
		{
			var logger = sp.GetRequiredService<ILogger<StackExchange.Redis.ConnectionMultiplexer>>();
			
			try
			{
				var multiplexer = redisStrategy.GetConnectionAsync().AsTask().GetAwaiter().GetResult();
				logger.LogInformation("Connected to Redis successfully");
				return multiplexer;
			}
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Failed to connect to Redis. Connection state will not be shared.");
				throw;
			}
		});
		
		// Add Redis-backed connection state store
		services.AddSingleton<IConnectionStateStore, RedisConnectionStateStore>();
		
		services.AddSingleton<INotifyService, NotifyService>();
		services.AddSingleton<ILocateService, LocateService>();
		services.AddSingleton<IMoveService, MoveService>();
		services.AddSingleton<IExpandedObjectDataService, ExpandedObjectDataService>();
		services.AddSingleton<IAttributeService, AttributeService>();
		services.AddSingleton<IManipulateSharpObjectService, ManipulateSharpObjectService>();
		services.AddSingleton<ITaskScheduler, TaskScheduler>();
		services.AddSingleton<IConnectionService, ConnectionService>();
		services.AddSingleton<ISqlService, SqlService>();
		services.AddSingleton<ICommunicationService, CommunicationService>();
		services.AddSingleton<ILockService, LockService>();
		services.AddSingleton<IBooleanExpressionParser, BooleanExpressionParser>();
		services.AddSingleton<ICommandDiscoveryService, CommandDiscoveryService>();
		services.AddSingleton<ISortService, SortService>();
		services.AddSingleton<IHookService, HookService>();
		services.AddSingleton<IEventService, EventService>();
		services.AddSingleton<IWarningService, WarningService>();
		services.AddSingleton<IChannelBufferService, InMemoryChannelBufferService>();
		services.AddSingleton<IListenPatternMatcher, ListenPatternMatcher>();
		services.AddSingleton<IListenerRoutingService, ListenerRoutingService>();
		services.AddSingleton<PennMUSHDatabaseParser>();
		services.AddSingleton<IPennMUSHDatabaseConverter, PennMUSHDatabaseConverter>();
		
		// Initialize TextFileService
		services.AddSingleton<ITextFileService, Implementation.Services.TextFileService>();
		
		// Register Commands and Functions as Scoped to enable per-test-class isolation
		// Each scope (test class, request, etc.) gets its own instance with isolated state
		services.AddScoped<ILibraryProvider<FunctionDefinition>, Functions>();
		services.AddScoped<ILibraryProvider<CommandDefinition>, Commands>();
		services.AddScoped(x => x.GetService<ILibraryProvider<FunctionDefinition>>()!.Get());
		services.AddScoped(x => x.GetService<ILibraryProvider<CommandDefinition>>()!.Get());
		
		services.AddSingleton<IOptionsFactory<SharpMUSHOptions>, OptionsService>();
		services.AddSingleton<IOptionsFactory<ColorsOptions>, ReadColorsOptionsFactory>();
		services.AddSingleton<ConfigurationReloadService>();
		services.AddSingleton<IOptionsChangeTokenSource<SharpMUSHOptions>>(sp => sp.GetRequiredService<ConfigurationReloadService>());
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(QueryCachingBehavior<,>));
		services.AddSingleton(new ArangoHandle("CurrentSharpMUSHWorld"));
		services.AddSingleton<IMUSHCodeParser, MUSHCodeParser>();
		services.AddSingleton<IValidateService, ValidateService>();
		services.AddKeyedSingleton(nameof(colorFile), colorFile);
		services.AddOptions<SharpMUSHOptions>().ValidateOnStart();
		services.AddScoped<IValidateOptions<SharpMUSHOptions>, Configuration.Generated.ValidateSharpMUSHOptions>();
		services.AddOptions<ColorsOptions>().ValidateOnStart();
		services.AddSingleton<IOptionsWrapper<SharpMUSHOptions>, Library.Services.OptionsWrapper<SharpMUSHOptions>>();
		services.AddSingleton<IOptionsWrapper<ColorsOptions>, Library.Services.OptionsWrapper<ColorsOptions>>();
		services.AddHttpClient();
		services.AddMediator();

		// Configure MassTransit with Kafka/RedPanda for message queue integration
		var kafkaHost = Environment.GetEnvironmentVariable("KAFKA_HOST") ?? "localhost";

		services.AddMainProcessMessaging(
			options =>
			{
				options.Host = kafkaHost;
				options.Port = 9092;
				options.MaxMessageBytes = 6 * 1024 * 1024; // 6MB
			},
			x =>
			{
				// Register consumers for input messages from ConnectionServer
				x.AddConsumer<Consumers.TelnetInputConsumer>();
				x.AddConsumer<Consumers.GMCPSignalConsumer>();
				x.AddConsumer<Consumers.MSDPUpdateConsumer>();
				x.AddConsumer<Consumers.NAWSUpdateConsumer>();
				x.AddConsumer<Consumers.ConnectionEstablishedConsumer>();
				x.AddConsumer<Consumers.ConnectionClosedConsumer>();
			});

		services.AddFusionCache().TryWithAutoSetup();
		services.AddArango((_, arango) =>
		{
			arango.ConnectionString = arangoConfig.ConnectionString;
			arango.HttpClient = arangoConfig.HttpClient;
			arango.Serializer = arangoConfig.Serializer;
		});
		services.AddQuartz(x =>
		{
			x.UseInMemoryStore();
		});
		services.AddAuthorization();
		services.AddRazorPages();
		services.AddControllers();
		services.AddQuartzHostedService();
		services.AddHostedService<StartupHandler>();
		services.AddHostedService<Services.ConnectionReconciliationService>();
		services.AddHostedService<Services.ConnectionLoggingService>();
		services.AddHostedService<Services.HealthMonitoringService>();
		services.AddHostedService<Services.WarningCheckService>();
		services.AddHostedService<Services.PennMUSHDatabaseConversionService>();

		services.AddLogging(logging =>
		{
			logging.ClearProviders();
			logging.AddSerilog(new LoggerConfiguration()
				.MinimumLevel.Debug()
				.MinimumLevel.Override("ZiggyCreatures.Caching.Fusion", LogEventLevel.Error)
				.Enrich.FromLogContext()
				.WriteTo.Sink(new PeriodicBatchingSink(
					new ArangoSerilogSink(
						logging.Services.BuildServiceProvider().GetRequiredService<IArangoContext>(),
						"CurrentSharpMUSHWorld",
						DatabaseConstants.Logs,
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
					}))
				.CreateLogger());
			;
		});

		// Configure OpenTelemetry Metrics for Prometheus
		services.AddOpenTelemetry()
			.ConfigureResource(resource => resource
				.AddService("SharpMUSH.Server", serviceVersion: "1.0.0"))
			.WithMetrics(metrics => metrics
				.AddMeter("SharpMUSH")
				.AddRuntimeInstrumentation()
				.AddConsoleExporter()
				.AddPrometheusExporter());
	}
}
