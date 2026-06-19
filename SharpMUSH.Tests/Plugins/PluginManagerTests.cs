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
/// Pure unit tests for <see cref="SharpMUSH.Implementation.Services.PluginManager"/>: no DB, no DLL loading.
/// Exercises load-order resolution and the registration policy directly via the public seams.
/// </summary>
public class PluginManagerTests
{
	private static readonly IServiceProvider EmptyProvider = new EmptyServiceProvider();

	private static PM NewManager(out CommandLibraryService commands, out FunctionLibraryService functions)
	{
		commands = [];
		functions = [];
		return new PM(commands, functions, EmptyProvider, NullLogger<PM>.Instance);
	}

	[Test]
	public async Task TopologicalSort_LoadsDependencyBeforeDependent()
	{
		var manager = NewManager(out _, out _);

		// "a" depends on "b" → b must come before a.
		var a = new PM.PluginCandidate("a.dll", "a", ["b"], 0);
		var b = new PM.PluginCandidate("b.dll", "b", [], 0);

		var ordered = manager.TopologicalSort([a, b]);

		var ids = ordered.Select(c => c.Id).ToList();
		await Assert.That(ids).IsEquivalentTo(new[] { "b", "a" });
		await Assert.That(ids.IndexOf("b")).IsLessThan(ids.IndexOf("a"));
	}

	[Test]
	public async Task TopologicalSort_TieBreaksByPriorityThenId()
	{
		var manager = NewManager(out _, out _);

		var high = new PM.PluginCandidate("z.dll", "zeta", [], Priority: 10);
		var lowB = new PM.PluginCandidate("b.dll", "beta", [], Priority: 1);
		var lowA = new PM.PluginCandidate("a.dll", "alpha", [], Priority: 1);

		var ordered = manager.TopologicalSort([high, lowB, lowA]).Select(c => c.Id).ToList();

		// Lower priority first; equal priority breaks by id alphabetically; highest priority last.
		await Assert.That(ordered).IsEquivalentTo(new[] { "alpha", "beta", "zeta" });
	}

	[Test]
	public async Task TopologicalSort_CycleIsDetectedAndCyclicPluginsSkipped()
	{
		var manager = NewManager(out _, out _);

		// a -> b -> a is a cycle; an independent "c" must still survive.
		var a = new PM.PluginCandidate("a.dll", "a", ["b"], 0);
		var b = new PM.PluginCandidate("b.dll", "b", ["a"], 0);
		var c = new PM.PluginCandidate("c.dll", "c", [], 0);

		var ordered = manager.TopologicalSort([a, b, c]).Select(x => x.Id).ToList();

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

		// Simulate an engine built-in already present.
		var builtin = MakeCommand("THINK");
		commands.Add("THINK", (builtin, true));

		var pluginVersion = MakeCommand("THINK");
		manager.RegisterPlugin(new FakePlugin("collider", commands: [pluginVersion], functions: []));

		// The original built-in definition must remain (collision skipped, not overwritten).
		await Assert.That(ReferenceEquals(commands["THINK"].LibraryInformation.Command, builtin.Command)).IsTrue();
	}

	[Test]
	public async Task RegisterPlugin_ThrowingPluginIsIsolated()
	{
		var manager = NewManager(out var commands, out var functions);

		// A throwing plugin must not bubble out and must not corrupt the libraries.
		var (cmdCount, fnCount) = manager.RegisterPlugin(new ThrowingPlugin());

		await Assert.That(cmdCount).IsEqualTo(0);
		await Assert.That(fnCount).IsEqualTo(0);
		await Assert.That(commands.Count).IsEqualTo(0);
		await Assert.That(functions.Count).IsEqualTo(0);

		// A subsequent good plugin still registers — the manager is not left in a broken state.
		manager.RegisterPlugin(new FakePlugin("good", commands: [MakeCommand("+OK")], functions: []));
		await Assert.That(commands.ContainsKey("+OK")).IsTrue();
	}

	private static CommandDefinition MakeCommand(string name) =>
		new(new SharpCommandAttribute { Name = name },
			_ => ValueTask.FromResult(new Option<CallState>(new OneOf.Types.None())));

	private static FunctionDefinition MakeFunction(string name) =>
		new(new SharpFunctionAttribute { Name = name, Flags = FunctionFlags.Regular },
			_ => ValueTask.FromResult(new CallState("0")));

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
