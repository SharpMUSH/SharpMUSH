using Core.Arango;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database;
using Serilog;
using SharpMUSH.Library.Services;
using Microsoft.AspNetCore.Identity;

namespace SharpMUSH.Server
{
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
				logging.SetMinimumLevel(LogLevel.Debug);
			});

			services.AddArango((x) => config.ConnectionString);
			services.AddSingleton<ISharpDatabase, ArangoDatabase>();
			services.AddSingleton<PasswordHasher<string>, PasswordHasher<string>>(_ => new PasswordHasher<string>());
			services.AddSingleton<IPasswordService, PasswordService>();
			services.AddSingleton<IPermissionService, PermissionService>();
			services.AddSingleton<INotifyService, NotifyService>();
			services.AddSingleton<IQueueService, QueueService>();
			services.AddSingleton<IConnectionService, ConnectionService>();
			services.AddSingleton(new ArangoHandle("CurrentSharpMUSHWorld"));
			services.AddMediatR(cfg => {
				cfg.RegisterServicesFromAssemblyContaining<Implementation.Parser>();
				cfg.RegisterServicesFromAssemblyContaining<Startup>();
			});
			services.BuildServiceProvider();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public static void Configure(IApplicationBuilder app, IWebHostEnvironment _) => app.Run(async _ => await Task.CompletedTask);
	}
}