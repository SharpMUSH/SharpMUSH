using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Loads C# plugin DLLs from the <c>plugins/</c> directory at boot, after the engine command/function
/// libraries are available. Each plugin's <c>[SharpCommand]</c>/<c>[SharpFunction]</c> contributions are
/// registered into the live libraries with <c>IsSystem=true</c> so they enter the command trie and
/// resolve like built-ins. Plugin failures are isolated by <see cref="IPluginManager"/> and never abort boot.
/// </summary>
public sealed class PluginBootstrapService(
	IPluginManager pluginManager,
	ILogger<PluginBootstrapService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			await pluginManager.LoadAllAsync(cancellationToken);
		}
		catch (Exception ex)
		{
			// Defensive: LoadAllAsync isolates per-plugin failures, but never let plugin loading kill boot.
			logger.LogError(ex, "Plugin loading failed; continuing server startup without plugins.");
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
