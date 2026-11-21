using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mediator;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// The bundled achievements package, installed from the catalogue and driven with typed commands: plaques made,
/// given colours and a font, awarded with a citation kept on the character, renamed with their awards kept, taken back
/// and destroyed; featured plaques kept to the ones a character holds; and the portal routes' JSON. Each test installs the package and removes it again. Not
/// in parallel: the command is global.
/// </summary>
[NotInParallel]
public class AchievementsIntegrationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private TestHelpers.NotificationRecorder Notifications => WebAppFactoryArg.Notifications;
	private IPackageInstallService Installer => WebAppFactoryArg.Services.GetRequiredService<IPackageInstallService>();
	private IPackageRegistryService Registry => WebAppFactoryArg.Services.GetRequiredService<IPackageRegistryService>();

	private async Task<string> God(string command) =>
		(await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command))).Message.ToPlainText()?.Trim() ?? string.Empty;

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText().Trim();

	/// <summary>What <paramref name="player"/> was told while <paramref name="command"/> ran.</summary>
	private async Task<string> As(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = Notifications.CountFor(player.DbRef);
		await Parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		await WebAppFactoryArg.QueueBarrierAsync();
		return string.Join("\n", Notifications.For(player.DbRef).Skip(before));
	}

	private string Heard(TestIsolationHelpers.TestPlayer player) => string.Join("\n", Notifications.For(player.DbRef));

	private async Task<DBRef> RoomAsync(string prefix)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName($"{prefix}Room"), god));
	}

	private Task<TestIsolationHelpers.TestPlayer> Player(string prefix, DBRef room) =>
		TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix, room);

	private async Task<ApplyResponse> InstallAsync()
	{
		var controller = new PackagesController(
			Registry,
			WebAppFactoryArg.Services.GetRequiredService<IPackageSourceService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageManifestService>(),
			Installer,
			WebAppFactoryArg.Services.GetRequiredService<IPackageAuthoringService>(),
			WebAppFactoryArg.Services.GetRequiredService<IPackageOperationRunner>(),
			WebAppFactoryArg.Services.GetRequiredService<PluginUploadStore>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Server.Services.PackageManifestResolver>(),
			WebAppFactoryArg.Services.GetRequiredService<IAuditLog>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Models.Packages.EngineVersion>(),
			WebAppFactoryArg.Services.GetRequiredService<SharpMUSH.Library.Services.Interfaces.IBundledPackageBootstrap>());
		var applied = await controller.Apply(
			new ApplyRequest(BundledPackages.RemoteName, "achievements", null, null, null), CancellationToken.None);
		await Assert.That(applied.Result).IsTypeOf<OkObjectResult>().Because("achievements must install from the catalogue");
		// AINSTALL is queued after the apply: it adds the command.
		await WebAppFactoryArg.QueueBarrierAsync();
		return applied.Result is OkObjectResult { Value: ApplyResponse response }
			? response
			: throw new InvalidOperationException("The apply answered without its response.");
	}

	/// <summary>A player holding softcode.achievements.admin, as staff who award plaques would.</summary>
	private async Task<TestIsolationHelpers.TestPlayer> StaffAsync(string prefix, DBRef room)
	{
		var staff = await Player(prefix, room);
		await God($"@permission/allow #{staff.DbRef.Number}=softcode.achievements.admin");
		return staff;
	}

	private async Task UninstallAsync() => await WebAppFactoryArg.Services.UninstallWithBridgesAsync("achievements");

	/// <summary><paramref name="expression"/> evaluated as the Achievements object, parsed as JSON.</summary>
	private async Task<JsonElement> Json(ApplyResponse installed, string expression)
	{
		var text = await Eval($"objeval({installed.CreatedObjects["achievements"]},{expression})");
		using var document = JsonDocument.Parse(text);
		return document.RootElement.Clone();
	}

	/// <summary>The number a "Plaque N, ... is made." line gives.</summary>
	private static string Number(string made) =>
		System.Text.RegularExpressions.Regex.Match(made, @"Plaque (\d+),").Groups[1].Value;

	[Test]
	public async Task PlaquesAreAwardedAndKeptThroughRenames()
	{
		try
		{
			var installed = await InstallAsync();
			var room = await RoomAsync("AchAward");
			var wren = await StaffAsync("AchWren", room);
			var ada = await Player("AchAda", room);
			var plaque = TestIsolationHelpers.GenerateUniqueName("AchHero");

			await Assert.That(await As(wren, $"+achievements/create {plaque}=Held the north gate.")).Contains("is made. +achievements/award <player>=");
			await Assert.That(await As(wren, $"+achievements/create {plaque}=Again")).Contains("is called that already.");
			await Assert.That(await As(wren, "+achievements/create 12=x")).Contains("cannot be a number");

			// Colours and font are checked before they are kept: both colours or neither, far enough apart to read.
			await Assert.That(await As(wren, $"+achievements/colour {plaque}=#777777/#888888")).Contains("too close to read: 1.3:1");
			await Assert.That(await As(wren, $"+achievements/colour {plaque}=#1a1a1a")).Contains("both colours, or neither");
			await Assert.That(await As(wren, $"+achievements/colour {plaque}=nope/#ffffff")).Contains("'nope' is not a colour.");
			await Assert.That(await As(wren, $"+achievements/colour {plaque}=+gold/#3b1f00")).Contains("is #ffd700 on #3b1f00 now.");
			await Assert.That(await As(wren, $"+achievements/colour {plaque}")).Contains("+achievements/colour <plaque>=<text>/<background> sets its colours");
			await Assert.That(await As(wren, $"+achievements/font {plaque}=wavy")).Contains("A font style is any of bold, italic and underline.");
			await Assert.That(await As(wren, $"+achievements/font {plaque}=Italic, bold")).Contains("is in bold and italic now.");
			// Clearing them leaves no empty attribute behind to count against the store.
			var achievements = installed.CreatedObjects["achievements"];
			var store = installed.CreatedObjects["store"];
			var number = await Eval($"objeval({achievements},u(FUN`PLAQUE`EXACT,{plaque},u(FUN`PLAQUE`IDS)))");
			await Assert.That(await Eval($"get({store}/PLAQUE`{number}`FONT)")).IsEqualTo("bold italic");
			var background = $"hasattr({store},PLAQUE`{number}`BACKGROUND)";
			await Assert.That(await Eval(background)).IsEqualTo("1");
			await Assert.That(await As(wren, $"+achievements/colour {plaque}=")).Contains("takes the theme's colours now.");
			await Assert.That(await Eval(background)).IsEqualTo("0");
			await Assert.That(await Eval($"hasattr({store},PLAQUE`{number}`FOREGROUND)")).IsEqualTo("0");
			await Assert.That(await As(wren, "+achievements/create Two%rLines=x")).Contains("or a line break.");

			// An award: the player is told, and the citation is kept as it was typed and evaluated, never run again.
			var awarded = await As(wren, $"+achievements/award {ada.Name}={plaque}/For the gate, a broom; and [add(1,2)] ladders");
			await Assert.That(awarded).Contains($"{ada.Name} holds {plaque} now.");
			await Assert.That(Heard(ada)).Contains($"You were awarded the plaque {plaque}.");
			await Assert.That(await As(wren, $"+achievements/award {ada.Name}={plaque}")).Contains("already");
			await Assert.That(await As(ada, "+achievements")).Contains(plaque).And.Contains("For the gate, a broom; and 3 ladders");
			// The award is on Ada, who cannot write it, and nothing about her is on the store.
			var adaRef = $"#{ada.DbRef.Number}";
			await Assert.That(await Eval($"get({adaRef}/ACHIEVEMENT`{number}`BY)")).IsEqualTo(await Eval($"objid(#{wren.DbRef.Number})"));
			await Assert.That(await Eval($"words(lattr({store}/AWARD**))")).IsEqualTo("0");
			await Assert.That(await As(ada, $"&ACHIEVEMENT`{number}`CITATION me=I wrote this")).Contains("NO PERMISSION");
			await Assert.That(await Eval($"get({adaRef}/ACHIEVEMENT`{number}`CITATION)")).IsEqualTo("For the gate, a broom; and 3 ladders");

			// Renaming the plaque and the player changes nothing else.
			var renamed = TestIsolationHelpers.GenerateUniqueName("AchNorth");
			await Assert.That(await As(wren, $"+achievements/rename {plaque}={renamed}")).Contains("Everyone holding it keeps it.");
			// player_name_len is 15, so the new name is short and made unique by the dbref.
			var newName = $"AchBea{ada.DbRef.Number}";
			await God($"@name #{ada.DbRef.Number}={newName}");
			await Assert.That(await As(ada, "+achievements")).Contains(renamed).And.Contains(newName);
			await Assert.That(await As(wren, $"+achievements/view {renamed}")).Contains($"Held by {newName}.");

			// Taking it back, and destroying a plaque someone holds, which needs a yes.
			await Assert.That(await As(wren, $"+achievements/revoke {newName}={renamed}")).Contains($"{newName} no longer holds {renamed}.");
			await Assert.That(await As(ada, "+achievements")).Contains("You hold no plaques yet.");
			// Nothing is left on her once she holds nothing.
			await Assert.That(await Eval($"hasattr({adaRef},ACHIEVEMENT)")).IsEqualTo("0");
			await As(wren, $"+achievements/award {newName}={renamed}");
			await Assert.That(await As(wren, $"+achievements/destroy {renamed}")).Contains("=yes destroys it and takes it from them.");
			await Assert.That(await As(wren, $"+achievements/destroy {renamed}=yes")).Contains("is destroyed, and taken from 1 character.");
			await Assert.That(await As(ada, "+achievements")).Contains("You hold no plaques yet.");
			await Assert.That(await Eval($"hasattr({adaRef},ACHIEVEMENT)")).IsEqualTo("0");

			// Players read; only staff change.
			await Assert.That(await As(ada, "+achievements/create Mine=x")).Contains("Permission denied. You need the softcode.achievements.admin permission.");
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task OnlyHeldPlaquesAreFeatured()
	{
		try
		{
			var installed = await InstallAsync();
			var room = await RoomAsync("AchFeature");
			var wren = await StaffAsync("AchFeatWren", room);
			var bo = await Player("AchBo", room);
			var names = Enumerable.Range(1, 5).Select(i => TestIsolationHelpers.GenerateUniqueName($"AchP{i}")).ToList();
			var numbers = new List<string>();
			foreach (var name in names)
				numbers.Add(Number(await As(wren, $"+achievements/create {name}=About {name}.")));
			foreach (var name in names.Take(4))
				await As(wren, $"+achievements/award {bo.Name}={name}");

			// One not held is refused, and so is a fourth past the game's three.
			await Assert.That(await As(bo, $"+achievements/feature {names[4]}")).Contains("You do not hold a plaque called");
			await Assert.That(await As(bo, $"+achievements/feature {names[2]}")).Contains($"You feature {names[2]} now.");
			await As(bo, $"+achievements/feature {names[0]}");
			await As(bo, $"+achievements/feature {names[1]}");
			await Assert.That(await As(bo, $"+achievements/feature {names[3]}")).Contains("as many as the game allows");

			var achievements = installed.CreatedObjects["achievements"];
			var featured = $"u({achievements}/FUN`FEATURED,objid(#{bo.DbRef.Number}))";
			await Assert.That(await Eval(featured)).IsEqualTo($"{numbers[2]} {numbers[0]} {numbers[1]}");

			// One taken back leaves the list at once, without anyone changing it.
			await As(wren, $"+achievements/revoke {bo.Name}={names[0]}");
			await Assert.That(await Eval(featured)).IsEqualTo($"{numbers[2]} {numbers[1]}");
			await Assert.That(await Eval($"u({achievements}/FUN`FEATURED`NAMES,objid(#{bo.DbRef.Number}))"))
				.IsEqualTo($"{names[2]} · {names[1]}");
			await Assert.That(await As(bo, $"+achievements/unfeature {names[2]}")).Contains($"You no longer feature {names[2]}.");
			await Assert.That(await Eval(featured)).IsEqualTo(numbers[1]);

			// Featured plaques come first in the case.
			var shown = await As(bo, "+achievements");
			await Assert.That(shown.IndexOf(names[1], StringComparison.Ordinal)).IsLessThan(shown.IndexOf(names[2], StringComparison.Ordinal));
		}
		finally
		{
			await UninstallAsync();
		}
	}

	[Test]
	public async Task PortalRoutesAnswerJsonForStaffAndPlayers()
	{
		try
		{
			var installed = await InstallAsync();
			var room = await RoomAsync("AchWeb");
			var wren = await StaffAsync("AchWebWren", room);
			var cy = await Player("AchCy", room);
			var plaque = TestIsolationHelpers.GenerateUniqueName("AchWebHero");
			var number = Number(await As(wren, $"+achievements/create {plaque}=Held the gate."));
			await As(wren, $"+achievements/award {cy.Name}={plaque}/For the gate");
			string Objid(TestIsolationHelpers.TestPlayer p) => $"objid(#{p.DbRef.Number})";

			// A player reads the list; staff get the form to make one.
			var list = await Json(installed, $"uargs(me/WEB`SCHEMA,viewer,{Objid(cy)},at,)");
			await Assert.That(list.GetProperty("kind").GetString()).IsEqualTo("view");
			var staffList = await Json(installed, $"uargs(me/WEB`SCHEMA,viewer,{Objid(wren)},at,)");
			await Assert.That(staffList.GetProperty("kind").GetString()).IsEqualTo("form");
			await Assert.That(staffList.GetRawText()).Contains("\"key\":\"create\"").And.DoesNotContain("#-1");

			// Every plaque is a pill: its name, its colours (both or neither), its description to show on hover, a link.
			await As(wren, $"+achievements/colour {plaque}=#f5e9c8/#1b3a5c");
			await As(wren, $"+achievements/font {plaque}=bold");
			var rows = await Json(installed, $"uargs(me/WEB`DATA,viewer,{Objid(cy)},at,)");
			var pill = rows.GetProperty("fields").GetProperty("plaques").GetProperty("value")[0];
			await Assert.That(pill.GetProperty("text").GetString()).IsEqualTo(plaque);
			await Assert.That(pill.GetProperty("background").GetString()).IsEqualTo("#1b3a5c");
			await Assert.That(pill.GetProperty("foreground").GetString()).IsEqualTo("#f5e9c8");
			await Assert.That(pill.GetProperty("font").GetString()).IsEqualTo("bold");
			await Assert.That(pill.GetProperty("hint").GetString()).IsEqualTo("Held the gate.");
			await Assert.That(pill.GetProperty("link").GetString()).IsEqualTo($"/apps/achievements/{number}");
			await Assert.That(list.GetRawText()).Contains("\"kind\":\"chips\"");

			// One plaque: its holders, and for staff the button to take it back.
			var page = await Json(installed, $"uargs(me/WEB`SCHEMA,viewer,{Objid(wren)},at,{number})");
			await Assert.That(page.GetProperty("title").GetString()).IsEqualTo(plaque);
			var holders = await Json(installed, $"uargs(me/WEB`DATA,viewer,{Objid(wren)},at,{number})");
			var holder = holders.GetProperty("fields").GetProperty("holders").GetProperty("value")[0];
			await Assert.That(holder.GetProperty("author").GetString()).IsEqualTo(cy.Name);
			await Assert.That(holder.GetProperty("actions").GetArrayLength()).IsEqualTo(1);
			var playerHolders = await Json(installed, $"uargs(me/WEB`DATA,viewer,{Objid(cy)},at,{number})");
			await Assert.That(playerHolders.GetProperty("fields").GetProperty("holders").GetProperty("value")[0]
				.GetProperty("actions").GetArrayLength()).IsEqualTo(0);

			// The profile widget shows the plaques the character holds as pills.
			var profile = await Json(installed, $"uargs(me/WEB`PROFILE`SCHEMA,target,{Objid(cy)})");
			await Assert.That(profile.GetRawText()).Contains("\"kind\":\"chips\"");
			var profileData = await Json(installed, $"uargs(me/WEB`PROFILE`DATA,target,{Objid(cy)})");
			await Assert.That(profileData.GetProperty("fields").GetProperty("plaques").GetProperty("value")[0]
				.GetProperty("text").GetString()).IsEqualTo(plaque);
		}
		finally
		{
			await UninstallAsync();
		}
	}

	private sealed record PortalCharacter(int DbrefNumber);
	private sealed record PortalAccount(string AccountSessionToken);

	/// <summary>A new account with one character, and an HTTP client signed in as it.</summary>
	private async Task<(HttpClient Http, int Dbref)> SignedInAsync(string prefix)
	{
		var http = WebAppFactoryArg.CreateHttpClient();
		http.BaseAddress = new Uri("https://localhost/");
		const string password = "Achievements-Pw-1!";
		var register = await http.PostAsJsonAsync("api/auth/account-register",
			new { Username = $"{prefix}{Guid.NewGuid():N}"[..15], Email = (string?)null, Password = password });
		await Assert.That(register.StatusCode).IsEqualTo(HttpStatusCode.OK);
		var account = (await register.Content.ReadFromJsonAsync<PortalAccount>())!;
		http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccountSessionToken);
		var created = await http.PostAsJsonAsync("api/account/characters",
			new { Name = $"{prefix}{Guid.NewGuid():N}"[..15], Password = password });
		await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (http, (await created.Content.ReadFromJsonAsync<PortalCharacter>())!.DbrefNumber);
	}

	/// <summary>What the page's press of a button gets back from POST /http/achievements/act.</summary>
	private static async Task<JsonNode> ActAsync(HttpClient http, object body)
	{
		var response = await http.PostAsJsonAsync("http/achievements/act", body);
		var text = (await response.Content.ReadAsStringAsync()).Trim();
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(text);
		await Assert.That(text).StartsWith("{").Because(text);
		return JsonNode.Parse(text)!;
	}

	[Test]
	public async Task PortalFormsWriteThroughTheActRoute()
	{
		try
		{
			var installed = await InstallAsync();
			var (staff, staffDbref) = await SignedInAsync("AchStaff");
			await God($"@permission/allow #{staffDbref}=softcode.achievements.admin");
			var (player, playerDbref) = await SignedInAsync("AchPlay");
			var playerName = await Eval($"name(#{playerDbref})");
			var plaque = TestIsolationHelpers.GenerateUniqueName("AchForm");

			// A refusal names the field it is about; a player is refused outright.
			var noName = await ActAsync(staff, new { op = "create", name = "", description = "x" });
			await Assert.That(noName["errors"]!["name"]!.GetValue<string>()).IsEqualTo("Give the plaque a name.");
			var refused = await ActAsync(player, new { op = "create", name = plaque, description = "x" });
			await Assert.That(refused["errors"]!["_global"]!.GetValue<string>()).StartsWith("Permission denied.");

			var tooClose = await ActAsync(staff,
				new { op = "create", name = plaque, description = "Held the gate.", foreground = "#777777", background = "#888888" });
			await Assert.That(tooClose["errors"]!["background"]!.GetValue<string>()).Contains("too close to read");
			var oneColour = await ActAsync(staff,
				new { op = "create", name = plaque, description = "Held the gate.", foreground = "#ffffff", background = "" });
			await Assert.That(oneColour["errors"]!["background"]!.GetValue<string>()).Contains("both colours, or neither");
			var badColour = await ActAsync(staff,
				new { op = "create", name = plaque, description = "Held the gate.", foreground = "nope", background = "#ffffff" });
			await Assert.That(badColour["errors"]!["foreground"]!.GetValue<string>()).IsEqualTo("'nope' is not a colour. Use #rrggbb such as #1b3a5c, or a name such as +gold.");

			var made = await ActAsync(staff,
				new { op = "create", name = plaque, description = "Held the gate.", foreground = "#F5E9C8", background = "#1b3a5c", font = new[] { "italic", "bold" } });
			var page = made["redirect"]!.GetValue<string>();
			await Assert.That(page).StartsWith("/apps/achievements/");
			var number = page["/apps/achievements/".Length..];
			var store = installed.CreatedObjects["store"];
			await Assert.That(await Eval($"get({store}/PLAQUE`{number}`FOREGROUND) on [get({store}/PLAQUE`{number}`BACKGROUND)] in [get({store}/PLAQUE`{number}`FONT)]"))
				.IsEqualTo("#f5e9c8 on #1b3a5c in bold italic");

			// The page drawn again after a press is still the staff form.
			var awarded = await ActAsync(staff, new { op = "award", plaque = number, character = playerName, citation = "For the gate" });
			await Assert.That(awarded["message"]!.GetValue<string>()).IsEqualTo($"{playerName} holds {plaque} now.");
			await Assert.That(awarded["schema"]!["kind"]!.GetValue<string>()).IsEqualTo("form");
			var again = await ActAsync(staff, new { op = "award", plaque = number, character = playerName, citation = "" });
			await Assert.That(again["errors"]!["character"]!.GetValue<string>()).IsEqualTo($"{playerName} holds {plaque} already.");

			// Only the named actions run, and typed text with quotes, braces and commas is kept as typed.
			var unknown = await ActAsync(staff, new { op = "*", plaque = number });
			await Assert.That(unknown["errors"]!["_global"]!.GetValue<string>()).IsEqualTo("There is no such action.");
			const string awkward = "He said \"hold\", {then} [left]; 2, 3.";
			var saved = await ActAsync(staff,
				new { op = "save", plaque = number, name = plaque, description = awkward, foreground = "", background = "", font = Array.Empty<string>() });
			await Assert.That(saved["message"]!.GetValue<string>()).IsEqualTo($"{plaque} is saved.");
			await Assert.That(await Eval($"u({installed.CreatedObjects["achievements"]}/FUN`PLAQUE`DESCRIPTION,{number})")).IsEqualTo(awkward);
			// Saving without colours or font clears them.
			await Assert.That(await Eval($"words(lattr({store}/PLAQUE`{number}`*))")).IsEqualTo("3");

			// The byte count the store enforces: é is 2 bytes, 漢 3, a 1, and a typed % 1.
			await Assert.That(await Eval($"u({installed.CreatedObjects["achievements"]}/FUN`BYTES,é漢a%%)")).IsEqualTo("7");

			var holder = await Eval($"objid(#{playerDbref})");
			var revoked = await ActAsync(staff, new { op = "revoke", plaque = number, holder });
			await Assert.That(revoked["message"]!.GetValue<string>()).IsEqualTo($"{playerName} no longer holds {plaque}.");

			var destroyed = await ActAsync(staff, new { op = "destroy", plaque = number });
			await Assert.That(destroyed["redirect"]!.GetValue<string>()).IsEqualTo("/apps/achievements");
			var gone = await ActAsync(staff, new { op = "destroy", plaque = number });
			await Assert.That(gone["errors"]!["_global"]!.GetValue<string>()).IsEqualTo("That plaque is gone.");
		}
		finally
		{
			await UninstallAsync();
		}
	}
}
