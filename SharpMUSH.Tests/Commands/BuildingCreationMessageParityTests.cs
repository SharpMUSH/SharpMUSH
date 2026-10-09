using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// What a builder is told by <c>@open</c>, <c>open()</c> and <c>pcreate()</c>, against PennMUSH's own
/// words: <c>do_real_open</c> (<c>src/create.c:161-175</c>) and <c>do_pcreate</c>
/// (<c>src/wiz.c:140-144</c>), which the functions reach unchanged (<c>src/fundb.c:2140</c>, <c>:2171</c>).
/// </summary>
public class BuildingCreationMessageParityTests
{
	/// <summary>The event handler object (<c>event_handler</c>), where <c>PLAYER`CREATE</c> is looked up.</summary>
	private const int EventHandlerDbRefNumber = 9;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParserFor(_builder.DbRef, _builder.Handle);
	private TestIsolationHelpers.TestPlayer _builder = null!;
	private DBRef _room;

	/// <summary>
	/// A wizard created in a room of its own, so what it hears is only what it did. It is never in the
	/// shared start room: a room broadcast there reads the contents first and notifies each one after an
	/// awaited permission check, so a builder moved out mid-broadcast still heard another test's
	/// "has left" after <see cref="Run"/> had started counting.
	/// </summary>
	[Before(Test)]
	public async Task SetUpBuilder()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		_room = await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName("BcmRoom"), god));
		_builder = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BcmBuilder", _room);
		var builder = (await Mediator.Send(new GetObjectNodeQuery(_builder.DbRef))).Expect<SharpPlayer>();
		var wizard = await Mediator.Send(new GetObjectFlagQuery("WIZARD"));
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(builder, wizard!))).IsTrue();
	}

	[After(Test)]
	public async Task DisconnectBuilder()
	{
		if (_builder is not null)
			await ConnectionService.Disconnect(_builder.Handle);
	}

	/// <summary>Runs <paramref name="command"/> as the builder: its result, and everything the builder heard.</summary>
	private async ValueTask<(string Result, List<string> Heard)> Run(string command)
	{
		var pre = WebAppFactoryArg.Notifications.CountFor(_builder.DbRef);
		var result = await Parser.CommandParse(_builder.Handle, ConnectionService, MarkupText.Plain(command));
		return (result?.Message.ToPlainText() ?? string.Empty,
			[.. WebAppFactoryArg.Notifications.For(_builder.DbRef).Skip(pre)]);
	}

	private async Task<DBRef> Destination(string prefix)
	{
		var builder = (await Mediator.Send(new GetObjectNodeQuery(_builder.DbRef))).Expect<SharpPlayer>();
		return await Mediator.Send(new CreateRoomCommand(TestIsolationHelpers.GenerateUniqueName(prefix), builder));
	}

	private async Task<DBRef> PlayerNamed(string name)
		=> (await Mediator.CreateStream(new GetPlayerQuery(name)).SingleAsync()).Object.DBRef;

	private async Task<int> ExitNumber(DBRef room, string name)
		=> (await Mediator.CreateStream(new GetExitsQuery(room)).SingleAsync(exit => exit.Object.Name == name))
			.Object.DBRef.Number;

	/// <summary>
	/// Each <c>do_real_open</c> says "Opened exit #N", then "Trying to link..." and
	/// "Linked exit #N to #M" (<c>create.c:161</c>, <c>:165</c>, <c>:175</c>) — dbrefs, not the name the
	/// builder typed. <c>@open</c>'s exit back is a second <c>do_real_open</c> (<c>:236</c>). SharpMUSH said
	/// "Linked to &lt;name&gt;." and never "Trying to link...".
	/// </summary>
	[Test]
	public async ValueTask OpenSaysWhatPennSaysForBothExits()
	{
		var destination = await Destination("BcmOpenDest");
		var forwardName = TestIsolationHelpers.GenerateUniqueName("BcmOpenTo");
		var backName = TestIsolationHelpers.GenerateUniqueName("BcmOpenBack");

		var (_, heard) = await Run($"@open {forwardName}={destination},{backName}");

		var forward = await ExitNumber(_room, forwardName);
		var back = await ExitNumber(destination, backName);
		await Assert.That(heard).IsEquivalentTo(new[]
		{
			$"Opened exit #{forward}",
			"Trying to link...",
			$"Linked exit #{forward} to #{destination.Number}",
			$"Opened exit #{back}",
			"Trying to link...",
			$"Linked exit #{back} to #{_room.Number}"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary><c>fun_open</c> is one <c>do_real_open</c> (<c>fundb.c:2171</c>), so it says the same three
	/// lines. SharpMUSH linked the exit and said nothing about it.</summary>
	[Test]
	public async ValueTask OpenFunctionSaysWhatPennSays()
	{
		var destination = await Destination("BcmOpenFnDest");
		var name = TestIsolationHelpers.GenerateUniqueName("BcmOpenFn");

		var (_, heard) = await Run($"think [open({name},{destination})]");

		var exit = await ExitNumber(_room, name);
		await Assert.That(heard.Take(3)).IsEquivalentTo(new[]
		{
			$"Opened exit #{exit}",
			"Trying to link...",
			$"Linked exit #{exit} to #{destination.Number}"
		}, TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	/// <summary>
	/// <c>fun_pcreate</c> is a call to <c>do_pcreate</c> (<c>fundb.c:2140</c>), which tells the creator
	/// "New player '%s' (#%d) created with password '%s'" (<c>wiz.c:140</c>). Only <c>@pcreate</c> said it.
	/// </summary>
	[Test]
	public async ValueTask PcreateFunctionTellsTheCreatorWhatItMade()
	{
		var name = $"BcmPc{Guid.NewGuid():N}"[..14];

		var (_, heard) = await Run($"think [pcreate({name},bcmpass)]");

		var player = await PlayerNamed(name);
		await Assert.That(heard).Contains($"New player '{name}' (#{player.Number}) created with password 'bcmpass'");
	}

	/// <summary>
	/// <c>do_pcreate</c> queues <c>PLAYER`CREATE</c> with the new player's objid, its name and
	/// <c>pcreate</c> (<c>wiz.c:142-143</c>), whether <c>@pcreate</c> or <c>pcreate()</c> called it.
	/// Only <c>@pcreate</c> fired it.
	/// </summary>
	[Test]
	// Both rows install and wipe the one PLAYER`CREATE handler on the event handler object.
	[NotInParallel("EventHandlerPlayerCreate")]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask PcreateFiresPlayerCreate(bool throughTheFunction)
	{
		var name = $"BcmEv{Guid.NewGuid():N}"[..14];

		try
		{
			// One attribute per created player, so a pcreate elsewhere in the run cannot be mistaken for this one.
			await Run($"&PLAYER`CREATE #{EventHandlerDbRefNumber}=&BCMEV`%1 me=%0 %2");

			await Run(throughTheFunction ? $"think [pcreate({name},bcmpass)]" : $"@pcreate {name}=bcmpass");

			var player = await PlayerNamed(name);
			await WebAppFactoryArg.QueueBarrierAsync();
			var (_, seen) = await Run($"think [get(#{EventHandlerDbRefNumber}/BCMEV`{name})]");
			await Assert.That(seen).Contains($"{player} pcreate");
		}
		finally
		{
			await Run($"@wipe #{EventHandlerDbRefNumber}/PLAYER`CREATE");
			await Run($"@wipe #{EventHandlerDbRefNumber}/BCMEV");
		}
	}
}
