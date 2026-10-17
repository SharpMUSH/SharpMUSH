using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Secrets;
using SharpMUSH.Library.Services.DatabaseConversion;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Server.Services.Imports;

/// <summary>
/// A worker-only host graph: every store, mediator handler, cache and option factory is independently
/// constructed. It never starts hosted services or publishes into the live world's transports.
/// Workers run in their own process, keeping plugin/runtime/Quartz static state outside the live host.
/// </summary>
public sealed class IsolatedImportHostFactory
{
	public async Task<IHost> CreateAsync(ImportSession session, CancellationToken cancellationToken = default)
	{
		ImportPromotionJournal.PrivateDirectory(session.Directory);
		ImportPromotionJournal.PrivateDirectory(session.KeyRingPath);
		var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
		{
			EnvironmentName = Environments.Production,
			ContentRootPath = AppContext.BaseDirectory,
			ApplicationName = typeof(Program).Assembly.GetName().Name,
			DisableDefaults = true
		});
		builder.Services.AddLogging();
		builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
		new Startup(Path.Combine(AppContext.BaseDirectory, "colors.json"), "nats://127.0.0.1:1")
			.ConfigureServices(builder.Services, builder.Configuration, builder.Environment, session.WorldPath,
				new PluginDirectories(PluginDirectories.DefaultBuiltIn, Path.Combine(session.Directory, "plugins")));
		builder.Services.RemoveAll<IHostedService>();
		builder.Services.RemoveAll<IConnectionStateStore>();
		builder.Services.RemoveAll<IMessageBus>();
		builder.Services.AddSingleton<IMessageBus, ImportMessageBus>();

		// The normal host's environment/file secret overrides and key-ring path must not leak into preview.
		builder.Services.AddSingleton(new SecretStoreOptions(Path.Combine(session.Directory, "secret-overrides"), _ => null));
		builder.Services.AddSingleton<IDataProtectionProvider>(DataProtectionProvider.Create(new DirectoryInfo(session.KeyRingPath),
			protection => protection.SetApplicationName(Registration.DataProtectionRegistration.ApplicationName)));
		builder.Services.AddSingleton<PennMUSHDatabaseConverter>();
		var host = builder.Build();
		try
		{
			await host.Services.GetRequiredService<IDatabaseLifecycle>().Migrate(cancellationToken);
			return host;
		}
		catch
		{
			if (host is IAsyncDisposable disposable) await disposable.DisposeAsync();
			else host.Dispose();
			throw;
		}
	}

	private sealed class ImportMessageBus : IMessageBus
	{
		public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.CompletedTask;
		}
		public Task HandlePublish<T>(T message, CancellationToken cancellationToken = default) where T : IHandleMessage
		{
			cancellationToken.ThrowIfCancellationRequested();
			return Task.CompletedTask;
		}
	}
}
