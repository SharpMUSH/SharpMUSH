using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>
/// A package's preference groups, written through <c>api/preferences</c> and read in softcode as attributes: an account group's values are on the account's holder, so every character on it shares
/// them; a character group's are on each character.
/// </summary>
public class PreferenceTests : ServerTestBase
{
	private string _package = null!;
	private string _account = null!;
	private string _holder = null!;
	private TestIsolationHelpers.TestPlayer _first = null!;
	private TestIsolationHelpers.TestPlayer _second = null!;
	private TestIsolationHelpers.TestPlayer _stranger = null!;
	private TestIsolationHelpers.TestPlayer _wizard = null!;

	private IAccountService Accounts => WebAppFactoryArg.Services.GetRequiredService<IAccountService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();
	private IPreferenceService Preferences => WebAppFactoryArg.Services.GetRequiredService<IPreferenceService>();

	private string Client => $"{_package}.client";
	private string Sheet => $"{_package}.sheet";

	private static readonly PackagePreferenceGroupSpec ClientGroup = new("client", PreferenceScope.Account, "Client", "", [
		new PackagePreferenceSpec("volume", PreferenceType.Integer, "70", [], 0, 100, Library.Models.Packages.Preferences.DefaultMaxLength, "Volume", ""),
		new PackagePreferenceSpec("theme", PreferenceType.Choice, "dark", ["dark", "light"], null, null, Library.Models.Packages.Preferences.DefaultMaxLength, "", ""),
		new PackagePreferenceSpec("triggers", PreferenceType.Json, null, [], null, null, Library.Models.Packages.Preferences.DefaultMaxLength, "", "") { Version = 2 },
		new PackagePreferenceSpec("nick", PreferenceType.Text, null, [], null, null, Library.Models.Packages.Preferences.DefaultMaxLength, "", "")
	]);

	private static readonly PackagePreferenceGroupSpec SheetGroup = new("sheet", PreferenceScope.Character, "", "", [
		new PackagePreferenceSpec("compact", PreferenceType.Boolean, "0", [], null, null, Library.Models.Packages.Preferences.DefaultMaxLength, "", "")
	]);

