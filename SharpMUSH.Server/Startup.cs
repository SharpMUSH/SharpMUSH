using Core.Arango;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Database;
using Serilog;
using Core.Arango.Serialization.Newtonsoft;

namespace SharpMUSH.Server
{
	public class Startup
	{
		// This method gets called by the runtime. Use this method to add services to the container.
		// For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
		public static void ConfigureServices(IServiceCollection services)
		{
			services.AddLogging(logging =>
			{
				logging.ClearProviders();
				logging.AddSerilog();
				logging.SetMinimumLevel(LogLevel.Debug);
			});

			services.AddArango("Server=http://127.0.0.1:8529;User=root;Realm=;Password=KJt7fVjUGFSl9Xqn;", new ArangoConfiguration ()
			{
				Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())

			});
			services.AddSingleton<ISharpDatabase, ArangoDatabase>();
			services.BuildServiceProvider();
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public static void Configure(IApplicationBuilder app, IWebHostEnvironment _) => app.Run(async _ => await Task.CompletedTask);
	}
}