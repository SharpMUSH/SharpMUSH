using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Loads C# plugin DLLs from the <c>plugins/</c> directory at boot, after the engine command/function
/// libraries are available. Each plugin's <c>[SharpCommand]</c>/<c>[SharpFunction]</c> contributions are
/// registered into the live libraries with <c>IsSystem=true</c> so they enter the command trie and
/// resolve like built-ins. Plugin failures are isolated by <see cref="IPluginManager"/> and never abort boot.
///
/// <para>Phase 2b: this is also where plugin <see cref="IConnectionHook"/>s are wired. Each cataloged
/// connection hook is registered as an <c>IConnectionService.ListenState</c> listener, so it rides the
/// engine's existing connection-state mechanism (the same one fired on Register/Bind/Disconnect). Command
/// and object-lifecycle hooks are consulted at their own seams via <see cref="IPluginHookDispatcher"/>.</para>
/// </summary>
public sealed class PluginBootstrapService(
	IPluginManager pluginManager,
	PluginCatalog catalog,
	IConnectionService connectionService,
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

		WireConnectionHooks();
	}

	/// <summary>
	/// Register every cataloged <see cref="IConnectionHook"/> as a connection-state listener. The
	/// <c>ListenState</c> callback is synchronous, so each transition dispatches the async hook on a
	/// fire-and-forget task (isolated in try/catch) — mirroring how <c>ConnectionService</c> already runs its
	/// sync handlers next to async publishes. A no-hook catalog adds no listeners, so connection flow is
	/// unchanged.
	/// </summary>
	private void WireConnectionHooks()
	{
		if (catalog.ConnectionHooks.Count == 0)
		{
			return;
		}

		connectionService.ListenState(transition =>
		{
			var (handle, reference, _, newState) = transition;
			foreach (var hook in catalog.ConnectionHooks)
			{
				_ = DispatchConnectionHookAsync(hook, handle, reference, newState);
			}
		});

		logger.LogInformation("Wired {Count} plugin connection hook(s) as connection-state listeners.",
			catalog.ConnectionHooks.Count);
	}

	private async Task DispatchConnectionHookAsync(
		IConnectionHook hook,
		long handle,
		SharpMUSH.Library.Models.DBRef? reference,
		IConnectionService.ConnectionState newState)
	{
		try
		{
			switch (newState)
			{
				case IConnectionService.ConnectionState.Connected:
					await hook.OnConnectAsync(handle);
					break;
				case IConnectionService.ConnectionState.LoggedIn when reference is { } player:
					await hook.OnLoginAsync(handle, player);
					break;
				case IConnectionService.ConnectionState.Disconnected:
					await hook.OnDisconnectAsync(handle, reference);
					break;
			}
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Connection hook {Hook} threw handling state {State} for handle {Handle}; ignoring.",
				hook.GetType().FullName, newState, handle);
		}
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
