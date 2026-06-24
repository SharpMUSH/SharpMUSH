using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services;
using PM = SharpMUSH.Implementation.Services.PluginManager;

namespace SharpMUSH.Tests.Plugins;

/// <summary>
/// Phase 3 unload/reload tests. These exercise the real collectible-ALC machinery: the command-only fixture
/// plugin DLL (built and copied into <c>plugins-unit/command-only/</c>) is loaded from disk through
/// <see cref="PluginLoaderService"/>, registered into live libraries, then unloaded — and we prove the
/// underlying <c>AssemblyLoadContext</c> is actually reclaimed by the GC (dead WeakReference). No DB, no
/// server host: a pure loader/registrar test.
/// </summary>
[NotInParallel]
public class PluginUnloadTests
{
	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();

	private static string CommandOnlyDllPath =>
		Path.Combine(AppContext.BaseDirectory, "plugins-unit", "command-only", "CommandOnlyPlugin.dll");

	private static PM NewManager(out CommandLibraryService commands, out FunctionLibraryService functions)
	{
		commands = [];
		functions = [];
		return new PM(PluginCatalog.Empty(), commands, functions, EmptyProvider, NullLogger<PM>.Instance);
	}

	[Test]
	public async Task CommandOnlyPlugin_IsClassifiedUnloadable()
	{
		await Assert.That(File.Exists(CommandOnlyDllPath))
			.IsTrue()
			.Because($"the CommandOnlyPlugin fixture DLL must be copied to {CommandOnlyDllPath}");

		var loaded = PluginLoaderService.LoadOne(CommandOnlyDllPath, NullLogger.Instance);
		await Assert.That(loaded).IsNotNull();
		await Assert.That(loaded!.IsUnloadable).IsTrue();
		await Assert.That(PluginLoaderService.IsUnloadablePlugin(loaded.Plugin)).IsTrue();

		loaded.Loader.Dispose();
	}

	/// <summary>
	/// THE canonical unload proof. Load a command-only plugin, register it, capture a WeakReference to its
	/// PluginLoader/ALC, then UnloadAsync and force a bounded GC loop. After unload the WeakReference must be
	/// dead (the collectible ALC really unloaded) and the plugin's command must no longer resolve.
	/// </summary>
	[Test]
	public async Task UnloadAsync_CommandOnlyPlugin_CollectibleContextIsReclaimed()
	{
		await Assert.That(File.Exists(CommandOnlyDllPath))
			.IsTrue()
			.Because($"the CommandOnlyPlugin fixture DLL must be copied to {CommandOnlyDllPath}");

		var manager = NewManager(out var commands, out var functions);

		var (loaderRef, pluginId) = LoadRegisterAndWeaklyReference(manager, commands, functions);

		await Assert.That(commands.ContainsKey("+UNLOADME")).IsTrue();
		await Assert.That(functions.ContainsKey("unloadme")).IsTrue();
		await Assert.That(loaderRef.IsAlive).IsTrue();

		var result = await manager.UnloadAsync(pluginId);
		await Assert.That(result.IsT0).IsTrue().Because("an unloadable plugin must unload successfully");

		await Assert.That(commands.ContainsKey("+UNLOADME")).IsFalse();
		await Assert.That(functions.ContainsKey("unloadme")).IsFalse();

		await Assert.That(WaitForCollected(loaderRef))
			.IsTrue()
			.Because("the collectible AssemblyLoadContext must be reclaimed after UnloadAsync");
	}

	[Test]
	public async Task ReloadAsync_CommandOnlyPlugin_ReRegistersFromDisk()
	{
		await Assert.That(File.Exists(CommandOnlyDllPath)).IsTrue();

		var manager = NewManager(out var commands, out var functions);

		var loaded = PluginLoaderService.LoadOne(CommandOnlyDllPath, NullLogger.Instance);
		manager.RegisterPlugin(loaded!.Plugin);
		await Assert.That(commands.ContainsKey("+UNLOADME")).IsTrue();

		var result = await manager.ReloadAsync("command-only");
		await Assert.That(result.IsT0).IsTrue();

		await Assert.That(commands.ContainsKey("+UNLOADME")).IsTrue();
		await Assert.That(functions.ContainsKey("unloadme")).IsTrue();
	}

	[Test]
	public async Task ReloadAsync_LoadOncePlugin_RefusedWithError()
	{
		var manager = NewManager(out _, out _);

		var plugin = new LoadOnceFlagPlugin();
		await Assert.That(PluginLoaderService.IsUnloadablePlugin(plugin)).IsFalse();
		manager.RegisterPlugin(plugin);

		var reload = await manager.ReloadAsync("load-once");
		await Assert.That(reload.IsT1).IsTrue().Because("a load-once plugin must refuse reload");
		await Assert.That(reload.AsT1.Value).Contains("load-once");

		var unload = await manager.UnloadAsync("load-once");
		await Assert.That(unload.IsT1).IsTrue().Because("a load-once plugin must refuse unload");
	}

	[Test]
	public async Task UnloadAsync_UnknownPlugin_ReturnsError()
	{
		var manager = NewManager(out _, out _);
		var result = await manager.UnloadAsync("does-not-exist");
		await Assert.That(result.IsT1).IsTrue();
		await Assert.That(result.AsT1.Value).Contains("not loaded");
	}

	/// <summary>
	/// Load the command-only DLL, register it, and return only a WeakReference to its loader plus the plugin
	/// id. No-inlining guarantees the JIT cannot keep the (now out-of-scope) plugin/loader locals rooted on
	/// the stack past the call, which would otherwise defeat the unload proof.
	/// </summary>
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (WeakReference LoaderRef, string PluginId) LoadRegisterAndWeaklyReference(
		PM manager, CommandLibraryService commands, FunctionLibraryService functions)
	{
		var loaded = PluginLoaderService.LoadOne(CommandOnlyDllPath, NullLogger.Instance)!;
		manager.RegisterPlugin(loaded.Plugin);
		return (new WeakReference(loaded.Loader), loaded.Plugin.Id);
	}

	private static bool WaitForCollected(WeakReference reference)
	{
		for (var i = 0; i < 20 && reference.IsAlive; i++)
		{
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();
		}

		return !reference.IsAlive;
	}

	/// <summary>A load-once plugin (contributes a flag) used to prove unload/reload is refused for it.</summary>
	private sealed class LoadOnceFlagPlugin : IPlugin, ICommandSource, IFlagSource
	{
		public string Id => "load-once";
		public string Version => "1.0.0";
		public IReadOnlyList<string> Dependencies => [];
		public int Priority => 0;
		public void Initialize(IServiceProvider services) { }

		public IEnumerable<CommandDefinition> GetCommands() =>
			[new CommandDefinition(new SharpCommandAttribute { Name = "+LOADONCE" },
				_ => ValueTask.FromResult(new Option<CallState>(new OneOf.Types.None())))];

		public IEnumerable<PluginFlag> Flags =>
			[new PluginFlag("LOADONCE_FLAG", "L", [], [], [], ["ROOM", "PLAYER", "EXIT", "THING"])];
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}
}
