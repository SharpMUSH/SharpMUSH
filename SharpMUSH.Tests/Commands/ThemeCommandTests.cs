using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
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
		var theme = (await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({player.DbRef}/THEME)")))!.Message!.ToPlainText();
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
		// The outer braces only keep the argument together, as they do for a layout function's options.
		var (_, theme) = await AsPlayer("@theme me={{\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}}");

		await Assert.That(theme).IsEqualTo("{\"seed\":\"#7aa2f7\",\"harmony\":\"triadic\"}");
	}

	[Test]
	public async Task Theme_Light_MakesItForALightBackground()
	{
		var (_, named) = await AsPlayer("@theme/light me=fantasy");
		var (_, written) = await AsPlayer("@theme/light me={{\"seed\":\"#7aa2f7\"}}");

		await Assert.That(named).IsEqualTo("{\"preset\":\"fantasy\",\"mode\":\"light\"}");
		await Assert.That(written).IsEqualTo("{\"preset\":{\"seed\":\"#7aa2f7\"},\"mode\":\"light\"}");
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
		await Assert.That((await WebAppFactoryArg.FunctionParser.FunctionParse(MarkupText.Plain($"get({player.DbRef}/THEME)")))!.Message!.ToPlainText()).IsEmpty();
	}

	[Test]
	public async Task Login_WithAThemeThatDoesNotRead_SaysSo()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>().Expect<SharpPlayer>();
		var home = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("ThemeRoom"), god));
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Themer", home);
		// Set by hand, so @theme never saw it.
		await WebAppFactoryArg.CommandParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&THEME me={\"look\":{\"bullet\":5}}"));

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
}
