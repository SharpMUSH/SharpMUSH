using Core.Arango;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using SharpMUSH.Database.ArangoDB;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Behaviors;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using TaskScheduler = SharpMUSH.Library.Services.TaskScheduler;

namespace SharpMUSH.Server;

public class Startup(ArangoConfiguration config)
{
	// This method gets called by the runtime. Use this method to add services to the container.
	// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
	public void ConfigureServices(IServiceCollection services)
	{
		services.AddLogging(logging =>
		{
			logging.ClearProviders();
			logging.AddSerilog();
			logging.SetMinimumLevel(LogLevel.Trace);
		});

		services.AddArango(_ => config.ConnectionString);
		services.AddSingleton<ISharpDatabase, ArangoDatabase>();
		services.AddSingleton<PasswordHasher<string>, PasswordHasher<string>>(
			_ => new PasswordHasher<string>()
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
		services.AddMemoryCache();
		services.AddSingleton<IPasswordService, PasswordService>();
		services.AddSingleton<IPermissionService, PermissionService>();
		services.AddSingleton<INotifyService, NotifyService>();
		services.AddSingleton<ILocateService, LocateService>();
		services.AddSingleton<IAttributeService, AttributeService>();
		services.AddSingleton<ITaskScheduler, TaskScheduler>();
		services.AddSingleton<IConnectionService, ConnectionService>();
		services.AddSingleton<ILockService, LockService>();
		services.AddSingleton<IBooleanExpressionParser, BooleanExpressionParser>();
		services.AddSingleton<ICommandDiscoveryService, CommandDiscoveryService>();
		services.AddSingleton(new ArangoHandle("CurrentSharpMUSHWorld"));
		services.AddScoped<IMUSHCodeParser, MUSHCodeParser>();
		services.AddHostedService<SchedulerService>();
		services.AddMediator();
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehavior<,>));
		services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(QueryCachingBehavior<,>));
		services.BuildServiceProvider();
	}

	// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
	public static void Configure(IApplicationBuilder app, IWebHostEnvironment _) =>
		app.Run(async _ => await Task.CompletedTask);
}