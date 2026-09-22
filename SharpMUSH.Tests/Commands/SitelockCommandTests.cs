using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
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
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/ban {pattern}"));

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
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/register {pattern}"));

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
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/remove {pattern}"));

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
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock {pattern}=!connect suspect"));

			var rules = Configuration.CurrentValue.SitelockRules.Rules;
			await Assert.That(rules.ContainsKey(pattern)).IsTrue();
			await Assert.That(rules[pattern]).IsEquivalentTo(["!connect", "suspect"]);
		}
		finally
		{
			await CleanupRuleAsync(pattern);
		}
	}

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who);
		await action();
		return [.. WebAppFactoryArg.Notifications.For(who).Skip(before)];
	}

	/// <summary>Restores the banned-name list to what it was, whatever the command under test did.</summary>
	private async Task RestoreBannedNamesAsync(string[] original)
	{
		var current = Configuration.CurrentValue;
		await Database.SetExpandedServerData(nameof(SharpMUSHOptions), current with { BannedNames = new BannedNamesOptions(original) });
		ConfigReloadService.SignalChange();
	}

	private async Task<string[]> PersistedBannedNamesAsync()
		=> (await Database.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions)))?.BannedNames.BannedNames ?? [];

	/// <summary>
	/// <c>do_sitelock_name</c> (<c>src/wiz.c:2082</c>) appends a wildcard pattern to the names file,
	/// once, and answers "Name &lt;pattern&gt; locked." Here the file is the persisted options row, which
	/// is also what a restart reads back.
	/// </summary>
	[Test]
	public async ValueTask Name_AddsThePatternOnceAndPersistsIt()
	{
		var original = Configuration.CurrentValue.BannedNames.BannedNames;
		var pattern = $"{TestIsolationHelpers.GenerateUniqueName("Nb")}*";
		var god = WebAppFactoryArg.ExecutorDBRef;

		try
		{
			var messages = await MessagesWhile(god,
				async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/name {pattern}")));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/name {pattern.ToLowerInvariant()}"));

			await Assert.That(messages).Contains($"Name {pattern} locked.");
			await Assert.That(Configuration.CurrentValue.BannedNames.BannedNames.Count(n => n.Equals(pattern, StringComparison.OrdinalIgnoreCase)))
				.IsEqualTo(1).Because("strcasecmp finds the existing line, so the name is not added twice");
			await Assert.That(await PersistedBannedNamesAsync()).Contains(pattern);
		}
		finally
		{
			await RestoreBannedNamesAsync(original);
		}
	}

	/// <summary><c>@sitelock/name !&lt;pattern&gt;</c> takes the pattern back out, matching it caselessly.</summary>
	[Test]
	public async ValueTask Name_BangRemovesThePattern()
	{
		var original = Configuration.CurrentValue.BannedNames.BannedNames;
		var pattern = $"{TestIsolationHelpers.GenerateUniqueName("Nr")}*";
		await RestoreBannedNamesAsync([.. original, pattern]);
		var god = WebAppFactoryArg.ExecutorDBRef;

		try
		{
			var messages = await MessagesWhile(god,
				async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/name !{pattern.ToUpperInvariant()}")));

			await Assert.That(messages).Contains("Name removed.");
			await Assert.That(Configuration.CurrentValue.BannedNames.BannedNames).DoesNotContain(pattern);
			await Assert.That(await PersistedBannedNamesAsync()).DoesNotContain(pattern);
		}
		finally
		{
			await RestoreBannedNamesAsync(original);
		}
	}

	/// <summary>Removing a pattern that is not there says so, rather than claiming a removal.</summary>
	[Test]
	public async ValueTask Name_BangOnAnAbsentPatternSaysSo()
	{
		var pattern = TestIsolationHelpers.GenerateUniqueName("Nx");
		var god = WebAppFactoryArg.ExecutorDBRef;

		var messages = await MessagesWhile(god,
			async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@sitelock/name !{pattern}")));

		await Assert.That(messages).DoesNotContain("Name removed.");
		await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.SitelockNameNotBannedFormat, pattern));
	}

	/// <summary>With no argument, <c>do_sitelock_name</c> lists the patterns under its own header.</summary>
	[Test]
	public async ValueTask Name_NoArgumentListsThePatterns()
	{
		var original = Configuration.CurrentValue.BannedNames.BannedNames;
		var pattern = $"{TestIsolationHelpers.GenerateUniqueName("Nl")}*";
		await RestoreBannedNamesAsync([.. original, pattern]);
		var god = WebAppFactoryArg.ExecutorDBRef;

		try
		{
			var messages = await MessagesWhile(god,
				async () => await Parser.CommandParse(1, ConnectionService, MarkupText.Plain("@sitelock/name")));

			await Assert.That(messages).Contains("Any name matching these wildcard patterns is banned:");
			await Assert.That(messages).Contains(pattern);
		}
		finally
		{
			await RestoreBannedNamesAsync(original);
		}
	}

	/// <summary><c>@sitelock</c> is locked to WIZARD (<c>src/command.c:298</c>); a mortal changes nothing.</summary>
	[Test]
	public async ValueTask Name_MortalCannotBanAName()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>(), ConnectionService, "NbMortal");
		var pattern = $"{TestIsolationHelpers.GenerateUniqueName("Nm")}*";

		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@sitelock/name {pattern}"));

		await Assert.That(Configuration.CurrentValue.BannedNames.BannedNames).DoesNotContain(pattern);
		await Assert.That(await PersistedBannedNamesAsync()).DoesNotContain(pattern);
	}

	/// <summary>
	/// <c>ok_player_name</c> (<c>src/predicat.c:731</c>) refuses a name matching a banned pattern,
	/// unless the one asking is a wizard or already goes by that name. <c>valid(playername, …)</c>
	/// asks it of the target (<c>src/funmisc.c:77</c>).
	/// </summary>
	[Test]
	public async ValueTask Name_BannedPatternRefusesPlayerNamesForMortals()
	{
		// Short enough that every name tried stays inside player_name_len, so a 0 can only be the ban.
		var stem = $"Zb{Guid.NewGuid():N}"[..8];
		var mediator = WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, mediator, ConnectionService, "N");

		// The generated name can outrun the test world's player_name_len of 15; give the player one that cannot.
		var ownName = $"Zo{Guid.NewGuid():N}"[..8];
		var mortalObject = (await mediator.Send(new GetObjectNodeQuery(mortal.DbRef))).Expect<AnySharpObject>();
		await mediator.Send(new SetNameCommand(mortalObject, MarkupText.Plain(ownName)));

		using var _ = TestOptionsOverride.Scope(o => o with { BannedNames = new BannedNamesOptions([$"{stem}*", ownName.ToLowerInvariant()]) });

		async Task<string> Valid(long handle, DBRef who, string name)
			=> (await MessagesWhile(who, async () =>
				await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think valid(playername,{name})")))).Single();

		await Assert.That(await Valid(mortal.Handle, mortal.DbRef, $"{stem}x")).IsEqualTo("0").Because("the name matches a banned pattern");
		await Assert.That(await Valid(mortal.Handle, mortal.DbRef, $"Q{stem}")).IsEqualTo("1").Because("the pattern is anchored at the start");
		await Assert.That(await Valid(mortal.Handle, mortal.DbRef, ownName)).IsEqualTo("1").Because("a player may keep a name they already have");
		await Assert.That(await Valid(1, WebAppFactoryArg.ExecutorDBRef, $"{stem}x")).IsEqualTo("1").Because("a wizard is exempt");
	}
}
