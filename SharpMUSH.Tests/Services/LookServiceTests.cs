using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
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
		var looker = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();

		var terse = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room, LookKey.Auto));

		await Assert.That(terse.Any(m => m.Contains("A distinctive description."))).IsFalse();

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {player.DbRef}=!TERSE"));

		var full = await MessagesWhile(player.DbRef, async () =>
			await LookService.LookRoom(GodParser, looker, room, LookKey.Auto));

		await Assert.That(full.Any(m => m.Contains("A distinctive description."))).IsTrue();
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
