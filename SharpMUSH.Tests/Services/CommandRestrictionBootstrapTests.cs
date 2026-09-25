using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Where the configured <c>command_restrictions</c> are applied in the boot sequence. A hosted
/// service's position in the host's <c>StartAsync</c> pass is its registration order, and
/// <see cref="CommandRestrictionBootstrapService"/> has to sit inside one window:
/// <list type="bullet">
/// <item>after <c>PluginBootstrapService</c>, which registers plugin <c>[SharpCommand]</c>s into the
/// live library. Applied earlier, a restriction naming a plugin command finds no such command and is
/// skipped — silently, on every boot.</item>
/// <item>before <c>StartupAttributeBootstrapService</c>, which runs every stored <c>@STARTUP</c> as
/// God. Applied later, a command a restriction disables outright is still usable by boot softcode
/// once per start.</item>
/// </list>
/// Both halves came out of review on #1224, and neither is visible in a test of the applier itself,
/// so the sequence is asserted here.
/// </summary>
public class CommandRestrictionBootstrapTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	/// <summary>
	/// The hosted services in registration order, by concrete type. The test host wraps each one, so
	/// a wrapper is unwrapped to the service it holds; an unwrapped registration is read directly.
	/// </summary>
	private string[] HostedServiceOrder()
		=> [.. Factory.Services.GetServices<IHostedService>().Select(service =>
		{
			var inner = service.GetType()
				.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
				.Select(field => field.GetValue(service))
				.OfType<IHostedService>()
				.FirstOrDefault();
			return (inner ?? service).GetType().Name;
		})];

	[Test]
	public async Task RestrictionsAreAppliedAfterPluginsLoadAndBeforeStartupSoftcodeRuns()
	{
		var order = HostedServiceOrder();

		var plugins = Array.IndexOf(order, nameof(PluginBootstrapService));
		var restrictions = Array.IndexOf(order, nameof(CommandRestrictionBootstrapService));
		var startupAttributes = Array.IndexOf(order, nameof(StartupAttributeBootstrapService));

		await Assert.That(plugins).IsGreaterThanOrEqualTo(0).Because("PluginBootstrapService must be registered");
		await Assert.That(restrictions).IsGreaterThanOrEqualTo(0).Because("CommandRestrictionBootstrapService must be registered");
		await Assert.That(startupAttributes).IsGreaterThanOrEqualTo(0).Because("StartupAttributeBootstrapService must be registered");

		await Assert.That(restrictions).IsGreaterThan(plugins)
			.Because("a restriction naming a plugin command is skipped if it is applied before the plugin registers it");
		await Assert.That(restrictions).IsLessThan(startupAttributes)
			.Because("boot @STARTUP runs as God and must not reach a command the configuration disabled");
	}

	/// <summary>
	/// Nothing else applies them: one call site is what makes the window above meaningful.
	/// </summary>
	[Test]
	public async Task OnlyTheBootstrapServiceAppliesThem()
	{
		var callers = Directory.EnumerateFiles(TestPaths.RepositoryRoot, "*.cs", SearchOption.AllDirectories)
			.Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
				&& !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
				&& !path.Contains($"{Path.DirectorySeparatorChar}SharpMUSH.Tests"))
			.Where(path => File.ReadAllText(path).Contains("ApplyConfiguredRestrictionsAsync"))
			.Select(path => Path.GetFileName(path)!)
			.Order()
			.ToArray();

		await Assert.That(callers).IsEquivalentTo(new[]
		{
			"CommandRestrictionBootstrapService.cs",
			"DefinitionRegistryCommands.cs",
			"ICommandRestrictionApplier.cs"
		}).Because("the declaration, the implementation, and exactly one production caller");
	}

	/// <summary>
	/// A change to <c>command_restrictions</c> reaches the command table without a restart, and a
	/// change to anything else does not reapply them, which would discard live
	/// <c>@command/restrict</c>s for nothing (#1250). PennMUSH reads them only at boot
	/// (<c>game.c:757</c>, <c>bsd.c:1284</c>). Driven with its own options monitor: the shared test
	/// host's configuration is every other test's too.
	/// </summary>
	[Test]
	public async Task AChangeToTheRestrictionsIsReappliedAndOtherChangesAreNot()
	{
		var library = Substitute.For<ILibraryProvider<CommandDefinition>, ICommandRestrictionApplier>();
		var applied = new List<IReadOnlyDictionary<string, string[]>>();
		_ = ((ICommandRestrictionApplier)library).ApplyConfiguredRestrictionsAsync(Arg.Do<IReadOnlyDictionary<string, string[]>>(applied.Add));

		var restricted = WithRestrictions(new() { ["THINK"] = ["wizard"] });
		var monitor = new ChangingOptions(restricted);
		using var service = new CommandRestrictionBootstrapService(library, monitor, NullLogger<CommandRestrictionBootstrapService>.Instance);

		await service.StartAsync(CancellationToken.None);
		await Assert.That(applied).Count().IsEqualTo(1).Because("the configured restrictions are applied at boot");

		await monitor.Change(restricted with { Cosmetic = restricted.Cosmetic with { FloatPrecision = 3 } });
		await Assert.That(applied).Count().IsEqualTo(1).Because("another option changed, and the restrictions did not");

		await monitor.Change(WithRestrictions([]));
		await Assert.That(applied).Count().IsEqualTo(2).Because("removing the entry loosens the command at runtime");
		await Assert.That(applied[1]).IsEmpty();

		await monitor.Change(restricted);
		await Assert.That(applied).Count().IsEqualTo(3).Because("adding it back tightens the command at runtime");
		await Assert.That(applied[2].Keys).IsEquivalentTo(new[] { "THINK" });

		await service.StopAsync(CancellationToken.None);
		await monitor.Change(WithRestrictions([]));
		await Assert.That(applied).Count().IsEqualTo(3).Because("a stopped service no longer listens");
	}

	private static SharpMUSHOptions WithRestrictions(Dictionary<string, string[]> restrictions)
	{
		var options = SharpMUSHOptions.Default();
		return options with { Restriction = options.Restriction with { CommandRestrictions = restrictions } };
	}

	/// <summary>An options monitor whose value a test changes, calling the listeners as the real one does.</summary>
	private sealed class ChangingOptions(SharpMUSHOptions initial) : IOptionsMonitor<SharpMUSHOptions>
	{
		private readonly List<Action<SharpMUSHOptions, string?>> _listeners = [];

		public SharpMUSHOptions CurrentValue { get; private set; } = initial;

		public SharpMUSHOptions Get(string? name) => CurrentValue;

		public IDisposable OnChange(Action<SharpMUSHOptions, string?> listener)
		{
			_listeners.Add(listener);
			return new Unsubscribe(() => _listeners.Remove(listener));
		}

		/// <summary>Changes the value and calls every listener; the service's reapply runs to completion inline.</summary>
		public Task Change(SharpMUSHOptions options)
		{
			CurrentValue = options;
			foreach (var listener in _listeners.ToArray())
			{
				listener(options, Options.DefaultName);
			}

			return Task.CompletedTask;
		}

		private sealed class Unsubscribe(Action dispose) : IDisposable
		{
			public void Dispose() => dispose();
		}
	}
}
