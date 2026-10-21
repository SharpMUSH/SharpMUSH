using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH's process_command (<c>game.c:1258</c>): a line that matches the EALIAS of something in the
/// room enters it, and one that matches the location's LALIAS leaves it.
/// </summary>
public class MoveAliasTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<(TestIsolationHelpers.TestPlayer Player, IMUSHCodeParser Parser, DBRef Room, DBRef Box)>
		PlayerBesideABox(string token)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MoveAlias");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var dig = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig Room_{token}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={room}"));
		var created = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create Box_{token}"));
		var box = DBRef.Parse(created.Message.ToPlainText().Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop {box}"));
		return (player, parser, room, box);
	}

	private async Task<int> LocationOf(IMUSHCodeParser parser, DBRef who)
		=> int.Parse((await parser.FunctionParse(MarkupText.Plain($"[loc(#{who.Number})]")))!.Message!
			.ToPlainText().TrimStart('#').Split(':')[0]);

	[Test]
	public async Task EnterAlias_EntersAndLeaveAlias_Leaves()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ealias");
		var (player, parser, room, box) = await PlayerBesideABox(token);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&EALIAS {box}=climb in;EIN_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&LALIAS {box}=LOUT_{token};climb out"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"ein_{token}"));
		await Assert.That(await LocationOf(parser, player.DbRef)).IsEqualTo(box.Number);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("climb out"));
		await Assert.That(await LocationOf(parser, player.DbRef)).IsEqualTo(room.Number);
	}

	[Test]
	public async Task AliasMustMatchAWholeWord()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ealiasword");
		var (player, parser, room, box) = await PlayerBesideABox(token);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&EALIAS {box}=EIN_{token}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"EIN_{token} now"));
		await Assert.That(await LocationOf(parser, player.DbRef)).IsEqualTo(room.Number);
	}
}
