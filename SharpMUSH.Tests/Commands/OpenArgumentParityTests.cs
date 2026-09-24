using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>do_open</c> (<c>src/create.c:205-237</c>) reads a 1-based <c>links</c> array, and SharpMUSH's
/// argument index N is PennMUSH's <c>links[N]</c>: <c>links[1]</c> destination, <c>links[2]</c> return
/// exit (<c>:229-236</c>), <c>links[3]</c> source room (<c>:210-217</c>). SharpMUSH read argument 2 as
/// the source room, so <c>@open north=#10,south</c> tried to open from a room called <c>south</c>
/// instead of opening the exit back — and no <c>@open</c> anywhere could open a return exit.
/// <para><c>fun_open</c> (<c>src/fundb.c:2144-2173</c>) is a different mapping and has no return exit
/// at all: exit, destination, source room, requested dbref.</para>
/// </summary>
public class OpenArgumentParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> Run(long handle, string command)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain(command)))?.Message?.ToPlainText()
			?? string.Empty;

	private Task<string> AsGod(string command) => Run(1, command);

	private static DBRef Ref(string reported) => DBRef.Parse(reported.Trim());

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	private async Task<string[]> Named(string name)
		=> (await AsGod($"think lsearch(all,name,{name})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);

	/// <summary>The room an exit is sourced in — SharpMUSH's <c>Location</c>, PennMUSH's <c>Home</c>.</summary>
	private async Task<int> SourceOf(DBRef exit)
		=> (await (await Node(exit)).Expect<SharpExit>().Location.WithCancellation(CancellationToken.None))
			.Object().DBRef.Number;

	/// <summary>Where an exit leads — SharpMUSH's <c>Home</c>, PennMUSH's <c>Location</c>.</summary>
	private async Task<int?> DestinationOf(DBRef exit)
		=> AnyOptionalSharpContainer.RefOf(
			await (await Node(exit)).Expect<SharpExit>().Home.WithCancellation(CancellationToken.None))?.Number;

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

	/// <summary>
	/// create.c:229-236 — <c>links[2]</c> names an exit opened back from the destination to the source,
	/// which is what <c>help @open</c> has always documented.
	/// </summary>
	[Test]
	public async ValueTask TheSecondArgumentOpensAnExitBack()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, home) = await BuilderAsync("OapBack", uid);
		var destination = Ref(await AsGod($"@dig OapBackDest{uid}"));
		await AsGod($"@chown {destination}={mortal.DbRef}");

		var forward = Ref(await Run(mortal.Handle, $"@open OapBackTo{uid}={destination},OapBackFrom{uid}"));

		await Assert.That(await SourceOf(forward)).IsEqualTo(home.Number);
		await Assert.That(await DestinationOf(forward)).IsEqualTo(destination.Number);

		var back = await Named($"OapBackFrom{uid}");
		await Assert.That(back.Length).IsEqualTo(1)
			.Because("create.c:236 opens a second exit when links[2] is given");
		await Assert.That(await SourceOf(Ref(back[0]))).IsEqualTo(destination.Number)
			.Because("the return exit is sourced in Location(forward), the room the forward exit leads to");
		await Assert.That(await DestinationOf(Ref(back[0]))).IsEqualTo(home.Number)
			.Because("create.c:236 links the return exit to the forward exit's source");
	}

	/// <summary>create.c:210-217 — <c>links[3]</c>, the third argument, is the source room.</summary>
	[Test]
	public async ValueTask TheThirdArgumentIsTheSourceRoom()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, home) = await BuilderAsync("OapSource", uid);
		var elsewhere = Ref(await AsGod($"@dig OapSourceElsewhere{uid}"));
		await AsGod($"@chown {elsewhere}={mortal.DbRef}");
		var destination = Ref(await AsGod($"@dig OapSourceDest{uid}"));

		var exit = Ref(await Run(mortal.Handle, $"@open OapSourceTo{uid}={destination},,{elsewhere}"));

		await Assert.That(await SourceOf(exit)).IsEqualTo(elsewhere.Number)
			.Because("links[3] names where the exit is opened");
		await Assert.That(await SourceOf(exit)).IsNotEqualTo(home.Number);
	}

	/// <summary>
	/// <c>do_real_open</c> gates on <c>can_open_from</c> (<c>create.c:127</c>; <c>hdrs/mushdb.h:94</c>),
	/// not on control: OPEN_OK plus the room's <c>@lock/open</c> is enough. SharpMUSH asked
	/// <c>Controls</c> alone, so an OPEN_OK room admitted nobody but its owner.
	/// </summary>
	[Test]
	public async ValueTask AnOpenOkRoomAdmitsABuilderWhoDoesNotControlIt()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "OapOpenOk");
		var room = Ref(await AsGod($"@dig OapOpenOkRoom{uid}"));
		await AsGod($"@set {room}=OPEN_OK");
		await AsGod($"@teleport {mortal.DbRef}={room}");

		var exit = await Run(mortal.Handle, $"@open OapOpenOkExit{uid}");

		await Assert.That(exit.StartsWith("#-")).IsFalse()
			.Because("mushdb.h:94 lets OPEN_OK plus a passed @lock/open source an exit");
		await Assert.That(await SourceOf(Ref(exit))).IsEqualTo(room.Number);
	}

	/// <summary>
	/// The return exit is held to the same standard as the forward one, in the room it is sourced in —
	/// <c>do_open</c> reaches it through a second <c>do_real_open</c> (<c>create.c:236</c>). A builder
	/// who may open at home but not at the far end gets the forward exit and no return.
	/// </summary>
	[Test]
	public async ValueTask AReturnExitIsRefusedWhereTheBuilderMayNotOpen()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, home) = await BuilderAsync("OapRefused", uid);
		// LINK_OK but not OPEN_OK: the forward exit may lead here (can_link_to), and no exit may be
		// sourced here (can_open_from). That is exactly the pair the return exit is gated on.
		var destination = Ref(await AsGod($"@dig OapRefusedDest{uid}"));
		await AsGod($"@set {destination}=LINK_OK");

		var forward = Ref(await Run(mortal.Handle, $"@open OapRefusedTo{uid}={destination},OapRefusedFrom{uid}"));

		await Assert.That(await SourceOf(forward)).IsEqualTo(home.Number);
		await Assert.That(await DestinationOf(forward)).IsEqualTo(destination.Number)
			.Because("the forward exit is unaffected by the return exit's refusal");
		await Assert.That((await Named($"OapRefusedFrom{uid}")).Length).IsEqualTo(0)
			.Because("can_open_from refuses the far room, which the builder neither controls nor may open in");
	}

	/// <summary>
	/// <c>fun_open</c>'s third argument is the source room, and a name that matches nothing there is
	/// <c>#-1 INVALID SOURCE ROOM</c> (<c>fundb.c:2160-2166</c>). SharpMUSH ignored arguments 1-3
	/// entirely and always opened, unlinked, where the caller stood.
	/// </summary>
	[Test]
	public async ValueTask TheFunctionReadsDestinationAndSourceRoom()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, home) = await BuilderAsync("OapFn", uid);
		var elsewhere = Ref(await AsGod($"@dig OapFnElsewhere{uid}"));
		await AsGod($"@chown {elsewhere}={mortal.DbRef}");
		var destination = Ref(await AsGod($"@dig OapFnDest{uid}"));
		await AsGod($"@set {destination}=LINK_OK");

		var exit = Ref(await Run(mortal.Handle, $"think open(OapFnTo{uid},{destination},{elsewhere})"));

		await Assert.That(await SourceOf(exit)).IsEqualTo(elsewhere.Number);
		await Assert.That(await SourceOf(exit)).IsNotEqualTo(home.Number);
		await Assert.That(await DestinationOf(exit)).IsEqualTo(destination.Number)
			.Because("fun_open links the exit to its second argument");
	}

	/// <summary>The other half of fundb.c:2160-2166: a source room that matches nothing builds nothing.</summary>
	[Test]
	public async ValueTask TheFunctionReportsAnInvalidSourceRoom()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var (mortal, _) = await BuilderAsync("OapFnBad", uid);

		var name = $"OapFnBadExit{uid}";
		await Assert.That(await Run(mortal.Handle, $"think open({name},,OapFnNoSuchRoom{uid})"))
			.IsEqualTo(ErrorMessages.Returns.InvalidSourceRoom);
		await Assert.That((await Named(name)).Length).IsEqualTo(0);
	}
}
