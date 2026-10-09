using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What a builder is told when an exit is linked and unlinked, and how a new exit's destination is
/// read. Two PennMUSH routines link an exit and they report differently: <c>do_link</c> names the
/// destination through <c>unparse_object</c> (<c>src/create.c:385-386</c>), <c>do_real_open</c> prints
/// both dbrefs bare (<c>:175</c>). <c>do_real_open</c>'s destination goes through
/// <c>parse_linkable_room</c> (<c>:40-66</c>), which reads <c>here</c>, <c>home</c>, <c>variable</c>
/// or a dbref and nothing else, and <c>@open</c>, <c>open()</c> and <c>@dig</c> all share it.
/// </summary>
public class ExitLinkReportTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Run(long handle, string command)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message.ToPlainText()
			?? string.Empty;

	private Task<string> AsGod(string command) => Run(1, command);

	private static DBRef Ref(string reported) => DBRef.Parse(reported.Trim());

	private async Task<int?> DestinationOf(DBRef exit)
		=> AnyOptionalSharpContainer.RefOf(await (await Mediator.Send(new GetObjectNodeQuery(exit)))
			.Expect<SharpExit>().Home.WithCancellation(CancellationToken.None))?.Number;

	/// <summary>A mortal standing in a room of their own, which is what <c>can_open_from</c> wants.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Player, DBRef Room)> BuilderAsync(string prefix, string uid)
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);
		var room = Ref(await AsGod($"@dig {prefix}Home{uid}"));
		await AsGod($"@chown {room}={mortal.DbRef}");
		await AsGod($"@teleport {mortal.DbRef}={room}");
		return (mortal, room);
	}

	/// <summary><c>do_unlink</c> (<c>src/create.c:275-276</c>) says where the exit used to lead.</summary>
	[Test]
	public async ValueTask UnlinkSaysWhereTheExitUsedToLead()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("ElrUnlink", uid);
		var destination = Ref(await Run(mortal.Handle, $"@dig ElrUnlinkDest{uid}"));
		var exit = Ref(await Run(mortal.Handle, $"@open ElrUnlinkExit{uid}={destination}"));

		await Run(mortal.Handle, $"@unlink {exit}");

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef,
			$"Unlinked exit #{exit.Number} (Used to lead to ElrUnlinkDest{uid}(#{destination.Number}R");
		await Assert.That(await DestinationOf(exit)).IsNull();
	}

	/// <summary>An exit linked to HOME used to lead to <c>*HOME*</c>, <c>unparse_object</c>'s word for it.</summary>
	[Test]
	public async ValueTask UnlinkingAHomeExitNamesHome()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("ElrUnlinkHome", uid);
		var exit = Ref(await Run(mortal.Handle, $"@open ElrUnlinkHomeExit{uid}"));
		await Run(mortal.Handle, $"@link {exit}=home");

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef, $"Linked exit #{exit.Number} to *HOME*");

		await Run(mortal.Handle, $"@unlink {exit}");

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef,
			$"Unlinked exit #{exit.Number} (Used to lead to *HOME*).");
	}

	/// <summary><c>@link &lt;exit&gt;=variable</c> is <c>check_var_link</c>'s AMBIGUOUS, which unparses as <c>*VARIABLE*</c>.</summary>
	[Test]
	public async ValueTask LinkingToVariableNamesVariable()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("ElrVariable", uid);
		var exit = Ref(await Run(mortal.Handle, $"@open ElrVariableExit{uid}"));

		await Run(mortal.Handle, $"@link {exit}=variable");

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef, $"Linked exit #{exit.Number} to *VARIABLE*");
	}

	/// <summary>
	/// <c>parse_linkable_room</c> reads no names: <c>@open</c> and <c>open()</c> both keep the exit,
	/// unlinked, and both say so the same way.
	/// </summary>
	[Test]
	[Arguments("@open {0}={1}")]
	[Arguments("think open({0},{1})")]
	public async ValueTask ANamedDestinationIsNotAValidObject(string template)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("ElrNamed", uid);
		var destinationName = $"ElrNamedDest{uid}";
		await Run(mortal.Handle, $"@dig {destinationName}");

		var exit = Ref(await Run(mortal.Handle, string.Format(template, $"ElrNamedExit{uid}", destinationName)));

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef, "That is not a valid object.");
		await Assert.That(await DestinationOf(exit)).IsNull();
	}

	/// <summary>
	/// <c>do_real_open</c> links a new exit to HOME and prints <c>Location(new_exit)</c> as a bare
	/// dbref, so <c>#-3</c>; <c>loc()</c> then answers HOME (<c>#-3</c>) too.
	/// </summary>
	[Test]
	[Arguments("@open {0}=home")]
	[Arguments("think open({0},home)")]
	public async ValueTask OpeningToHomeLinksToHome(string template)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("ElrOpenHome", uid);

		var exit = Ref(await Run(mortal.Handle, string.Format(template, $"ElrOpenHomeExit{uid}")));

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef, $"Linked exit #{exit.Number} to #-3");
		await Assert.That(await Run(mortal.Handle, $"think loc({exit})")).StartsWith("#-3");
	}

	/// <summary><c>here</c> is <c>speech_loc(player)</c>, the opener's own room.</summary>
	[Test]
	public async ValueTask OpeningToHereLinksToTheOpenersRoom()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, home) = await BuilderAsync("ElrOpenHere", uid);

		var exit = Ref(await Run(mortal.Handle, $"@open ElrOpenHereExit{uid}=here"));

		await Assert.That(await DestinationOf(exit)).IsEqualTo(home.Number);
		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef, $"Linked exit #{exit.Number} to #{home.Number}");
	}

	/// <summary>
	/// <c>do_open</c>'s source room is <c>match_result(TYPE_ROOM, MAT_HERE | MAT_ABSOLUTE | MAT_TYPE)</c>
	/// (<c>src/create.c:211-216</c>), so a name builds nothing and says "Open from where?".
	/// </summary>
	[Test]
	public async ValueTask ANamedSourceRoomIsOpenFromWhere()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("ElrSource", uid);
		var sourceName = $"ElrSourceRoom{uid}";
		await Run(mortal.Handle, $"@dig {sourceName}");

		var exitName = $"ElrSourceExit{uid}";
		await Run(mortal.Handle, $"@open {exitName}=,,{sourceName}");

		await WebAppFactoryArg.Notifications.WaitForAsync(mortal.DbRef, "Open from where?");
		await Assert.That(await AsGod($"think lsearch(all,name,{exitName})")).IsEqualTo(string.Empty);
	}
}
