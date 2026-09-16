using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The actions destruction queues: ADESTROY when an object is scheduled (PennMUSH
/// <c>pre_destroy</c>'s <c>did_it</c>, gated by the <c>adestroy</c> option) and STARTUP when it is
/// spared (<c>undestroy</c>'s <c>queue_attribute_noparent</c>, skipped for a HALTed object). Both are
/// command lists run from the queue, so every assertion is a side effect the action wrote, polled until
/// the queue has drained it.
/// </summary>
[NotInParallel]
public class DestroyActionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private ValueTask<CallState> AsGod(string command) =>
		Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private static DBRef Parse(CallState result) => DBRef.Parse(result.Message!.ToPlainText().Trim());

	private async Task<DBRef> CreateAsync(string prefix)
		=> Parse(await AsGod($"@create {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	private async Task<DBRef> DigAsync(string prefix)
		=> Parse(await AsGod($"@dig {TestIsolationHelpers.GenerateUniqueName(prefix)}"));

	private async Task<string> GetAsync(DBRef obj, string attr)
		=> (await Parser.FunctionParse(MarkupText.Plain($"get({obj}/{attr})")))?.Message?.ToPlainText() ?? string.Empty;

	private async Task WaitForAsync(DBRef obj, string attr, string expected)
		=> await Assert.That(async () => await GetAsync(obj, attr))
			.WaitsFor(value => value.IsEqualTo(expected), timeout: TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(50));

	/// <summary>
	/// A drain marker: queued after the action under test, so once it has run the action has had its
	/// turn, and an attribute it did not write is an action that did not run.
	/// </summary>
	private async Task DrainAsync()
	{
		var marker = await CreateAsync("DAT_Marker");
		await AsGod($"&GO {marker}=&DONE me=1");
		await AsGod($"@trigger {marker}/GO");
		await WaitForAsync(marker, "DONE", "1");
	}

	private static IDisposable Adestroy(bool on)
		=> TestOptionsOverride.Scope(options => options with
		{
			Attribute = options.Attribute with { ADestroy = on }
		});

	[Test]
	public async Task Adestroy_RunsAsAQueuedCommandList_WithTheDestroyerAsEnactor()
	{
		using var _ = Adestroy(true);
		var destroyer = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DAT_Destroyer");
		var thing = Parse(await Parser.CommandParse(destroyer.Handle, ConnectionService,
			MarkupText.Plain($"@create {TestIsolationHelpers.GenerateUniqueName("DAT_Doomed")}")));
		// STATE is written last, so waiting on it waits for the whole list.
		await AsGod($"&ADESTROY {thing}=&WHO me=%#;&STATE me=ran");

		await Parser.CommandParse(destroyer.Handle, ConnectionService, MarkupText.Plain($"@destroy {thing}"));

		await WaitForAsync(thing, "STATE", "ran");
		await Assert.That(await GetAsync(thing, "WHO")).StartsWith($"#{destroyer.DbRef.Number}");
	}

	[Test]
	public async Task Adestroy_IsNotRun_WhenTheOptionIsOff()
	{
		using var _ = Adestroy(false);
		var thing = await CreateAsync("DAT_Quiet");
		// set() acts whether the attribute is run as commands or merely evaluated, so this also
		// catches an ungated evaluation.
		await AsGod($"&ADESTROY {thing}=[set(me,STATE:ran)]");

		await AsGod($"@destroy {thing}");
		await DrainAsync();

		await Assert.That(await GetAsync(thing, "STATE")).IsEmpty();
	}

	/// <summary>
	/// did_it looks the action up through parents, but ADESTROY itself is AF_PRIVATE
	/// (<c>hdrs/atr_tab.h:20</c>, <c>no_inherit</c> in the seed), so a parent's never reaches the child.
	/// </summary>
	[Test]
	public async Task Adestroy_IsNotInherited_BecauseTheAttributeIsPrivate()
	{
		using var _ = Adestroy(true);
		var parent = await CreateAsync("DAT_Parent");
		var child = await CreateAsync("DAT_Child");
		await AsGod($"&ADESTROY {parent}=&STATE me=inherited");
		await AsGod($"@parent {child}={parent}");

		await AsGod($"@destroy {child}");
		await DrainAsync();

		await Assert.That(await GetAsync(child, "STATE")).IsEmpty();
	}

	/// <summary>
	/// A room owned by a doomed player is reached twice — as a possession and, for its exit, through
	/// the room — yet each object is scheduled, and so runs its ADESTROY, exactly once.
	/// </summary>
	[Test]
	public async Task Adestroy_RunsOncePerScheduledObject_AcrossTheCascade()
	{
		using var _ = Adestroy(true);
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "DAT_Cascade");
		var room = await DigAsync("DAT_CascadeRoom");
		var elsewhere = await DigAsync("DAT_CascadeElsewhere");
		var exit = Parse(await AsGod($"@open {TestIsolationHelpers.GenerateUniqueName("DAT_CascadeExit")}={elsewhere},{room}"));
		foreach (var obj in new[] { room, exit })
		{
			await AsGod($"&ADESTROY {obj}=&HITS me=[get(me/HITS)]x");
			await AsGod($"@chown {obj}={player}");
			// @chown sets HALT, and a HALTed object runs no actions.
			await AsGod($"@set {obj}=!HALT");
		}

		await AsGod($"@nuke {player}");
		await WaitForAsync(room, "HITS", "x");
		await WaitForAsync(exit, "HITS", "x");
		await DrainAsync();

		await Assert.That(await GetAsync(room, "HITS")).IsEqualTo("x");
		await Assert.That(await GetAsync(exit, "HITS")).IsEqualTo("x");
	}

	[Test]
	public async Task Startup_RunsAsAQueuedCommandList_WithTheSparedObjectAsEnactor()
	{
		var thing = await CreateAsync("DAT_Spared");
		await AsGod($"&STARTUP {thing}=&BACK me=%#");

		await AsGod($"@destroy {thing}");
		await AsGod($"@undestroy {thing}");

		await Assert.That(async () => await GetAsync(thing, "BACK"))
			.WaitsFor(value => value.StartsWith($"#{thing.Number}"), timeout: TimeSpan.FromSeconds(5), pollingInterval: TimeSpan.FromMilliseconds(50));
	}

	[Test]
	public async Task Startup_RunsForEachObjectTheCascadeSpares()
	{
		var room = await DigAsync("DAT_SparedRoom");
		var elsewhere = await DigAsync("DAT_SparedElsewhere");
		var exit = Parse(await AsGod($"@open {TestIsolationHelpers.GenerateUniqueName("DAT_SparedExit")}={elsewhere},{room}"));
		await AsGod($"&STARTUP {room}=&BACK me=[get(me/BACK)]y");
		await AsGod($"&STARTUP {exit}=&BACK me=[get(me/BACK)]y");

		await AsGod($"@destroy {room}");
		await AsGod($"@undestroy {room}");
		await WaitForAsync(room, "BACK", "y");
		await WaitForAsync(exit, "BACK", "y");
		await DrainAsync();

		await Assert.That(await GetAsync(room, "BACK")).IsEqualTo("y");
		await Assert.That(await GetAsync(exit, "BACK")).IsEqualTo("y");
	}

	[Test]
	public async Task Startup_IsNotInherited()
	{
		var parent = await CreateAsync("DAT_StartupParent");
		var child = await CreateAsync("DAT_StartupChild");
		await AsGod($"&STARTUP {parent}=&BACK me=inherited");
		await AsGod($"@parent {child}={parent}");

		await AsGod($"@destroy {child}");
		await AsGod($"@undestroy {child}");
		await DrainAsync();

		await Assert.That(await GetAsync(child, "BACK")).IsEmpty();
	}

	[Test]
	public async Task Startup_IsNotRun_ForAHaltedObject()
	{
		var thing = await CreateAsync("DAT_Halted");
		await AsGod($"&STARTUP {thing}=[set(me,BACK:ran)]");
		await AsGod($"@set {thing}=HALT");

		await AsGod($"@destroy {thing}");
		await AsGod($"@undestroy {thing}");
		await DrainAsync();

		await Assert.That(await GetAsync(thing, "BACK")).IsEmpty();
	}
}