	[Before(Test)]
	public async Task InstallAGroupAndLinkTwoCharacters()
	{
		_package = "p" + Guid.NewGuid().ToString("N")[..12];
		await Registry.UpsertInstalledPackageAsync(new InstalledPackageRecord(
			_package, "1.0.0", "test", null, "0", null, DateTimeOffset.UnixEpoch, 1)
		{
			Preferences = [ClientGroup, SheetGroup]
		});

		var services = WebAppFactoryArg.Services;
		_first = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(services, Mediator, ConnectionService, "PrefFirst");
		_second = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(services, Mediator, ConnectionService, "PrefSecond");
		_stranger = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(services, Mediator, ConnectionService, "PrefStranger");
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(services, Mediator, ConnectionService, "PrefWiz");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!));

		var username = TestIsolationHelpers.GenerateUniqueName("pref").ToLowerInvariant();
		var account = (await Accounts.CreateAccountAsync(username, null, "correct horse battery")).Expect<SharpAccount>();
		foreach (var player in new[] { _first, _second })
			await Accounts.AttachCharacterAsync(account.Id!, await Player(player));
		_account = account.Id!;
		_holder = AccountHolders.ForAccount(account.Id)!.Value.ToString();
	}

	[After(Test)]
	public async Task Uninstall()
	{
		await Registry.RemoveInstalledPackageAsync(_package);
		foreach (var player in new[] { _first, _second, _stranger, _wizard })
			await ConnectionService.Disconnect(player.Handle);
	}

	private async Task<SharpPlayer> Player(TestIsolationHelpers.TestPlayer player)
		=> (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();

	private static string Ref(TestIsolationHelpers.TestPlayer player) => $"#{player.DbRef.Number}";

	private Task<string> As(TestIsolationHelpers.TestPlayer player, string command) => CmdAs(player.DbRef, player.Handle, command);

	private string Attribute(string group, string key) => $"PREF`{_package.ToUpperInvariant()}`{group.ToUpperInvariant()}`{key.ToUpperInvariant()}";

	private string AccountValue(string key, string fallback) => $"default(account(%#)/{Attribute("client", key)}, {fallback})";

	[Test]
	public async Task AnAccountValueIsAnAttributeEveryCharacterOnTheAccountReads()
	{
		await Assert.That(await EvalAs(_first.DbRef, AccountValue("volume", "70"))).IsEqualTo("70").Because("nothing is set yet");

		await (await Controller(_first)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": 35}""")), CancellationToken.None);

		await Assert.That(await EvalAs(_second.DbRef, AccountValue("volume", "70"))).IsEqualTo("35");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{Attribute("client", "volume")})")).IsEqualTo("35");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasflag({_holder}/{Attribute("client", "volume")},no_command)")).IsEqualTo("1");
	}

	[Test]
	public async Task ACharacterSetsItsOwnCharacterValueAndThePortalShowsIt()
	{
		await As(_first, $"&{Attribute("sheet", "compact")} me=1");

		var group = Body<PreferenceGroupResponse>(await (await Controller(_first)).Get(Sheet, CancellationToken.None));
		await Assert.That(group.Values!["compact"].GetBoolean()).IsTrue();
		await Assert.That(Body<PreferenceGroupResponse>(await (await Controller(_second)).Get(Sheet, CancellationToken.None)).Values!["compact"].GetBoolean())
			.IsFalse();
	}

	[Test]
	public async Task AValueTheDeclarationDoesNotAllowShowsAsTheDefault()
	{
		await As(_wizard, $"&{Attribute("client", "volume")} {_holder}=900");

		var group = Body<PreferenceGroupResponse>(await (await Controller(null)).Get(Client, CancellationToken.None));
		await Assert.That(group.Values!["volume"].GetInt32()).IsEqualTo(70);
		await Assert.That(group.Preferences.Single(p => p.Key == "volume").IsSet).IsFalse();
	}

	[Test]
	public async Task AnUnknownKeyIsRefused()
	{
		var group = (await Preferences.FindGroupAsync(Client)).Expect<PreferenceGroup>();
		var holder = (await Preferences.HolderAsync(group, _account, null)).Expect<DBRef>();

		await Assert.That((await Preferences.WriteAsync(group, holder, new Dictionary<string, string> { ["colour"] = "red" })).Expect<Error<string>>().Value)
			.IsEqualTo($"{Client} has no preference 'colour'.");
	}

	[Test]
	public async Task ACharacterOnNoAccountHasNoAccountValues()
	{
		await Assert.That(await EvalAs(_stranger.DbRef, $"if(account(%#), {AccountValue("volume", "70")}, 70)")).IsEqualTo("70");

		var group = (await Preferences.FindGroupAsync(Client)).Expect<PreferenceGroup>();
		await Assert.That((await Preferences.HolderAsync(group, null, await Player(_stranger))).Expect<Error<string>>().Value)
			.IsEqualTo("These preferences are kept on an account, and this character is on none.");
	}

	[Test]
	public async Task AValueStaffProtectedIsNotChanged()
	{
		await As(_wizard, $"&{Attribute("sheet", "compact")} {Ref(_first)}=1");
		await As(_wizard, $"@set {Ref(_first)}/{Attribute("sheet", "compact")}=wizard");

		var put = await (await Controller(_first)).Put(Sheet, new PreferenceUpdateRequest(Values("""{"compact": false}""")), CancellationToken.None);
		await Assert.That(Body<string>(put)).IsEqualTo($"{Sheet}/compact is protected, so it cannot be changed here.");
		await Assert.That(Body<string>(await (await Controller(_first)).Reset(Sheet, CancellationToken.None)))
			.IsEqualTo($"{Sheet}/compact is protected, so it cannot be changed here.");
		await Assert.That(await EvalAs(_first.DbRef, $"get(me/{Attribute("sheet", "compact")})")).IsEqualTo("1");
	}

	[Test]
	public async Task ANewValueUnderAProtectedGroupIsNotCreated()
	{
		var branch = $"PREF`{_package.ToUpperInvariant()}`CLIENT";
		await As(_wizard, $"&{Attribute("client", "volume")} {_holder}=50");
		await As(_wizard, $"@set {_holder}/{branch}=wizard");

		var put = await (await Controller(_first)).Put(Client, new PreferenceUpdateRequest(Values("""{"nick": "Ari"}""")), CancellationToken.None);

		await Assert.That(Body<string>(put)).IsEqualTo($"{Client}/nick is protected, so it cannot be changed here.");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasattr({_holder}, {Attribute("client", "nick")})")).IsEqualTo("0");
	}

	[Test]
	public async Task AValueTheAttributeTableProtectsIsNotCreated()
	{
		var nick = Attribute("client", "nick");
		await As(_wizard, $"@attribute/access {nick}=wizard");
		try
		{
			var group = (await Preferences.FindGroupAsync(Client)).Expect<PreferenceGroup>();
			var lone = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("lone").ToLowerInvariant(), null,
				"correct horse battery")).Expect<SharpAccount>();
			foreach (var account in new[] { _account, lone.Id! })
			{
				var holder = (await Preferences.HolderAsync(group, account, null)).Expect<DBRef>();
				await Assert.That((await Preferences.WriteAsync(group, holder, new Dictionary<string, string> { ["nick"] = "Ari" }))
						.Expect<Error<string>>().Value)
					.IsEqualTo($"{Client}/nick is protected, so it cannot be changed here.");
				await Assert.That(await EvalAs(_wizard.DbRef, $"hasattr({holder}, {nick})")).IsEqualTo("0");
			}
		}
		finally
		{
			await As(_wizard, $"@attribute/delete {nick}");
		}
	}

	[Test]
	public async Task AnAccountWithNoCharacterWritesFreelyButNotPastAWriteKey()
	{
		var nick = Attribute("client", "nick");
		var group = (await Preferences.FindGroupAsync(Client)).Expect<PreferenceGroup>();
		var lone = (await Accounts.CreateAccountAsync(TestIsolationHelpers.GenerateUniqueName("lone").ToLowerInvariant(), null,
			"correct horse battery")).Expect<SharpAccount>();
		var holder = (await Preferences.HolderAsync(group, lone.Id!, null)).Expect<DBRef>();

		(await Preferences.WriteAsync(group, holder, new Dictionary<string, string> { ["nick"] = "Ari" })).Expect<IReadOnlyList<PreferenceReading>>();
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/{nick})")).IsEqualTo("Ari");

		await As(_wizard, $"@attribute/access {nick}=none");
		await As(_wizard, $"@attribute/lock/write {nick}==#1");
		try
		{
			await Assert.That((await Preferences.WriteAsync(group, holder, new Dictionary<string, string> { ["nick"] = "Bo" }))
					.Expect<Error<string>>().Value)
				.IsEqualTo($"{Client}/nick is protected, so it cannot be changed here.");
			await Assert.That((await Preferences.ResetAsync(group, holder, [])).Expect<Error<string>>().Value)
				.IsEqualTo($"{Client}/nick is protected, so it cannot be changed here.");
			await Assert.That(await EvalAs(_wizard.DbRef, $"get({holder}/{nick})")).IsEqualTo("Ari");
		}
		finally
		{
			await As(_wizard, $"@attribute/delete {nick}");
		}
	}

	[Test]
	public async Task AResetLeavesAProtectedValueTheGroupNoLongerDeclares()
	{
		await As(_wizard, $"&{Attribute("sheet", "retired")} {Ref(_first)}=kept");
		await As(_wizard, $"@set {Ref(_first)}/{Attribute("sheet", "retired")}=wizard");

		await Assert.That(Body<string>(await (await Controller(_first)).Reset(Sheet, CancellationToken.None)))
			.IsEqualTo($"{Sheet}/retired is protected, so it cannot be changed here.");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({Ref(_first)}/{Attribute("sheet", "retired")})")).IsEqualTo("kept");
	}

	private async Task<PreferencesController> Controller(TestIsolationHelpers.TestPlayer? playing)
	{
		var player = playing is null ? null : await Player(playing);
		var projection = Substitute.For<IVisibleWorldProjection>();
		projection.ResolveCharacterAsync(Arg.Any<CapabilityActor>(), Arg.Any<CancellationToken>()).Returns(player);
		Claim[] claims = player is null
			? [new Claim(ClaimTypes.NameIdentifier, _account)]
			: [new Claim(ClaimTypes.NameIdentifier, _account), new Claim(GameHub.CharacterDbrefClaim, player.Object.DBRef.ToString())];
		return new PreferencesController(Preferences, projection,
			new BearerAccountResolver(Substitute.For<IAccountSessionStore>(), Accounts))
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					User = new ClaimsPrincipal(new ClaimsIdentity(claims, AccountSessionAuthenticationHandler.SchemeName))
				}
			}
		};
	}

	private static T Body<T>(IActionResult result) => (T)((ObjectResult)result).Value!;

	private static Dictionary<string, JsonElement> Values(string json)
		=> JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

	[Test]
	public async Task TheApiWritesWhatTheGameReads()
	{
		var put = await (await Controller(null)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": 15, "triggers": [{"match": "page"}]}""")), CancellationToken.None);

		var group = Body<PreferenceGroupResponse>(put);
		await Assert.That(group.Values!["volume"].GetInt32()).IsEqualTo(15);
		await Assert.That(group.Values["theme"].GetString()).IsEqualTo("dark");
		await Assert.That(group.Values["triggers"].GetRawText()).IsEqualTo("""[{"match":"page"}]""");
		await Assert.That(group.Preferences.Single(p => p.Key == "volume").IsSet).IsTrue();
		await Assert.That(await EvalAs(_second.DbRef, AccountValue("triggers", "none"))).IsEqualTo("""{"version":2,"value":[{"match":"page"}]}""")
			.Because("a json value is stored with the version of its shape");
		await Assert.That(await EvalAs(_second.DbRef, $"json_query(get(account(%#)/{Attribute("client", "triggers")}),get,version)")).IsEqualTo("2")
			.Because("the help's example");
		await Assert.That(await EvalAs(_second.DbRef, $"json_query(get(account(%#)/{Attribute("client", "triggers")}),get,value)")).IsEqualTo("""[{"match":"page"}]""")
			.Because("the help's example");
		var triggers = group.Preferences.Single(p => p.Key == "triggers");
		await Assert.That(triggers.Version).IsEqualTo(2);
		await Assert.That(triggers.DeclaredVersion).IsEqualTo(2);
		await Assert.That(group.Preferences.Single(p => p.Key == "volume").Version).IsNull().Because("only json values carry a version");
	}

	[Test]
	public async Task AJsonValueKeepsTheVersionItWasWrittenAt()
	{
		var holder = (await Preferences.HolderAsync((await Preferences.FindGroupAsync(Client)).Expect<PreferenceGroup>(), _account, null)).Expect<DBRef>();
		var path = Attribute("client", "triggers").Split('`');
		await Mediator.Send(new SetAttributeCommand(holder, path, MString.Plain("""[{"match":"old"}]"""), await Player(_wizard)));

		var bare = Body<PreferenceGroupResponse>(await (await Controller(null)).Get(Client, CancellationToken.None)).Preferences.Single(p => p.Key == "triggers");
		await Assert.That(bare.Value!.Value.GetRawText()).IsEqualTo("""[{"match":"old"}]""");
		await Assert.That(bare.Version).IsEqualTo(1).Because("a value stored without a version is version 1");

		var edited = await (await Controller(null)).Put(Client,
			new PreferenceUpdateRequest(Values("""{"triggers": [{"match": "older"}]}""")) { Versions = new Dictionary<string, int> { ["triggers"] = 1 } },
			CancellationToken.None);
		await Assert.That(Body<PreferenceGroupResponse>(edited).Preferences.Single(p => p.Key == "triggers").Version).IsEqualTo(1)
			.Because("a value edited in its older shape is still that shape");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{Attribute("client", "triggers")})")).IsEqualTo("""{"version":1,"value":[{"match":"older"}]}""");

		var ahead = await (await Controller(null)).Put(Client,
			new PreferenceUpdateRequest(Values("""{"triggers": []}""")) { Versions = new Dictionary<string, int> { ["triggers"] = 3 } },
			CancellationToken.None);
		await Assert.That(Body<string>(ahead)).IsEqualTo($"{Client}/triggers is at version 2; a value cannot be written at version 3.");
	}

	[Test]
	public async Task TheApiRefusesTheWholeRequestWhenOneValueIsRefused()
	{
		var put = await (await Controller(null)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": 15, "theme": "blue"}""")), CancellationToken.None);

		await Assert.That(put).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(Body<string>(put)).IsEqualTo("theme is one of: dark, light.");
		await Assert.That(await EvalAs(_first.DbRef, AccountValue("volume", "70"))).IsEqualTo("70");

		var loud = await (await Controller(null)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": 400}""")), CancellationToken.None);
		await Assert.That(Body<string>(loud)).IsEqualTo("volume is at most 100.");
	}

	[Test]
	public async Task ACharacterGroupNeedsACharacterInTheApi()
	{
		await Assert.That(await (await Controller(null)).Get(Sheet, CancellationToken.None)).IsTypeOf<ConflictObjectResult>();

		var put = await (await Controller(_second)).Put(Sheet, new PreferenceUpdateRequest(Values("""{"compact": true}""")), CancellationToken.None);

		await Assert.That(Body<PreferenceGroupResponse>(put).Values!["compact"].GetBoolean()).IsTrue();
		await Assert.That(await EvalAs(_second.DbRef, $"default(me/{Attribute("sheet", "compact")}, 0)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_first.DbRef, $"default(me/{Attribute("sheet", "compact")}, 0)")).IsEqualTo("0");
	}

	[Test]
	public async Task TheApiListsEveryGroupAndResets()
	{
		await (await Controller(null)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": 5}""")), CancellationToken.None);

		var list = Body<List<PreferenceGroupResponse>>(await (await Controller(null)).List(CancellationToken.None));
		await Assert.That(list.Single(g => g.Id == Client).Values!["volume"].GetInt32()).IsEqualTo(5);
		await Assert.That(list.Single(g => g.Id == Sheet).Unavailable).IsNotNull().Because("no character is being played");

		var reset = Body<PreferenceGroupResponse>(await (await Controller(null)).ResetKey(Client, "volume", CancellationToken.None));
		await Assert.That(reset.Values!["volume"].GetInt32()).IsEqualTo(70);
		await Assert.That(await (await Controller(null)).Get($"{_package}.nothing", CancellationToken.None)).IsTypeOf<NotFoundObjectResult>();
	}

	[Test]
	public async Task TheApiRefusesNullAndPointsAtReset()
	{
		var put = await (await Controller(null)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": null}""")), CancellationToken.None);

		await Assert.That(put).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(Body<string>(put)).Contains($"DELETE api/preferences/{Client}/volume");
	}

	[Test]
	public async Task PlaysSettingsAreAnAccountGroupEveryCharacterShares()
	{
		const string play = "portal.play";
		var get = Body<PreferenceGroupResponse>(await (await Controller(null)).Get(play, CancellationToken.None));
		await Assert.That(get.Scope).IsEqualTo("account");
		await Assert.That(get.Values![PlayPreferences.Echo].GetBoolean()).IsTrue().Because("echo is on until the player turns it off");
		await Assert.That(get.Values[PlayPreferences.HidePages].GetBoolean()).IsFalse();

		var put = await (await Controller(null)).Put(play, new PreferenceUpdateRequest(Values("""{"hide_pages": true}""")), CancellationToken.None);

		await Assert.That(Body<PreferenceGroupResponse>(put).Values![PlayPreferences.HidePages].GetBoolean()).IsTrue();
		await Assert.That(await EvalAs(_second.DbRef, "default(account(%#)/PREF`PORTAL`PLAY`HIDE_PAGES, 0)")).IsEqualTo("1")
			.Because("the help's example");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/PREF`PORTAL`PLAY`HIDE_PAGES)")).IsEqualTo("1");
		var list = Body<List<PreferenceGroupResponse>>(await (await Controller(null)).List(CancellationToken.None));
		await Assert.That(list.Select(g => g.Id)).Contains(play);

		await As(_wizard, $"&PREF`PORTAL`PLAY`HIDE_CHANNELS {_holder}=1");
		var after = Body<PreferenceGroupResponse>(await (await Controller(null)).Get(play, CancellationToken.None));
		await Assert.That(after.Values![PlayPreferences.HideChannels].GetBoolean()).IsTrue().Because("staff set the attribute directly");
	}
}
