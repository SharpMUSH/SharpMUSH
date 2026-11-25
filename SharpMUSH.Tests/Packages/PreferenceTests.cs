using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
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
/// A package's preference groups, read and written in the game with <c>@preference</c> and <c>pref()</c> and through
/// <c>api/preferences</c>: an account group's values are on the account's holder, so every character on it shares
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
		new PackagePreferenceSpec("triggers", PreferenceType.Json, null, [], null, null, Library.Models.Packages.Preferences.DefaultMaxLength, "", "")
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

	[Test]
	public async Task AnAccountPreferenceIsSharedByEveryCharacterOnTheAccount()
	{
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},volume)")).IsEqualTo("70").Because("the default, until one is set");

		await Assert.That(await As(_first, $"@preference {Client}/volume=35")).IsEqualTo($"{Client}/volume is now 35.");

		await Assert.That(await EvalAs(_second.DbRef, $"pref({Client},VOLUME)")).IsEqualTo("35");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/{Attribute("client", "volume")})")).IsEqualTo("35");
		await Assert.That(await EvalAs(_wizard.DbRef, $"hasflag({_holder}/{Attribute("client", "volume")},no_command)")).IsEqualTo("1");
	}

	[Test]
	public async Task ACharacterPreferenceIsEachCharactersOwn()
	{
		await As(_first, $"@preference {Sheet}/compact=on");

		await Assert.That(await EvalAs(_first.DbRef, $"pref({Sheet},compact)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_second.DbRef, $"pref({Sheet},compact)")).IsEqualTo("0");
		await Assert.That(await EvalAs(_first.DbRef, $"get(me/{Attribute("sheet", "compact")})")).IsEqualTo("1");
	}

	[Test]
	public async Task ARefusedValueSaysWhyAndChangesNothing()
	{
		await As(_first, $"@preference {Client}/volume=40");

		await Assert.That(await As(_first, $"@preference {Client}/volume=400")).IsEqualTo("volume is at most 100.");
		await Assert.That(await As(_first, $"@preference {Client}/theme=blue")).IsEqualTo("theme is one of: dark, light.");
		await Assert.That(await As(_first, $"@preference {Client}/colour=red")).IsEqualTo($"{Client} has no preference 'colour'.");
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},volume)")).IsEqualTo("40");
	}

	[Test]
	public async Task ResetReturnsAKeyOrTheWholeGroupToItsDefaults()
	{
		await As(_first, $"@preference {Client}/volume=10");
		await As(_first, $"@preference {Client}/theme=light");

		await Assert.That(await As(_first, $"@preference/reset {Client}/volume")).IsEqualTo($"{Client}/volume is back to its default.");
		await Assert.That(await EvalAs(_first.DbRef, $"[pref({Client},volume)] [pref({Client},theme)]")).IsEqualTo("70 light");

		await Assert.That(await As(_first, $"@preference/reset {Client}")).IsEqualTo($"{Client} is back to its defaults.");
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},theme)")).IsEqualTo("dark");
	}

	[Test]
	public async Task AnotherPlayersPreferenceIsReadAsGetReadsIt()
	{
		await As(_first, $"@preference {Client}/volume=20");

		await Assert.That(await EvalAs(_stranger.DbRef, $"pref({Client},volume,{Ref(_first)})")).IsEqualTo("#-1 NO PERMISSION TO GET ATTRIBUTE");
		await Assert.That(await EvalAs(_wizard.DbRef, $"pref({Client},volume,{Ref(_first)})")).IsEqualTo("20");
		await Assert.That(await EvalAs(_second.DbRef, $"pref({Client},volume,{Ref(_first)})")).IsEqualTo("20")
			.Because("a character on the account reads its attributes");
	}

	[Test]
	public async Task AValueTheDeclarationNoLongerAllowsReadsAsTheDefault()
	{
		await As(_wizard, $"&{Attribute("client", "volume")} {_holder}=900");

		await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},volume)")).IsEqualTo("70");
	}

	[Test]
	public async Task UnknownGroupsAndKeysAreErrors()
	{
		await Assert.That(await EvalAs(_first.DbRef, $"pref({_package}.nothing,volume)")).IsEqualTo("#-1 NO SUCH PREFERENCE GROUP");
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},nothing)")).IsEqualTo("#-1 NO SUCH PREFERENCE");
		await Assert.That(await As(_first, $"@preference {_package}.nothing"))
			.IsEqualTo($"No installed package declares the preference group '{_package}.nothing'. See @preference.");
	}

	[Test]
	public async Task ACharacterOnNoAccountHasTheDefaultsOfAnAccountGroupAndCannotSetThem()
	{
		await Assert.That(await EvalAs(_stranger.DbRef, $"pref({Client},volume)")).IsEqualTo("70");
		await Assert.That(await As(_stranger, $"@preference {Client}/volume=5"))
			.IsEqualTo("These preferences are kept on an account, and this character is on none.");
	}

	[Test]
	public async Task TheGroupShowsEachValueAndWhatItAllows()
	{
		await As(_first, $"@preference {Client}/volume=35");

		var shown = await As(_first, $"@preference {Client}");

		await Assert.That(shown).Contains("kept on your account");
		await Assert.That(shown).Contains("35");
		await Assert.That(shown).Contains("dark (default)");
		await Assert.That(shown).Contains("integer, 0 to 100");
		await Assert.That(await As(_first, "@preference")).Contains(Sheet);
	}

	[Test]
	public async Task OneKeyIsShownAlone()
	{
		var shown = await As(_first, $"@preference {Client}/volume");

		await Assert.That(shown).Contains("70 (default)");
		await Assert.That(shown).DoesNotContain("theme");
		await Assert.That(await As(_first, $"@preference {Client}/colour")).IsEqualTo($"{Client} has no preference 'colour'.");
	}

	[Test]
	public async Task APackagesCodeReadsThePreferencesOfWhoeverRunsIt()
	{
		var thing = await EvalAs(_wizard.DbRef, "create(PrefReader)");
		await As(_wizard, $"@set {thing}=WIZARD");
		var command = TestIsolationHelpers.GenerateUniqueName("prefcheck").ToLowerInvariant();
		await As(_wizard, $"&CMD {thing}=${command}:@pemit %#={command} [pref({Client},volume)]");
		await As(_wizard, $"@set {thing}=!NO_COMMAND");
		await As(_wizard, $"@tel {thing}={Ref(_first)}");
		await As(_first, $"@preference {Client}/volume=35");

		var before = Notifications.CountFor(_first.DbRef);

		await As(_first, command);

		// The value of whoever typed the command, not of the object's owner (the wizard has the default, 70).
		await Notifications.WaitForAsync(_first.DbRef, $"{command} 35", startIndex: before);
	}

	[Test]
	public async Task APlayersOwnObjectReadsTheirCharacterPreferences()
	{
		await As(_first, $"@preference {Sheet}/compact=on");
		var thing = await EvalAs(_first.DbRef, "create(MyReader)");
		await As(_first, $"&FN {thing}=pref({Sheet},compact)");

		await Assert.That(await EvalAs(_first.DbRef, $"u({thing}/FN)")).IsEqualTo("1");
	}

	[Test]
	public async Task AStrangerCannotLearnWhetherAPlayerIsOnAnAccount()
		=> await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},volume,{Ref(_stranger)})"))
			.IsEqualTo("#-1 NO PERMISSION TO GET ATTRIBUTE");

	[Test]
	public async Task AValueStaffProtectedIsNotChanged()
	{
		await As(_wizard, $"&{Attribute("sheet", "compact")} {Ref(_first)}=1");
		await As(_wizard, $"@set {Ref(_first)}/{Attribute("sheet", "compact")}=wizard");

		await Assert.That(await As(_first, $"@preference {Sheet}/compact=off")).IsEqualTo($"{Sheet}/compact is protected, so it cannot be changed here.");
		await Assert.That(await As(_first, $"@preference/reset {Sheet}")).IsEqualTo($"{Sheet}/compact is protected, so it cannot be changed here.");
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Sheet},compact)")).IsEqualTo("1");
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
		await Assert.That(await EvalAs(_second.DbRef, $"pref({Client},triggers)")).IsEqualTo("""[{"match":"page"}]""");
	}

	[Test]
	public async Task TheApiRefusesTheWholeRequestWhenOneValueIsRefused()
	{
		var put = await (await Controller(null)).Put(Client, new PreferenceUpdateRequest(Values("""{"volume": 15, "theme": "blue"}""")), CancellationToken.None);

		await Assert.That(put).IsTypeOf<BadRequestObjectResult>();
		await Assert.That(Body<string>(put)).IsEqualTo("theme is one of: dark, light.");
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Client},volume)")).IsEqualTo("70");
	}

	[Test]
	public async Task ACharacterGroupNeedsACharacterInTheApi()
	{
		await Assert.That(await (await Controller(null)).Get(Sheet, CancellationToken.None)).IsTypeOf<ConflictObjectResult>();

		var put = await (await Controller(_second)).Put(Sheet, new PreferenceUpdateRequest(Values("""{"compact": true}""")), CancellationToken.None);

		await Assert.That(Body<PreferenceGroupResponse>(put).Values!["compact"].GetBoolean()).IsTrue();
		await Assert.That(await EvalAs(_second.DbRef, $"pref({Sheet},compact)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_first.DbRef, $"pref({Sheet},compact)")).IsEqualTo("0");
	}

	[Test]
	public async Task TheApiListsEveryGroupAndResets()
	{
		await As(_first, $"@preference {Client}/volume=5");

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
		await Assert.That(await EvalAs(_second.DbRef, $"pref({play},hide_pages)")).IsEqualTo("1");
		await Assert.That(await EvalAs(_wizard.DbRef, $"get({_holder}/PREF`PORTAL`PLAY`HIDE_PAGES)")).IsEqualTo("1");
		var list = Body<List<PreferenceGroupResponse>>(await (await Controller(null)).List(CancellationToken.None));
		await Assert.That(list.Select(g => g.Id)).Contains(play);
		await Assert.That(await As(_second, $"@preference {play}/hide_channels=on")).IsEqualTo($"{play}/hide_channels is now 1.");
	}
}
