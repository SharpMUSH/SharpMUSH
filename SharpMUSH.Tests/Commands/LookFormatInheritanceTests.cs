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

	/// <summary>
	/// The look formats read their values by name as well as by position: @nameformat's %&lt;object&gt; and
	/// %&lt;name&gt;, @descformat's %&lt;description&gt;, @conformat's %&lt;contents&gt; and %&lt;names&gt;, and
	/// @exitformat's %&lt;exits&gt;.
	/// </summary>
	[Test]
	public async Task LookFormats_ReadArgumentsByName()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("nfmt");
		var (player, parser, _) = await MortalInOwnRoom(token);

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create Thing_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop Thing_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@open Exit_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@describe here=Desc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&NAMEFORMAT here=NNAME_{token}:[name(%<object>)]:[strmatch(%<name>,%1)]"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&DESCFORMAT here=NDESC_{token}:%<description>"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&CONFORMAT here=NCON_{token}:[name(%<contents>)]:%<names>"));
		await parser.CommandParse(player.Handle, ConnectionService,
			MarkupText.Plain($"&EXITFORMAT here=NEXIT_{token}:[name(%<exits>)]"));

		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef);
		await Assert.That(heard).Contains($"NNAME_{token}:Room_{token}:1");
		await Assert.That(heard).Contains($"NDESC_{token}:Desc_{token}");
		await Assert.That(heard).Contains($"NCON_{token}:Thing_{token}:Thing_{token}");
		await Assert.That(heard).Contains($"NEXIT_{token}:Exit_{token}");
	}

	[Test]
	[NotInParallel]
	public async Task AncestorRoomNameFormat_AppliesToAMortalsLook()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("afmt");
		var (player, parser, _) = await MortalInOwnRoom(token);

		// Every room in the shared world inherits this; it only answers for this test's room, and
		// anywhere else gives back the default name.
		var original = (await GodParser.EvaluateAsync(MarkupText.Plain($"[get({AncestorRoom}/NAMEFORMAT)]"))).ToPlainText();
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}=[if(strmatch(name(%0),Room_{token}),ANAME_{token}:[name(%0)],%1)]"));
		try
		{
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

			await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef))
				.Contains($"ANAME_{token}:Room_{token}");
		}
		finally
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}={original}"));
		}
	}

	[Test]
	[NotInParallel]
	public async Task OwnNameFormat_ShadowsTheAncestors()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("ofmt");
		var (player, parser, _) = await MortalInOwnRoom(token);

		var original = (await GodParser.EvaluateAsync(MarkupText.Plain($"[get({AncestorRoom}/NAMEFORMAT)]"))).ToPlainText();
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}=[if(strmatch(name(%0),Room_{token}),ANAME_{token},%1)]"));
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
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}={original}"));
		}
	}

	/// <summary>
	/// PennMUSH shows its built-in look for a halted room, and a room is halted by @chown. The Ancestor
	/// Room's formats are SharpMUSH's built-in look, so a halted room shows the default name and its
	/// description instead of that code. Its own format still shows unevaluated, as in PennMUSH, where a
	/// halted executor evaluates nothing (<c>parse.c:2092</c>).
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task HaltedRoom_ShowsTheBuiltInLookInsteadOfTheAncestorsCode()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("halt");
		var (player, parser, room) = await MortalInOwnRoom(token);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@describe here=Desc_{token}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {room}=HALT"));

		var original = (await GodParser.EvaluateAsync(MarkupText.Plain($"[get({AncestorRoom}/NAMEFORMAT)]"))).ToPlainText();
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}=[if(strmatch(name(%0),Room_{token}),ANAME_{token})]"));
		try
		{
			var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

			var heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
			await Assert.That(heard).Contains((string line) => line.StartsWith($"Room_{token}(", StringComparison.Ordinal));
			await Assert.That(heard).Contains((string line) => line.Contains($"Desc_{token}", StringComparison.Ordinal));
			await Assert.That(heard).DoesNotContain((string line) => line.Contains($"ANAME_{token}", StringComparison.Ordinal));
			await Assert.That(heard).DoesNotContain((string line) => line.Contains("localize(", StringComparison.Ordinal));

			// The same formats reached through an @parent set to the Ancestor Room are still the built-in look.
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {room}={AncestorRoom}"));
			before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

			heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
			await Assert.That(heard).Contains((string line) => line.StartsWith($"Room_{token}(", StringComparison.Ordinal));
			await Assert.That(heard).DoesNotContain((string line) => line.Contains($"ANAME_{token}", StringComparison.Ordinal));

			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&NAMEFORMAT here=OWN_{token}:[name(%0)]"));
			before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
			await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

			await Assert.That(WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before))
				.Contains($"OWN_{token}:[name(%0)]");
		}
		finally
		{
			await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&NAMEFORMAT {AncestorRoom}={original}"));
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
		await Assert.That(heard).Contains((string line) => line.Contains($"Room_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains("Contents:", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains(player.Name, StringComparison.Ordinal));
	}

	/// <summary>
	/// <c>look_exits</c> and <c>look_contents</c> (<c>look.c</c>) call EXITFORMAT and CONFORMAT before they
	/// count anything, so a room with nothing to list still shows what its formats say.
	/// </summary>
	[Test]
	public async Task Look_RunsTheFormatsOfAnEmptyRoom()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("empty");
		var (player, parser, _) = await MortalInOwnRoom(token);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&EXITFORMAT here=NOEXITS_{token}:[words(%0)]"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"&CONFORMAT here=NOCONTENTS_{token}:[words(%0)]"));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
		await Assert.That(heard).Contains($"NOEXITS_{token}:0");
		await Assert.That(heard).Contains($"NOCONTENTS_{token}:0");
	}

	/// <summary>
	/// <c>fetch_ufun_attrib(..., UFUN_REQUIRE_ATTR)</c> falls back only when the format is missing: one that
	/// evaluates to nothing, or is stored empty, shows nothing in place of the name, contents or exits.
	/// </summary>
	[Test]
	public async Task EmptyFormats_ShowNothing()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("nil");
		var (player, parser, _) = await MortalInOwnRoom(token);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@create Thing_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"drop Thing_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@open Exit_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@describe here=Desc_{token}"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&NAMEFORMAT here=[null(%0)]"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&CONFORMAT here=[null(%0)]"));
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&EXITFORMAT here="));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
		await Assert.That(heard).Contains((string line) => line.Contains($"Desc_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains($"Room_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains($"Thing_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains($"Exit_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Length == 0);
	}

	/// <summary>Without a format, an empty room lists no headings, and a format that says nothing shows nothing.</summary>
	[Test]
	public async Task Look_ShowsNoHeadingsForAnEmptyRoom()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("bare");
		var (player, parser, _) = await MortalInOwnRoom(token);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("&EXITFORMAT here=[null(%0)]"));

		var before = WebAppFactoryArg.Notifications.CountFor(player.DbRef);
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look"));

		var heard = WebAppFactoryArg.Notifications.For(player.DbRef).Skip(before).ToList();
		await Assert.That(heard).Contains((string line) => line.Contains($"Room_{token}", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains("Obvious exits:", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains("Contents:", StringComparison.Ordinal));
		await Assert.That(heard).DoesNotContain((string line) => line.Length == 0);
	}
}
