using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// PennMUSH queues <c>OBJECT`CREATE</c> for every exit <c>do_real_open</c> makes
/// (<c>src/create.c:181</c>) and for every room <c>do_dig</c> makes (<c>:526</c>), each with the new
/// object alone: <c>queue_event(player, "OBJECT`CREATE", "%s", unparse_objid(...))</c>. That covers
/// <c>@dig</c>'s two exits and <c>@open</c>'s return exit, since both are further
/// <c>do_real_open</c>s. SharpMUSH fired the event, and the plugin creation hook beside it, for
/// things and clones only.
/// </summary>
/// <remarks>
/// <c>event_handler = 9</c> (the seeded Event Handler) in the test config. Other tests build while
/// these run, so the log is read for the objects each test made and not for its length.
/// </remarks>
[NotInParallel]
public class RoomAndExitCreationEventTests
{
	private const int EventHandlerDbRefNumber = 9;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private async Task<string> AsGod(string command)
		=> (await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command)))?.Message.ToPlainText()
			?? string.Empty;

	/// <summary>What the handler recorded, once the events queued so far have run.</summary>
	private async Task<string> Get(string attribute)
	{
		await WebAppFactoryArg.QueueBarrierAsync();
		return (await Parser.FunctionParse(MarkupText.Plain($"get(#{EventHandlerDbRefNumber}/{attribute})")))?.Message
			?.ToPlainText() ?? string.Empty;
	}

	/// <summary>The one object answering to <paramref name="name"/>.</summary>
	private async Task<int> Only(string name)
	{
		// Anything the creation events queued has to have run, or a handler's own build is still pending.
		await WebAppFactoryArg.QueueBarrierAsync();
		var found = (await AsGod($"think lsearch(all,name,{name})"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(found.Length).IsEqualTo(1);
		return DBRef.Parse(found[0]).Number;
	}

	/// <summary>Every <c>OBJECT`CREATE</c> fired while <paramref name="build"/> ran, oldest first, as dbref numbers.</summary>
	private async Task<int[]> EventsDuring(Func<Task> build)
	{
		try
		{
			await AsGod($"&CREATELOG #{EventHandlerDbRefNumber}=");
			await AsGod($"&OBJECT`CREATE #{EventHandlerDbRefNumber}="
				+ $"think set(#{EventHandlerDbRefNumber},CREATELOG:[get(#{EventHandlerDbRefNumber}/CREATELOG)] %0)");

			await build();

			return [.. (await Get("CREATELOG")).Split(' ', StringSplitOptions.RemoveEmptyEntries)
				.Select(logged => DBRef.Parse(logged).Number)];
		}
		finally
		{
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/OBJECT`CREATE");
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/CREATELOG");
		}
	}

	/// <summary>Those of <paramref name="events"/> that name one of <paramref name="ours"/>, in order.</summary>
	private static string Ours(int[] events, params int[] ours)
		=> string.Join(' ', events.Where(ours.Contains).Select(number => $"#{number}"));

	/// <summary>
	/// <c>do_dig</c> runs <c>do_real_open</c> for the exit to the room and the exit back
	/// (<c>create.c:507-517</c>), each queueing its own event, and then queues the room's (<c>:526</c>).
	/// <c>fun_dig</c> is the same call (<c>src/fundb.c:2177-2189</c>).
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask DiggingFiresObjectCreateForTheRoomAndBothExits(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var room = 0;

		var events = await EventsDuring(async () =>
			room = DBRef.Parse(throughTheFunction
				? await AsGod($"think dig(RxeRoom{uid},RxeTo{uid},RxeFrom{uid})")
				: await AsGod($"@dig RxeRoom{uid}=RxeTo{uid},RxeFrom{uid}")).Number);

		var to = await Only($"RxeTo{uid}");
		var from = await Only($"RxeFrom{uid}");

		await Assert.That(Ours(events, room, to, from)).IsEqualTo($"#{to} #{from} #{room}")
			.Because("each exit's do_real_open queues its event before do_dig queues the room's, once each");
	}

	/// <summary>
	/// <c>@dig/teleport</c> moves the digger (<c>create.c:518-525</c>) before <c>do_dig</c> queues the
	/// room's event (<c>:526</c>), so a handler sees the enactor already in the new room.
	/// </summary>
	[Test]
	public async ValueTask DigTeleportMovesTheDiggerBeforeTheRoomsEvent()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var home = (await AsGod("think loc(me)")).Trim();

		try
		{
			await AsGod($"&OBJECT`CREATE #{EventHandlerDbRefNumber}="
				+ $"think set(#{EventHandlerDbRefNumber},CREATELOC:[loc(%#)])");

			var room = DBRef.Parse(await AsGod($"@dig/teleport RxeTelRoom{uid}")).Number;

			await Assert.That(DBRef.Parse(await Get("CREATELOC")).Number).IsEqualTo(room);
		}
		finally
		{
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/OBJECT`CREATE");
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/CREATELOC");
			await AsGod($"@tel me={home}");
		}
	}

	/// <summary>
	/// <c>do_open</c> is one <c>do_real_open</c> for the exit and a second for the return exit
	/// (<c>create.c:229-236</c>), and each queues <c>OBJECT`CREATE</c> (<c>:181</c>).
	/// </summary>
	[Test]
	public async ValueTask OpeningFiresObjectCreateForTheExitAndTheReturnExit()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var destination = DBRef.Parse(await AsGod($"@dig RxeOpenDest{uid}")).Number;
		var forward = 0;

		var events = await EventsDuring(async () =>
			forward = DBRef.Parse(await AsGod($"@open RxeOut{uid}=#{destination},RxeBack{uid}")).Number);

		var back = await Only($"RxeBack{uid}");

		await Assert.That(Ours(events, destination, forward, back)).IsEqualTo($"#{forward} #{back}");
	}

	/// <summary><c>fun_open</c> is a <c>do_real_open</c> too (<c>src/fundb.c:2144-2173</c>).</summary>
	[Test]
	public async ValueTask TheOpenFunctionFiresObjectCreate()
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var exit = 0;

		var events = await EventsDuring(async () =>
			exit = DBRef.Parse(await AsGod($"think open(RxeFnOut{uid})")).Number);

		await Assert.That(Ours(events, exit)).IsEqualTo($"#{exit}");
	}

	/// <summary>
	/// The C# object-lifecycle hook fires beside <c>OBJECT`CREATE</c>, as it does for things and clones:
	/// once for every room and exit a build makes, each credited to the builder.
	/// </summary>
	[Test]
	[Arguments("@dig RxeHookRoom{0}=RxeHookTo{0},RxeHookFrom{0}", 3)]
	[Arguments("think dig(RxeHookRoom{0},RxeHookTo{0},RxeHookFrom{0})", 3)]
	[Arguments("@open RxeHookOut{0}=#{1},RxeHookBack{0}", 2)]
	[Arguments("think open(RxeHookOut{0})", 1)]
	public async ValueTask BuildingFiresThePluginCreationHookForEachRoomAndExit(string build, int created)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var destination = DBRef.Parse(await AsGod($"@dig RxeHookDest{uid}")).Number;

		var hooks = Substitute.For<IPluginHookDispatcher>();
		var original = (SharpMUSH.Implementation.MUSHCodeParser)WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
		var provider = Substitute.For<IServiceProvider>();
		provider.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IPluginHookDispatcher)
			? hooks
			: WebAppFactoryArg.Services.GetService(call.Arg<Type>()));
		var parser = new SharpMUSH.Implementation.MUSHCodeParser(original.Logger, original.FunctionLibrary,
			original.CommandLibrary, original.Configuration, provider);

		var result = (await parser.CommandParse(1, ConnectionService,
			MarkupText.Plain(string.Format(build, uid, destination))))?.Message.ToPlainText() ?? string.Empty;
		var first = DBRef.Parse(result).Number;

		var named = new List<int> { first };
		foreach (var name in new[] { $"RxeHookTo{uid}", $"RxeHookFrom{uid}", $"RxeHookBack{uid}" })
		{
			var found = (await AsGod($"think lsearch(all,name,{name})")).Trim();
			named.AddRange(found.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(f => DBRef.Parse(f).Number));
		}

		await Assert.That(named.Count).IsEqualTo(created);
		var calls = hooks.ReceivedCalls()
			.Where(call => call.GetMethodInfo().Name == nameof(IPluginHookDispatcher.ObjectCreatedAsync))
			.Select(call => call.GetArguments())
			.ToList();
		await Assert.That(calls.Select(arguments => ((DBRef)arguments[0]!).Number).Order())
			.IsEquivalentTo(named.Order());
		await Assert.That(calls.All(arguments => ((DBRef)arguments[1]!).Number == 1)).IsTrue();
	}

	/// <summary>Creates a thing as God and frees its dbref again — <c>@destroy</c> twice is Penn's immediate free.</summary>
	private async Task<int> Hole(string name)
	{
		var doomed = DBRef.Parse(await AsGod($"@create {name}"));
		await AsGod($"@destroy {doomed}");
		await AsGod($"@destroy {doomed}");

		await Assert.That(await Mediator.Send(new GetObjectNodeQuery(new DBRef(doomed.Number))) is None).IsTrue();

		return doomed.Number;
	}

	/// <summary>
	/// The event's handler runs inline, and one that builds at a requested dbref of its own takes the
	/// requested-dbref gate. The event has to fire after the build has released it, or the server
	/// deadlocks — bounded here so a regression fails rather than wedging the suite.
	/// </summary>
	[Test]
	[Arguments("@dig RxeReRoom{0}=,,#{1}")]
	[Arguments("think dig(RxeReRoom{0},,,#{1})")]
	[Arguments("@open RxeReOut{0}=,,,#{1}")]
	[Arguments("think open(RxeReOut{0},,,#{1})")]
	public async ValueTask ACreationEventMayItselfBuildAtARequestedDbref(string outerBuild)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var outerHole = await Hole($"RxeHoleA{uid}");
		var innerHole = await Hole($"RxeHoleB{uid}");

		try
		{
			await AsGod($"&OBJECT`CREATE #{EventHandlerDbRefNumber}="
				+ $"@create RxeReInner{uid}=,#{innerHole}");

			var build = Task.Run(() => AsGod(string.Format(outerBuild, uid, outerHole)));
			var finished = await Task.WhenAny(build, Task.Delay(TimeSpan.FromSeconds(30)));

			await Assert.That(finished).IsSameReferenceAs(build)
				.Because("the gate must be released before the creation event runs its handler");
			await Assert.That(DBRef.Parse(await build).Number).IsEqualTo(outerHole);
			await Assert.That(await Only($"RxeReInner{uid}")).IsEqualTo(innerHole)
				.Because("the handler's own requested build has to have gone through");
		}
		finally
		{
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/OBJECT`CREATE");
		}
	}
}
