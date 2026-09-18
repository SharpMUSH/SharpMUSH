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
/// PennMUSH's <c>fun_create</c> is a one-line call to <c>do_create</c> (<c>src/fundb.c</c>), so
/// <c>create()</c> has to be <c>@create</c> and not a second, thinner implementation of it. It was
/// the thinner one: the object landed in the creator's room rather than their inventory, it
/// inherited no zone, it reported nothing, and it fired neither <c>OBJECT`CREATE</c> nor the
/// object-lifecycle hook.
/// </summary>
public class BuildingFunctionParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	/// <summary>Runs <paramref name="expression"/> as the player behind <paramref name="handle"/>.</summary>
	private async Task<string> Eval(long handle, string expression)
		=> (await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}")))
			?.Message?.ToPlainText() ?? string.Empty;

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();

	/// <summary>
	/// <c>do_create</c> hands the new object to the player (<c>src/create.c</c>), and so does the
	/// function that calls it. This one put it in whatever room the creator was standing in.
	/// </summary>
	[Test]
	public async ValueTask CreateFunctionPutsTheObjectInTheCreatorsInventory()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BFP_Loc");

		var created = DBRef.Parse(await Eval(player.Handle, $"create(BfpLoc{Guid.NewGuid():N})"));
		var where = await (await Node(created)).Where();

		await Assert.That(where.Object().DBRef.Number).IsEqualTo(player.DbRef.Number);
	}

	/// <summary>A new object inherits its creator's zone, from either side of the pair.</summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CreationInheritsTheCreatorsZone(bool throughTheFunction)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BFP_Zone");

		var zoneResult = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create BfpZone{Guid.NewGuid():N}"));
		var zone = DBRef.Parse(zoneResult.Message!.ToPlainText());
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@chzone {player.DbRef}={zone}"));

		var name = $"BfpZoned{Guid.NewGuid():N}";
		var created = DBRef.Parse(throughTheFunction
			? await Eval(player.Handle, $"create({name})")
			: (await Parser.CommandParse(player.Handle, ConnectionService,
				MarkupText.Plain($"@create {name}"))).Message!.ToPlainText());

		var inherited = await (await Node(created)).Object().Zone.WithCancellation(CancellationToken.None);
		await Assert.That(inherited.Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(zone.Number)
			.Because("the creator carried a zone, so the new object has to carry it too");
	}

	/// <summary>
	/// An exit cannot hold anything, so an executor that is one builds into the room it is in.
	/// <c>AnySharpObject.AsContainer</c> throws for an exit, which made <c>@create</c> run by an
	/// exit's code fault outright; <c>create()</c> had a <c>Where()</c> fallback until the two were
	/// merged, and then it faulted too.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async ValueTask CreationByAnExitBuildsIntoTheRoomTheExitIsIn(bool throughTheFunction)
	{
		var uid = Guid.NewGuid().ToString("N")[..8];
		var roomResult = await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig BfpExitRoom{uid}"));
		var room = new DBRef(int.Parse(System.Text.RegularExpressions.Regex.Match(
			roomResult.Message!.ToPlainText(), @"#(\d+)").Groups[1].Value));

		var exitResult = await Parser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@open BfpExit{uid}={room}"));
		var exit = new DBRef(int.Parse(System.Text.RegularExpressions.Regex.Match(
			exitResult.Message!.ToPlainText(), @"#(\d+)").Groups[1].Value));

		// Where the exit itself lives is the answer we expect, and it is not necessarily `room`.
		var exitHome = (await (await Node(exit)).Where()).Object().DBRef;

		var name = $"BfpByExit{uid}";
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(throughTheFunction
			? $"@force {exit}=think create({name})"
			: $"@force {exit}=@create {name}"));

		var found = (await Eval(1, $"lsearch(all,name,{name})")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(found.Length).IsEqualTo(1)
			.Because("the create has to have happened at all — it used to throw for an exit executor");

		var built = DBRef.Parse(found[0]);
		await Assert.That((await (await Node(built)).Where()).Object().DBRef.Number).IsEqualTo(exitHome.Number);
	}

	/// <summary>
	/// <c>do_create</c> reports the object it made, and <c>fun_create</c> reaches that report by
	/// calling it.
	/// </summary>
	[Test]
	public async ValueTask CreateFunctionReportsTheNewObject()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "BFP_Report");
		var name = $"BfpReport{Guid.NewGuid():N}";

		await Eval(player.Handle, $"create({name})");

		await Assert.That(TestHelpers.ReceivedNotifyLocalizedWithKey(
			NotifyService, nameof(ErrorMessages.Notifications.Created), player.DbRef)).IsTrue();
	}
}
