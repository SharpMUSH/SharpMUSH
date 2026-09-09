using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class LookServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private ILookService LookService => WebAppFactoryArg.Services.GetRequiredService<ILookService>();
	private IOptionsMonitor<SharpMUSHOptions> Configuration =>
		WebAppFactoryArg.Services.GetRequiredService<IOptionsMonitor<SharpMUSHOptions>>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	/// <summary>
	/// PennMUSH <c>look_room</c> (<c>src/look.c:503</c>): an automatic look shows a TERSE player no
	/// description. The same look for a non-terse player shows it.
	/// </summary>
	[Test]
	public async ValueTask ATerseLookerSkipsTheDescriptionOnAnAutomaticLook()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "TerseLooker");
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("TerseRoom")}"));
		var roomRef = dig.Message!.ToPlainText().Trim();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {player.DbRef}={roomRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@describe {roomRef}=A distinctive description."));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=TERSE"));

		var roomDbRef = DBRef.Parse(roomRef);
		var room = await Mediator.Send(new GetObjectNodeQuery(roomDbRef));
		var looker = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;

		var terse = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room, LookKey.Auto));

		await Assert.That(terse.Any(m => m.Contains("A distinctive description."))).IsFalse();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=!TERSE"));

		var full = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room, LookKey.Auto));

		await Assert.That(full.Any(m => m.Contains("A distinctive description."))).IsTrue();
	}

	/// <summary>
	/// <c>LookRoom</c> is entered on the caller's parser state, never on a fresh one — the counters
	/// that bound an evaluation have to survive the hop into it. The two recursion tests below both
	/// run inside a single <c>EvaluateAttributeFunctionAsync</c> tree that enters <c>LookRoom</c>
	/// once, so a reset at the entry would not disturb either; this one drives the entry itself.
	/// </summary>
	/// <remarks>
	/// The real re-entrant caller is <c>EnterRoom</c> → <c>LookRoom</c>, which Task 10 adds. Nothing
	/// reaches <c>LookRoom</c> from inside an evaluation before it exists, so the boundary is driven
	/// here by handing <c>LookRoom</c> the state such a caller would hand it: one whose
	/// <c>DESCRIBE</c> recursion budget is already spent. Threaded, the description trips the
	/// recursion limit; reset, it evaluates as if nothing had run before it.
	/// </remarks>
	[Test]
	public async ValueTask LookRoomEvaluatesTheDescriptionOnTheCallersRecursionCounters()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DeepLooker");
		var dig = await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@dig {TestIsolationHelpers.GenerateUniqueName("DeepRoom")}"));
		var roomRef = dig.Message!.ToPlainText().Trim();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@teleport/silent {player.DbRef}={roomRef}"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@describe {roomRef}=A description from depth."));

		var room = await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(roomRef)));
		var looker = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Known;

		// A shallow parser sees the description, so the assertion below is about the depth and not
		// about the room being unreadable.
		var shallow = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room, LookKey.Normal));

		await Assert.That(shallow.Any(m => m.Contains("A description from depth."))).IsTrue();

		// AttributeService increments the per-attribute depth and errors once it passes the limit, so
		// a budget already at the limit is one that the next DESCRIBE evaluation must not be able to
		// afford.
		var limit = (int)Configuration.CurrentValue.Limit.FunctionRecursionLimit;
		var spent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["DESCRIBE"] = limit };
		var deepParser = GodParser.Push(GodParser.CurrentState with { FunctionRecursionDepths = spent });

		var deep = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(deepParser, looker, room, LookKey.Normal));

		await Assert.That(deep.Any(m => m.Contains("A description from depth."))).IsFalse();
		await Assert.That(deep.Any(m => m.Contains(ErrorMessages.Returns.Recursion))).IsTrue();
	}

	/// <summary>
	/// The description is evaluated on the caller's parser state, so
	/// <c>ParserState.FunctionRecursionDepths</c> bounds a <c>@describe</c> that evaluates itself.
	/// </summary>
	[Test]
	public async ValueTask ASelfReferentialDescriptionTerminatesWithARecursionError()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "RecursiveDesc");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {player.DbRef}=[u(%#/describe)]"));

		var messages = await MessagesWhile(player.DbRef, async () =>
			await GodParser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain("look me")));

		await Assert.That(messages.Any(m => m.Contains("recursion", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}

	/// <summary>
	/// <c>FunctionRecursionDepths</c> is keyed by attribute name, so it also bounds two descriptions
	/// that evaluate each other — neither of which repeats within one hop.
	/// </summary>
	[Test]
	public async ValueTask TwoDescriptionsThatEvaluateEachOtherTerminate()
	{
		var a = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MutualDescA");
		var b = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "MutualDescB");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {a.DbRef}=[u({b.DbRef}/describe)]"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@describe {b.DbRef}=[u({a.DbRef}/describe)]"));

		var messages = await MessagesWhile(a.DbRef, async () =>
			await GodParser.CommandParse(a.Handle, ConnectionService, MarkupText.Plain($"look {b.DbRef}")));

		await Assert.That(messages.Any(m => m.Contains("recursion", StringComparison.OrdinalIgnoreCase))).IsTrue();
	}
}
