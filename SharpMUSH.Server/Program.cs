﻿using Core.Arango;
using Core.Arango.Serialization.Newtonsoft;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using SharpMUSH.Server.ProtocolHandlers;
using Testcontainers.ArangoDb;

namespace SharpMUSH.Server;

public class Program
{
	public static async Task Main()
	{
		new LoggerConfiguration()
			.Enrich.FromLogContext()
			.WriteTo.Console(theme: AnsiConsoleTheme.Code)
			.MinimumLevel.Debug()
			.CreateLogger();

		var container = new ArangoDbBuilder()
			// .WithReuse(true)
			.WithLabel("reuse-id", "SharpMUSH")
			.WithImage("arangodb:latest")
			.WithPassword("password")
			.Build();

		await container.StartAsync()
			.ConfigureAwait(false);

		var config = new ArangoConfiguration
		{
			ConnectionString = $"Server={container.GetTransportAddress()};User=root;Realm=;Password=password;",
			Serializer = new ArangoNewtonsoftSerializer(new ArangoNewtonsoftDefaultContractResolver())
		};

		var configFile = Path.Combine(AppContext.BaseDirectory, "mushcnf.dst");
		
		if (!File.Exists(configFile))
		{
			throw new FileNotFoundException($"Configuration file not found: {configFile}");
		}

		await CreateWebHostBuilder(config, configFile).Build().RunAsync();
	}

	public static IWebHostBuilder CreateWebHostBuilder(ArangoConfiguration arangoConfig, string configFile) =>
		WebHost
			.CreateDefaultBuilder()
			.UseStartup(_ => new Startup(arangoConfig, configFile))
			.UseKestrel(options =>
				options.ListenLocalhost(
					4202,
					builder => builder.UseConnectionHandler<TelnetServer>()
				)
			);
}