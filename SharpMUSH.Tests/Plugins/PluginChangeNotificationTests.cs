using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using PM = SharpMUSH.Implementation.Services.PluginManager;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Proves the "force a browser refresh on plugin unload" chain at the manager seam: a successful
/// <see cref="PM.UnloadAsync"/> / <see cref="PM.ReloadAsync"/> fires the generic
/// <see cref="IPluginChangeNotifier"/> (which the Server implements by broadcasting <c>ReceivePluginsChanged</c>
/// to every connected client). A refused unload (unknown / load-once) does NOT fire it, and a load of a new
/// plugin does not either.
/// </summary>
[NotInParallel]
public class PluginChangeNotificationTests
{
	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();

	private static string CommandOnlyDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", "CommandOnlyPlugin.dll");

	private sealed class CountingNotifier : IPluginChangeNotifier
	{
		public int Count { get; private set; }

		public Task NotifyPluginsChangedAsync()
		{
			Count++;
			return Task.CompletedTask;
		}
	}

	private static PM NewManager(CountingNotifier notifier,
		out CommandLibraryService commands, out FunctionLibraryService functions)
	{
		commands = [];
		functions = [];
		return new PM(PluginCatalog.Empty(), commands, functions, EmptyProvider, NullLogger<PM>.Instance, notifier);
	}

	[Test]
	public async Task Unload_Success_FiresPluginsChangedExactlyOnce()
	{
		await Assert.That(File.Exists(CommandOnlyDllPath)).IsTrue();

		var notifier = new CountingNotifier();
		var manager = NewManager(notifier, out _, out _);

		var loaded = PluginLoaderService.LoadOne(CommandOnlyDllPath, NullLogger.Instance)!;
		manager.RegisterPlugin(loaded.Plugin);

		// Registering (a "load") must NOT notify — only an unload forces a refresh.
		await Assert.That(notifier.Count).IsEqualTo(0).Because("loading a plugin must not force a reload");

		var result = await manager.UnloadAsync(loaded.Plugin.Id);
		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(notifier.Count).IsEqualTo(1).Because("a successful unload fires the generic plugins-changed signal");
	}

	[Test]
	public async Task Reload_Success_FiresPluginsChanged()
	{
		await Assert.That(File.Exists(CommandOnlyDllPath)).IsTrue();

		var notifier = new CountingNotifier();
		var manager = NewManager(notifier, out _, out _);

		var loaded = PluginLoaderService.LoadOne(CommandOnlyDllPath, NullLogger.Instance)!;
		manager.RegisterPlugin(loaded.Plugin);

		var result = await manager.ReloadAsync("command-only");
		await Assert.That(result.IsT0).IsTrue();
		await Assert.That(notifier.Count).IsGreaterThanOrEqualTo(1).Because("a reload swaps the running DLL and forces a refresh");
	}

	[Test]
	public async Task Unload_UnknownPlugin_DoesNotFire()
	{
		var notifier = new CountingNotifier();
		var manager = NewManager(notifier, out _, out _);

		var result = await manager.UnloadAsync("does-not-exist");
		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(notifier.Count).IsEqualTo(0).Because("a refused unload must not signal a change");
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}
}
