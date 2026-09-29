using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Commands;
using SharpMUSH.Tests.Infrastructure;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// A <c>function_restrictions</c> entry has to reach the function table. PennMUSH applies the
/// <c>restrict_function</c> lines of <c>mush.cnf</c> through <c>restrict_function()</c> (the
/// <c>restrict_function</c> branch of <c>config_set</c>, <c>src/conf.c</c>), which adds the words to
/// the function's restriction bits; <c>check_func</c> (<c>src/function.c</c>) then refuses a guest a
/// <c>noguest</c> function and a gagged or fixed owner a <c>nogagged</c> or <c>nofixed</c> one, and the
/// call answers <c>#-1 PERMISSION DENIED</c> (<c>e_perm</c>, <c>src/parse.c</c>). The option was read
/// into <c>Configurable.FunctionRestrictions</c> and never applied, so it restricted nobody.
///
/// Driven through the seam startup calls, because the shared test host is built once and a test
/// cannot rewrite the configuration it booted with.
/// </summary>
[NotInParallel(GuestLoginTests.GuestCharacters)]
public class ConfiguredFunctionRestrictionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private ConfiguredFunctionRestrictions Restrictions => WebAppFactoryArg.Services.GetRequiredService<ConfiguredFunctionRestrictions>();

	private readonly List<DBRef> _players = [];

	// The configured layer is the host's, and a leftover Guest power would count as a guest character
	// in GuestLoginTests: put both back.
	[After(Test)]
	public async Task Restore()
	{
		Restrictions.Apply(new Dictionary<string, string[]>());
		foreach (var player in _players)
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@power {player}=!Guest"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player}=!GAGGED"));
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player}=!FIXED"));
		}
	}

	private async Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix, string? grant = null)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		_players.Add(player.DbRef);
		if (grant is not null) await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(grant.Replace("{0}", player.DbRef.ToString())));
		return player;
	}

	private async Task<string> Think(TestIsolationHelpers.TestPlayer who, string expression)
	{
		var before = WebAppFactoryArg.Notifications.DeliveryCountFor(who.DbRef);
		await Parser.CommandParse(who.Handle, ConnectionService, MarkupText.Plain($"think {expression}"));
		return WebAppFactoryArg.Notifications.DeliveriesFor(who.DbRef).Skip(before)
			.Single(delivery => delivery.Sender == who.DbRef).Message;
	}

	/// <summary>The issue's acceptance case: <c>lstats: [noguest]</c> refuses a guest, and removing it loosens it.</summary>
	[Test]
	public async Task NoGuest_RefusesAGuestUntilTheEntryIsRemoved()
	{
		var guest = await PlayerAsync("FnCfgGuest", "@power {0}=Guest");
		var mortal = await PlayerAsync("FnCfgMortal");
		await Assert.That(await Think(guest, "lstats()")).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied).Because("precondition");

		Restrictions.Apply(new Dictionary<string, string[]> { ["lstats"] = ["noguest"] });

		await Assert.That(await Think(guest, "lstats()")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await Think(guest, "stats()")).IsEqualTo(ErrorMessages.Returns.PermissionDenied)
			.Because("stats is an alias of lstats, the same FUN in PennMUSH");
		await Assert.That(await Think(mortal, "lstats()")).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);

		Restrictions.Apply(new Dictionary<string, string[]>());

		await Assert.That(await Think(guest, "lstats()")).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await Think(guest, "stats()")).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary><c>nogagged</c> and <c>nofixed</c> read the owner's flag, as <c>Gagged()</c> and <c>Fixed()</c> do.</summary>
	[Test]
	[Arguments("@set {0}=GAGGED", "nogagged")]
	[Arguments("@set {0}=FIXED", "nofixed")]
	public async Task OwnerFlagRestrictions_RefuseTheFlaggedPlayer(string grant, string restriction)
	{
		var flagged = await PlayerAsync("FnCfgFlag", grant);
		var mortal = await PlayerAsync("FnCfgPlain");

		Restrictions.Apply(new Dictionary<string, string[]> { ["lstats"] = [restriction] });

		await Assert.That(await Think(flagged, "lstats()")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
		await Assert.That(await Think(mortal, "lstats()")).IsNotEqualTo(ErrorMessages.Returns.PermissionDenied);
	}

	/// <summary>
	/// <c>restrict_function</c> returns 0 for a name it cannot find rather than failing the
	/// configuration, so an entry naming no function is skipped and the rest still apply.
	/// </summary>
	[Test]
	public async Task EntriesNamingNoFunction_AreSkipped()
	{
		var guest = await PlayerAsync("FnCfgSkip", "@power {0}=Guest");

		Restrictions.Apply(new Dictionary<string, string[]>
		{
			[$"nosuchfn{Guid.NewGuid():N}"] = ["noguest"],
			["lstats"] = ["noguest"]
		});

		await Assert.That(await Think(guest, "lstats()")).IsEqualTo(ErrorMessages.Returns.PermissionDenied);
	}
}
