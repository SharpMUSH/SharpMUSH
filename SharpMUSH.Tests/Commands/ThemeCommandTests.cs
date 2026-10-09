using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary><c>@theme</c>: a player's own theme for every layout they read.</summary>
public class ThemeCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>A new player in a room of their own, what they heard while <paramref name="command"/> ran, and their THEME after it.</summary>
	private async Task<(List<string> Heard, string Theme)> AsPlayer(string command)
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ThemeRoom"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Themer", home);
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		var heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
		var theme = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({player.DbRef}/THEME)")))!.Message.ToPlainText();
		return (heard, theme);
	}

	[Test]
	public async Task Theme_KeepsWhatThePlayerChose()
	{
		var (heard, theme) = await AsPlayer("@theme me=fantasy");

		await Assert.That(heard).Contains("Theme set.");
		await Assert.That(theme).IsEqualTo("fantasy");
	}

	[Test]
	public async Task Theme_TakesJsonAsWritten()
	{
		// Kept as typed; evaluating it takes the outer braces off, as for a layout function's options.
		var (_, theme) = await AsPlayer("@theme me={{\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}}");

		await Assert.That(theme).IsEqualTo("{{\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}}");
	}

	[Test]
	public async Task Theme_Light_MakesItForALightBackground()
	{
		var (_, named) = await AsPlayer("@theme/light me=fantasy");
		var (_, written) = await AsPlayer("@theme/light me={{\"seed\":\"#7aa2f7\"}}");

		await Assert.That(named).IsEqualTo("{{\"preset\":\"fantasy\",\"mode\":\"light\"}}");
		await Assert.That(written).IsEqualTo("{{\"preset\":{\"seed\":\"#7aa2f7\"},\"mode\":\"light\"}}");
	}

	/// <summary>A new player in a room of their own, parented to a thing they own, which is returned too.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Player, DBRef Parent)> PlayerWithParent()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ThemeRoom"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Themer", home);
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		var room = (await Mediator.Send(new GetObjectNodeQuery(home))).Expect<SharpRoom>();
		var parent = await Mediator.Send(new CreateThingCommand(TestIsolationHelpers.GenerateUniqueName("Faction"), room, owner, room));
		await Run(player, $"@parent me={parent}");
		return (player, parent);
	}

	private async Task Run(TestIsolationHelpers.TestPlayer player, string command) =>
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));

	/// <summary>What <c>@theme/refresh</c> tells <paramref name="player"/>.</summary>
	private async Task<string> InUse(TestIsolationHelpers.TestPlayer player)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await Run(player, "@theme/refresh");
		return WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).Last();
	}

	[Test]
	public async Task Theme_IsInheritedFromAParent()
	{
		var (player, parent) = await PlayerWithParent();
		await Run(player, $"@theme {parent}=nord");

		await Assert.That(await InUse(player)).IsEqualTo("Theme in use: nord");
	}

	[Test]
	public async Task Theme_OfTheirOwn_WinsUntilCleared()
	{
		var (player, parent) = await PlayerWithParent();
		await Run(player, $"@theme {parent}=nord");
		await Run(player, "@theme me=fantasy");
		var own = await InUse(player);
		await Run(player, "@theme me=");

		await Assert.That(own).IsEqualTo("Theme in use: fantasy");
		await Assert.That(await InUse(player)).IsEqualTo("Theme in use: nord");
	}

	[Test]
	public async Task Theme_IsEvaluatedAsThePlayer()
	{
		var (player, parent) = await PlayerWithParent();
		await Run(player, $"@theme {parent}=[if(strmatch(get(%#/FACTION),Rebel),horror,nord)]");
		var before = await InUse(player);
		await Run(player, "&FACTION me=Rebel");

		await Assert.That(before).IsEqualTo("Theme in use: nord");
		await Assert.That(await InUse(player)).IsEqualTo("Theme in use: horror");
	}

	[Test]
	public async Task Theme_WorkingOutToNothing_IsTheGamesTheme()
	{
		var (player, parent) = await PlayerWithParent();
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await Run(player, $"@theme {parent}=[switch(get(%#/FACTION),Rebel,horror)]");
		var told = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
		var unaligned = await InUse(player);
		await Run(player, "&FACTION me=Rebel");

		await Assert.That(told).Contains("Theme set.");
		await Assert.That(unaligned).IsEqualTo(ErrorMessages.Notifications.ThemeNoneInUse);
		await Assert.That(await InUse(player)).IsEqualTo("Theme in use: horror");
	}

	[Test]
	public async Task Theme_InAModeWorkingOutToNothing_IsRefused()
	{
		var (player, _) = await PlayerWithParent();
		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await Run(player, "@theme/light me=[switch(get(%#/FACTION),Rebel,horror)]");
		var told = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();

		await Assert.That(told).Contains(ErrorMessages.Notifications.ThemeModeNeedsTheme);
		await Assert.That(await InUse(player)).IsEqualTo(ErrorMessages.Notifications.ThemeNoneInUse);
	}

	[Test]
	public async Task Theme_SetBeforeTheParent_IsInherited()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ThemeRoom"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Themer", home);
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<SharpPlayer>();
		var room = (await Mediator.Send(new GetObjectNodeQuery(home))).Expect<SharpRoom>();
		var parent = await Mediator.Send(new CreateThingCommand(TestIsolationHelpers.GenerateUniqueName("Faction"), room, owner, room));
		await Run(player, $"@theme {parent}=nord");
		var unparented = await InUse(player);
		await Run(player, $"@parent me={parent}");

		await Assert.That(unparented).IsEqualTo(ErrorMessages.Notifications.ThemeNoneInUse);
		await Assert.That(await InUse(player)).IsEqualTo("Theme in use: nord");
	}

	[Test]
	public async Task Theme_WithNoneSet_SaysSo()
	{
		var (player, _) = await PlayerWithParent();

		await Assert.That(await InUse(player)).IsEqualTo(ErrorMessages.Notifications.ThemeNoneInUse);
	}

	[Test]
	public async Task ChangingTheGamesThemes_NeedsLayoutAdmin()
	{
		var (heard, _) = await AsPlayer("@theme/disable nord");

		await Assert.That(heard).Contains(ErrorMessages.Notifications.PermissionDenied);
	}

	[Test]
	public async Task AnAddedTheme_CanBeChosen_UntilRemoved()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("mytheme").ToLowerInvariant().Replace('_', '-');
		var connections = ConnectionService;
		await WebAppFactoryArg.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@theme/add {name}={{{{\"preset\":\"nord\",\"colors\":{{\"primary\":\"#bf616a\"}}}}}}"));
		try
		{
			await Assert.That((await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain("themes()")))!.Message.ToPlainText()).EndsWith(name);
			var (player, _) = await PlayerWithParent();
			await Run(player, $"@theme me={name}");
			var inUse = await InUse(player);

			await Assert.That(inUse).StartsWith("Theme in use: {");
			await Assert.That(inUse).Contains($"\"name\":\"{name}\"");
			await Assert.That((await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"json_query(theme({name}),get,colors,primary,rgb)")))!.Message.ToPlainText())
				.IsEqualTo("\"#bf616a\"");
		}
		finally
		{
			await WebAppFactoryArg.CommandParser.CommandParse(1, connections, MarkupText.Plain($"@theme/remove {name}"));
		}

		await Assert.That((await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"theme({name})")))!.Message.ToPlainText())
			.IsEqualTo("#-1 UNKNOWN THEME");
	}

	[Test]
	public async Task Theme_RefusesOneItCannotRead()
	{
		var (heard, theme) = await AsPlayer("@theme me=nowhere");

		await Assert.That(heard).Contains("#-1 UNKNOWN THEME");
		await Assert.That(theme).IsEmpty();
	}

	[Test]
	public async Task Theme_Empty_ClearsIt()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ThemeRoom"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Themer", home);
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@theme me=nord"));
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("@theme me="));

		await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef)).Contains("Theme cleared.");
		await Assert.That((await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({player.DbRef}/THEME)")))!.Message.ToPlainText()).IsEmpty();
	}

	[Test]
	public async Task Login_WithAThemeThatDoesNotRead_SaysSo()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ThemeRoom"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Themer", home);
		// Set by hand, so @theme never saw it.
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&THEME me={{\"look\":{\"bullet\":5}}}"));

		var handle = await TestIsolationHelpers.RegisterTestHandleAsync(ConnectionService);
		try
		{
			await WebAppFactoryArg.CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect {player.Name} {TestIsolationHelpers.TestPassword}"));

			await Assert.That(WebAppFactoryArg.Notifications.ForHandle(handle))
				.Contains("Your @theme does not read (#-1 INVALID THEME: bullet is text), so layouts use the game's theme. @theme me=<theme> sets another; @theme me= clears it.");
		}
		finally
		{
			await ConnectionService.Disconnect(handle);
		}
	}

	[Test]
	[Arguments("\ud800")]
	[Arguments("{\"look\":{\"bullet\":\"\\ud800\"}}")]
	public async Task ATheme_ThatCannotBeText_IsRefusedNotThrown(string spec)
		=> await Assert.That(LayoutThemes.Read(spec) is Error<string>).IsTrue();

	[Test]
	[Arguments("\ud800")]
	[Arguments("{\"preset\":\"\\ud800\"}")]
	public async Task TheGamesThemes_RefuseATheme_ThatCannotBeText(string spec)
		=> await Assert.That(WebAppFactoryArg.Services.GetRequiredService<ILayoutThemeService>().Read(spec) is Error<string>).IsTrue();

	/// <summary>Every <c>&gt; @theme</c> example in the help, with the line under it as what the player is told.</summary>
	public static IEnumerable<Func<(string Command, string Told)>> HelpExamples()
	{
		var lines = File.ReadAllLines(Path.Join(TestPaths.Helpfiles.FullName, "theme-command.md"))
			.SkipWhile(line => line != "# @theme").Skip(1).TakeWhile(line => line != "## The game's themes").ToArray();
		for (var i = 0; i < lines.Length - 1; i++)
		{
			if (!lines[i].StartsWith("> @theme", StringComparison.Ordinal)) continue;
			var (command, told) = (lines[i][2..], lines[i + 1]);
			yield return () => (command, told);
		}
	}

	[Test]
	[MethodDataSource(nameof(HelpExamples))]
	public async Task TheHelpExamplesSayWhatTheyDo(string command, string told)
	{
		var (heard, _) = await AsPlayer(command);

		await Assert.That(heard).Contains(told);
	}
}
