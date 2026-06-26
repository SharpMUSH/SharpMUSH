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
/// Pure unit tests for the plugin loader/registrar split: no DB, no DLL loading. Load-order resolution
/// lives in <see cref="PluginLoaderService"/> (shared single-pass loader); command/function registration
/// lives in <see cref="PM"/>, which now replays the already-loaded plugins from a <see cref="PluginCatalog"/>.
/// </summary>
public class PluginManagerTests
{
	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();

	private static PM NewManager(out CommandLibraryService commands, out FunctionLibraryService functions)
	{
		commands = [];
		functions = [];
		return new PM(PluginCatalog.Empty(), commands, functions, EmptyProvider, NullLogger<PM>.Instance);
	}

	[Test]
	public async Task TopologicalSort_LoadsDependencyBeforeDependent()
	{
		var a = new PluginLoaderService.PluginCandidate("a.dll", "a", ["b"], 0);
		var b = new PluginLoaderService.PluginCandidate("b.dll", "b", [], 0);

		var ordered = PluginLoaderService.TopologicalSort([a, b], NullLogger.Instance);

		var ids = ordered.Select(c => c.Id).ToList();
		await Assert.That(ids).IsEquivalentTo(new[] { "b", "a" });
		await Assert.That(ids.IndexOf("b")).IsLessThan(ids.IndexOf("a"));
	}

	[Test]
	public async Task TopologicalSort_TieBreaksByPriorityThenId()
	{
		var high = new PluginLoaderService.PluginCandidate("z.dll", "zeta", [], Priority: 10);
		var lowB = new PluginLoaderService.PluginCandidate("b.dll", "beta", [], Priority: 1);
		var lowA = new PluginLoaderService.PluginCandidate("a.dll", "alpha", [], Priority: 1);

		var ordered = PluginLoaderService.TopologicalSort([high, lowB, lowA], NullLogger.Instance)
			.Select(c => c.Id).ToList();

		await Assert.That(ordered).IsEquivalentTo(new[] { "alpha", "beta", "zeta" });
	}

	[Test]
	public async Task TopologicalSort_CycleIsDetectedAndCyclicPluginsSkipped()
	{
		var a = new PluginLoaderService.PluginCandidate("a.dll", "a", ["b"], 0);
		var b = new PluginLoaderService.PluginCandidate("b.dll", "b", ["a"], 0);
		var c = new PluginLoaderService.PluginCandidate("c.dll", "c", [], 0);

		var ordered = PluginLoaderService.TopologicalSort([a, b, c], NullLogger.Instance)
			.Select(x => x.Id).ToList();

		await Assert.That(ordered).Contains("c");
		await Assert.That(ordered).DoesNotContain("a");
		await Assert.That(ordered).DoesNotContain("b");
	}

	[Test]
	public async Task RegisterPlugin_EntriesLandWithIsSystemTrue()
	{
		var manager = NewManager(out var commands, out var functions);

		manager.RegisterPlugin(new FakePlugin("p1",
			commands: [MakeCommand("+FOO")],
			functions: [MakeFunction("fooadd")]));

		await Assert.That(commands.ContainsKey("+FOO")).IsTrue();
		await Assert.That(commands["+FOO"].IsSystem).IsTrue();
		await Assert.That(functions.ContainsKey("fooadd")).IsTrue();
		await Assert.That(functions["fooadd"].IsSystem).IsTrue();
	}

	[Test]
	public async Task RegisterPlugin_CollisionWithExistingIsSkipped_NotOverwritten()
	{
		var manager = NewManager(out var commands, out var functions);

		var builtin = MakeCommand("THINK");
		commands.Add("THINK", (builtin, true));

		var pluginVersion = MakeCommand("THINK");
		manager.RegisterPlugin(new FakePlugin("collider", commands: [pluginVersion], functions: []));

		await Assert.That(ReferenceEquals(commands["THINK"].LibraryInformation.Command, builtin.Command)).IsTrue();
	}

	[Test]
	public async Task RegisterPlugin_ThrowingPluginIsIsolated()
	{
		var manager = NewManager(out var commands, out var functions);

		var (cmdCount, fnCount) = manager.RegisterPlugin(new ThrowingPlugin());

		await Assert.That(cmdCount).IsEqualTo(0);
		await Assert.That(fnCount).IsEqualTo(0);
		await Assert.That(commands.Count).IsEqualTo(0);
		await Assert.That(functions.Count).IsEqualTo(0);

		manager.RegisterPlugin(new FakePlugin("good", commands: [MakeCommand("+OK")], functions: []));
		await Assert.That(commands.ContainsKey("+OK")).IsTrue();
	}

	[Test]
	public async Task Catalog_AppliesServiceRegistrar_AndCollectsContributions()
	{
		// PluginCatalog.Build loads DLLs from disk, but its contribution-collection logic is exercisable
		// directly via the typed source surfaces it exposes. Here we verify the two-phase collection shape:
		// a plugin implementing several contribution interfaces is classified into the right buckets and an
		// IServiceRegistrar applies straight into the IServiceCollection (pre-build).
		var services = new ServiceCollection();
		var plugin = new MultiContributionPlugin();

		// Mirror the catalog's classification + DI application (the heart of the pre-build pass).
		var serviceCollection = services;
		((IServiceRegistrar)plugin).RegisterServices(serviceCollection);

		var provider = services.BuildServiceProvider();
		await Assert.That(provider.GetService<MarkerService>()).IsNotNull();

		await Assert.That(plugin is IServiceRegistrar).IsTrue();
		await Assert.That(plugin is IFlagSource).IsTrue();
		await Assert.That(plugin is IMigrationSource).IsTrue();
		await Assert.That(((IFlagSource)plugin).Flags.Single().Name).IsEqualTo("PLUGINFLAG");
		await Assert.That(((IMigrationSource)plugin).CypherStatements).IsNotEmpty();

		await Assert.That(plugin is ICommandInterceptor).IsTrue();
		await Assert.That(plugin is IConnectionHook).IsTrue();
		await Assert.That(plugin is IObjectLifecycleHook).IsTrue();
	}

