using Core.Arango;
using Core.Arango.Serilog;
using MassTransit;
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
		services.AddSingleton<PennMUSHDatabaseParser>();
		services.AddSingleton<IPennMUSHDatabaseConverter, PennMUSHDatabaseConverter>();
		services.AddSingleton<ILibraryProvider<FunctionDefinition>, Functions>();
		services.AddSingleton<ILibraryProvider<CommandDefinition>, Commands>();
		services.AddSingleton(x => x.GetService<ILibraryProvider<FunctionDefinition>>()!.Get());
		services.AddSingleton(x => x.GetService<ILibraryProvider<CommandDefinition>>()!.Get());
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