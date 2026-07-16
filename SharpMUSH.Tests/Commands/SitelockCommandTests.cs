using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Task 16: <c>@sitelock/ban</c>, <c>@sitelock/register</c>, <c>@sitelock/remove</c>, and the
/// generic 2-arg <c>@sitelock &lt;pattern&gt;=&lt;flags&gt;</c> add form. Each mutation persists via
/// the same <see cref="ISharpDatabase.SetExpandedServerData{T}"/> + <see cref="ConfigurationReloadService.SignalChange"/>
/// pattern <c>SitelockController</c> (SharpMUSH.Server) already uses, then triggers
/// <see cref="IBanEnforcer.EnforceHostRuleAsync"/> on add.
/// </summary>
/// <remarks>
/// Observability: assertions read <see cref="IOptionsMonitor{TOptions}"/> (<see cref="SharpMUSHOptions"/>),
/// NOT the <see cref="IOptionsWrapper{T}"/> that <c>Commands.Configuration</c> uses internally.
/// <c>ServerTestWebApplicationBuilderFactory</c> replaces <c>IOptionsWrapper&lt;SharpMUSHOptions&gt;</c>
/// with an NSubstitute stub whose <c>CurrentValue</c> is a fixed snapshot captured once at session
/// start — it never reflects DB reloads in this fixture. <c>IOptionsMonitor&lt;SharpMUSHOptions&gt;</c>
/// is not overridden, so it is the one live-reload-observable path available to these tests (same
/// path <c>ConfigurationControllerTests.ImportConfiguration_UpdatesOptionsMonitor</c> already relies
/// on). The command implementation itself still reads/writes through <c>Configuration.CurrentValue</c>
/// (the wrapper) to mirror the established <c>SitelockController</c> pattern; that is correct in
/// production, where the wrapper *is* reload-aware — only this test fixture's DI override breaks it.
///
/// <see cref="NotInParallelAttribute"/>: <see cref="SitelockRulesOptions"/> is persisted as a single
/// whole-object row (no per-key DB row), so two of these tests mutating it concurrently could clobber
/// each other's in-flight read-modify-write.
/// </remarks>
[NotInParallel]
public class SitelockCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();
	private ConfigurationReloadService ConfigReloadService => WebAppFactoryArg.Services.GetRequiredService<ConfigurationReloadService>();
	private IOptionsMonitor<SharpMUSHOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();

	/// <summary>
	/// Seeds <paramref name="pattern"/> =&gt; <paramref name="flags"/> directly against the DB
	/// (bypassing the command under test), for tests (REMOVE) that need a rule to already exist
	/// without depending on BAN/REGISTER/2-arg-add being correct.
	/// </summary>
	private async Task SeedRuleAsync(string pattern, string[] flags)
	{
		var current = Configuration.CurrentValue;
		var newRules = new Dictionary<string, string[]>(current.SitelockRules.Rules)
		{
			[pattern] = flags
		};
		var updated = current with { SitelockRules = new SitelockRulesOptions(newRules) };
		await Database.SetExpandedServerData(nameof(SharpMUSHOptions), updated);
		ConfigReloadService.SignalChange();
	}

	/// <summary>
	/// Removes <paramref name="pattern"/> from the persisted rules directly against the DB
	/// (bypassing the command under test), restoring pristine state after each test regardless of
	/// whether REMOVE itself is implemented/working yet.
	/// </summary>
	private async Task CleanupRuleAsync(string pattern)
	{
		var current = Configuration.CurrentValue;
		if (!current.SitelockRules.Rules.ContainsKey(pattern))
		{
			return;
		}

		var newRules = new Dictionary<string, string[]>(current.SitelockRules.Rules);
		newRules.Remove(pattern);
		var updated = current with { SitelockRules = new SitelockRulesOptions(newRules) };
		await Database.SetExpandedServerData(nameof(SharpMUSHOptions), updated);
		ConfigReloadService.SignalChange();
	}

	[Test]
	public async ValueTask Ban_AddsConnectCreateGuestRule()
	{
		var pattern = $"*.{TestIsolationHelpers.GenerateUniqueName("evil")}.test";

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@sitelock/ban {pattern}"));

			var rules = Configuration.CurrentValue.SitelockRules.Rules;
			await Assert.That(rules.ContainsKey(pattern)).IsTrue();
			await Assert.That(rules[pattern]).IsEquivalentTo(["!connect", "!create", "!guest"]);
		}
		finally
		{
			await CleanupRuleAsync(pattern);
		}
	}

	[Test]
	public async ValueTask Register_AddsCreateRegisterRule()
	{
		var pattern = $"*.{TestIsolationHelpers.GenerateUniqueName("reg")}.test";

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@sitelock/register {pattern}"));

			var rules = Configuration.CurrentValue.SitelockRules.Rules;
			await Assert.That(rules.ContainsKey(pattern)).IsTrue();
			await Assert.That(rules[pattern]).IsEquivalentTo(["!create", "register"]);
		}
		finally
		{
			await CleanupRuleAsync(pattern);
		}
	}

	[Test]
	public async ValueTask Remove_DeletesRule()
	{
		var pattern = $"*.{TestIsolationHelpers.GenerateUniqueName("gone")}.test";
		await SeedRuleAsync(pattern, ["!connect"]);

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@sitelock/remove {pattern}"));

			var rules = Configuration.CurrentValue.SitelockRules.Rules;
			await Assert.That(rules.ContainsKey(pattern)).IsFalse();
		}
		finally
		{
			await CleanupRuleAsync(pattern);
		}
	}

	[Test]
	public async ValueTask TwoArgAdd_UsesProvidedFlags()
	{
		var pattern = $"*.{TestIsolationHelpers.GenerateUniqueName("custom")}.test";

		try
		{
			await Parser.CommandParse(1, ConnectionService, MModule.single($"@sitelock {pattern}=!connect suspect"));

			var rules = Configuration.CurrentValue.SitelockRules.Rules;
			await Assert.That(rules.ContainsKey(pattern)).IsTrue();
			await Assert.That(rules[pattern]).IsEquivalentTo(["!connect", "suspect"]);
		}
		finally
		{
			await CleanupRuleAsync(pattern);
		}
	}
}