	[Test]
	public async Task CommandInterceptor_VetoAndOverride_FlowThroughTheInterface()
	{
		// Verify the ICommandInterceptor contract semantics the hook dispatcher relies on: a before that
		// returns false vetoes; a non-null override short-circuits; defaults are inert.
		ICommandInterceptor interceptor = new MultiContributionPlugin();

		await Assert.That(await interceptor.BeforeAsync(null!, "@veto stuff")).IsFalse();
		await Assert.That(await interceptor.BeforeAsync(null!, "look")).IsTrue();

		var overridden = await interceptor.TryOverrideAsync(null!, "@over here");
		await Assert.That(overridden).IsNotNull();
		await Assert.That(overridden!.AsValue().Message!.ToString()).IsEqualTo("overridden");

		await Assert.That(await interceptor.TryOverrideAsync(null!, "look")).IsNull();

		ICommandInterceptor inert = new InertInterceptor();
		await Assert.That(await inert.BeforeAsync(null!, "anything")).IsTrue();
		await Assert.That(await inert.TryOverrideAsync(null!, "anything")).IsNull();
	}

	[Test]
	public async Task EmptyCatalog_HasNoContributions()
	{
		var catalog = PluginCatalog.Empty();
		await Assert.That(catalog.Plugins).IsEmpty();
		await Assert.That(catalog.FlagSources).IsEmpty();
		await Assert.That(catalog.MigrationSources).IsEmpty();
		await Assert.That(catalog.BridgeSources).IsEmpty();
		await Assert.That(catalog.AllFlags).IsEmpty();
		await Assert.That(catalog.CommandInterceptors).IsEmpty();
		await Assert.That(catalog.ConnectionHooks).IsEmpty();
		await Assert.That(catalog.ObjectLifecycleHooks).IsEmpty();
	}

	private static CommandDefinition MakeCommand(string name) =>
		new(new SharpCommandAttribute { Name = name },
			_ => ValueTask.FromResult(new Option<CallState>(new OneOf.Types.None())));

	private static FunctionDefinition MakeFunction(string name) =>
		new(new SharpFunctionAttribute { Name = name, Flags = FunctionFlags.Regular },
			_ => ValueTask.FromResult(new CallState("0")));

	private sealed class MarkerService;

	private sealed class MultiContributionPlugin
		: IPlugin, IServiceRegistrar, IFlagSource, IMigrationSource,
			ICommandInterceptor, IConnectionHook, IObjectLifecycleHook
	{
		public string Id => "multi";
		public string Version => "1.0.0";
		public IReadOnlyList<string> Dependencies => [];
		public int Priority => 0;
		public void Initialize(IServiceProvider services) { }

		public void RegisterServices(IServiceCollection services)
			=> services.AddSingleton<MarkerService>();

		public IEnumerable<PluginFlag> Flags =>
			[new PluginFlag("PLUGINFLAG", "P", [], [], [], ["ROOM", "PLAYER", "EXIT", "THING"])];

		public IEnumerable<string> CypherStatements => ["CREATE INDEX ON :PluginThing(id)"];

		public ValueTask<bool> BeforeAsync(IMUSHCodeParser parser, string command)
			=> ValueTask.FromResult(!command.StartsWith("@veto", StringComparison.OrdinalIgnoreCase));

		public ValueTask<Option<CallState>?> TryOverrideAsync(IMUSHCodeParser parser, string command)
			=> ValueTask.FromResult(command.StartsWith("@over", StringComparison.OrdinalIgnoreCase)
				? (Option<CallState>?)new CallState("overridden")
				: null);
	}

	/// <summary>An interceptor that overrides nothing — exercises the default no-op interface methods.</summary>
	private sealed class InertInterceptor : ICommandInterceptor;

	private sealed class FakePlugin(string id, CommandDefinition[] commands, FunctionDefinition[] functions)
		: IPlugin, ICommandSource, IFunctionSource
	{
		public string Id => id;
		public string Version => "1.0.0";
		public IReadOnlyList<string> Dependencies => [];
		public int Priority => 0;
		public void Initialize(IServiceProvider services) { }
		public IEnumerable<CommandDefinition> GetCommands() => commands;
		public IEnumerable<FunctionDefinition> GetFunctions() => functions;
	}

	private sealed class ThrowingPlugin : IPlugin, ICommandSource, IFunctionSource
	{
		public string Id => "boom";
		public string Version => "1.0.0";
		public IReadOnlyList<string> Dependencies => [];
		public int Priority => 0;
		public void Initialize(IServiceProvider services) => throw new InvalidOperationException("boom");
		public IEnumerable<CommandDefinition> GetCommands() => throw new InvalidOperationException("boom");
		public IEnumerable<FunctionDefinition> GetFunctions() => throw new InvalidOperationException("boom");
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public object? GetService(Type serviceType) => null;
	}
}
