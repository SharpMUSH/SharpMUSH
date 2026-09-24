using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
}
