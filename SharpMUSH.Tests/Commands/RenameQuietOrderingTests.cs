using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>do_name</c> ends with <c>queue_event(...OBJECT`RENAME...)</c> and then
/// <c>if (!AreQuiet(player, thing)) notify(player, T("Name set."))</c> (<c>src/set.c:151-154</c>).
/// <c>queue_event</c> only enqueues, so <c>AreQuiet</c> is answered against the state the rename
/// found. SharpMUSH's <c>IEventService.TriggerEventAsync</c> runs the handler inline, so a handler
/// that sets <c>QUIET</c> would answer the check for its own rename unless the flag is read first.
/// </summary>
/// <remarks>
/// <c>event_handler = 9</c> (the seeded Event Handler) in the test config. It is both the rename's
/// target and the handler: a handler runs as the handler object, so the only thing it can flag is
/// itself, and <c>AreQuiet</c>'s second half needs the renamer to own the target, which only God
/// does. Writing a global handler attribute is not something a parallel test may do.
/// </remarks>
[NotInParallel]
public class RenameQuietOrderingTests
{
	private const int EventHandlerDbRefNumber = 9;

	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private async Task AsGod(string command)
		=> await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async Task<bool> HandlerIsQuiet()
		=> (await Parser.FunctionParse(MarkupText.Plain($"hasflag(#{EventHandlerDbRefNumber},QUIET)")))!
			.Message!.ToPlainText() == "1";

	[Test]
	public async ValueTask ARenameHandlerThatSetsQuietDoesNotSwallowItsOwnNameSet()
	{
		var god = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>().Object.DBRef;
		var original = (await Parser.FunctionParse(MarkupText.Plain($"name(#{EventHandlerDbRefNumber})")))!
			.Message!.ToPlainText();
		var renamed = TestIsolationHelpers.GenerateUniqueName("QuietRenameHandler");
		var recorder = WebAppFactoryArg.Notifications;

		try
		{
			await AsGod($"@set #{EventHandlerDbRefNumber}=!QUIET");
			await AsGod($"&OBJECT`RENAME #{EventHandlerDbRefNumber}=@set me=QUIET");

			var before = recorder.CountFor(god);
			await AsGod($"@name #{EventHandlerDbRefNumber}={renamed}");
			var heard = recorder.For(god).Skip(before).ToList();

			await Assert.That(await HandlerIsQuiet()).IsTrue()
				.Because("the handler has to have run for the ordering to be under test at all");
			await Assert.That(heard).Contains("Name set.")
				.Because("the handler's QUIET arrives after queue_event, so it cannot answer this rename's AreQuiet");

			// And the flag it set does hold for the next rename, which is AreQuiet's other half:
			// Quiet(thing) && Owner(thing) == player.
			var beforeSecond = recorder.CountFor(god);
			await AsGod($"@name #{EventHandlerDbRefNumber}={original}");
			await Assert.That(recorder.For(god).Skip(beforeSecond).ToList()).DoesNotContain("Name set.");
		}
		finally
		{
			await AsGod($"@wipe #{EventHandlerDbRefNumber}/OBJECT`RENAME");
			await AsGod($"@set #{EventHandlerDbRefNumber}=!QUIET");
			await AsGod($"@name #{EventHandlerDbRefNumber}={original}");
		}
	}
}
