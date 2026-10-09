using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH fetches the look formats with <c>fetch_ufun_attrib(..., UFUN_IGNORE_PERMS |
/// UFUN_REQUIRE_ATTR)</c> (<c>unparse.c:185</c>, <c>look.c:99</c>, <c>look.c:241</c>), which reads the
/// attribute through <c>atr_get</c>: the room, its @parent chain, then the room ancestor. The looker's
/// permissions play no part, so a mortal sees a format set on a wizard-owned parent or ancestor.
/// </summary>
public class LookFormatInheritanceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private static readonly DBRef AncestorRoom = new(3);

	private async Task<(TestIsolationHelpers.TestPlayer Player, IMUSHCodeParser Parser, DBRef Room)> MortalInOwnRoom(
		string token)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "LookFmt");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var dig = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig Room_{token}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={room}"));
		return (player, parser, room);
	}

	[Test]
	public async Task ParentFormats_ApplyToAMortalsLook()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("pfmt");
		var (player, parser, room) = await MortalInOwnRoom(token);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create Thing_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop Thing_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@open Exit_{token}"));

		// A God-owned parent the mortal can neither read nor evaluate.
		var created = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@create Parent_{token}"));
		var parent = DBRef.Parse(created.Message.ToPlainText().Trim());
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&NAMEFORMAT {parent}=PNAME_{token}:[name(%0)]"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&CONFORMAT {parent}=PCON_{token}:[words(%0)]"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&EXITFORMAT {parent}=PEXIT_{token}:[words(%0)]"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {room}={parent}"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(heard).Contains($"PNAME_{token}:Room_{token}");
		await Assert.That(heard).Contains($"PCON_{token}:1");
		await Assert.That(heard).Contains($"PEXIT_{token}:1");
	}

	[Test]
	[NotInParallel]
	public async Task AncestorRoomNameFormat_AppliesToAMortalsLook()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("afmt");
		var (player, parser, _) = await MortalInOwnRoom(token);

		// Every room in the shared world inherits this; it only answers for this test's room, and
		// anywhere else evaluates to nothing, which leaves the default name in place.
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}=[if(strmatch(name(%0),Room_{token}),ANAME_{token}:[name(%0)])]"));
		try
		{
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

			await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef))
				.Contains($"ANAME_{token}:Room_{token}");
		}
		finally
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}="));
		}
	}

	[Test]
	[NotInParallel]
	public async Task OwnNameFormat_ShadowsTheAncestors()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ofmt");
		var (player, parser, _) = await MortalInOwnRoom(token);

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}=[if(strmatch(name(%0),Room_{token}),ANAME_{token})]"));
		try
		{
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&NAMEFORMAT here=OWN_{token}"));
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

			var heard = WebAppFactoryArg.Notifications.For(player.DbRef);
			await Assert.That(heard).Contains($"OWN_{token}");
			await Assert.That(heard).DoesNotContain($"ANAME_{token}");
		}
		finally
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}="));
		}
	}

	/// <summary>
	/// <c>can_see</c> (<c>predicat.c:338-344</c>): "your own body isn't listed in a 'look'", so a player
	/// alone in a room sees no Contents section at all.
	/// </summary>
	[Test]
	public async Task Look_DoesNotListTheLooker()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("self");
		var (player, parser, _) = await MortalInOwnRoom(token);

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
		await Assert.That(heard).Contains((string line) => line.StartsWith($"Room_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains("Contents:", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains(player.Name, StringComparison.Ordinal));
	}
}
