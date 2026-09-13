using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class EmitOutcomeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private INotifyService Notifications => Factory.Services.GetRequiredService<INotifyService>();
	private readonly List<long> _handles = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "Outcome");
		_handles.Add(player.Handle);
		return player;
	}
	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task<CallState> Admin(string command) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
	private async Task<CallState> Command(TestIsolationHelpers.TestPlayer actor, string command)
		=> await Factory.CommandParserFor(actor.DbRef, actor.Handle).CommandParse(actor.Handle, Connections, MarkupText.Plain(command));
	private async Task<DBRef> Room(params TestIsolationHelpers.TestPlayer[] players)
	{
		var result = await Admin($"@dig {Guid.NewGuid():N}");
		var room = DBRef.Parse(result.Message!.ToPlainText().Trim());
		foreach (var player in players) await Admin($"@tel {player.DbRef}={room}");
		return room;
	}
	private static string Invocation(string name, DBRef room, DBRef recipient, string body) => name switch
	{
		"nsoemit" => $"@nsoemit {room}/unmatched={body}",
		"nsprompt" => $"@nsprompt/silent {recipient}={body}",
		_ => $"@{name} {body}"
	};

	[Test]
	[Arguments("emit")]
	[Arguments("nsemit")]
	[Arguments("nsoemit")]
	[Arguments("nsprompt")]
	public async Task PayloadCommandsReturnOnlyAdmittedMessages(string name)
	{
		var actor = await Player();
		var recipient = await Player();
		var room = await Room(actor, recipient);
		var accepted = $"accepted_{Guid.NewGuid():N}";
		var result = await Command(actor, Invocation(name, room, recipient.DbRef, accepted));
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(accepted);
		await Assert.That(result.HadErrors).IsFalse();
		if (name == "nsprompt")
			await Notifications.Received().Prompt(TestHelpers.MatchingObject(recipient.DbRef), TestHelpers.MatchingMessage(accepted),
				TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		else await Assert.That(Factory.Notifications.For(recipient.DbRef)).Contains(accepted);
		await Admin(name == "nsprompt" ? $"@lock/page {recipient.DbRef}=#FALSE" : $"@lock/speech {room}=#FALSE");
		var denied = $"denied_{Guid.NewGuid():N}";
		result = await Command(actor, Invocation(name, room, recipient.DbRef, denied));
		await Assert.That(result.Message!.ToPlainText()).IsEmpty();
		await Assert.That(result.HadErrors).IsFalse();
		await Assert.That(Factory.Notifications.For(recipient.DbRef)).DoesNotContain(denied);
		await Notifications.DidNotReceive().Prompt(TestHelpers.MatchingObject(recipient.DbRef), TestHelpers.MatchingMessage(denied),
			TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
	}

	[Test]
	[Arguments("interact")]
	[Arguments("haven")]
	public async Task PromptRefusedByHearingOrHavenReturnsEmpty(string gate)
	{
		var actor = await Player();
		var recipient = await Player();
		await Admin(gate == "haven" ? $"@set {recipient.DbRef}=HAVEN" : $"@lock/interact {recipient.DbRef}=#FALSE");
		var body = $"refused_{Guid.NewGuid():N}";
		var result = await Command(actor, $"@nsprompt/silent {recipient.DbRef}={body}");
		await Assert.That(result.Message!.ToPlainText()).IsEmpty();
		await Notifications.DidNotReceive().Prompt(TestHelpers.MatchingObject(recipient.DbRef), TestHelpers.MatchingMessage(body),
			TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
	}

	[Test]
	[Arguments("oemit")]
	[Arguments("nsoemit")]
	public async Task UnresolvedExplicitOmitLocationRetainsCommandErrorOnly(string name)
	{
		var actor = await Player();
		var target = $"missing_{Guid.NewGuid():N}/unmatched";
		var result = await Command(actor, $"@{name} {target}=undeliverable");
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(ErrorMessages.Returns.InvalidRoom);
		await Assert.That(result.HadErrors).IsFalse();
		result = (await Factory.CommandParserFor(actor.DbRef, actor.Handle).FunctionParse(MarkupText.Plain($"[{name}({target},undeliverable)]")))!;
		await Assert.That(result.Message!.ToPlainText()).IsEmpty();
		await Assert.That(result.HadErrors).IsFalse();
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task PromptListAdmissionAndLookupFailuresAreIndependent(bool missingTarget, bool missingFirst)
	{
		var actor = await Player();
		var accepted = await Player();
		var denied = await Player();
		await Admin($"@lock/page {denied.DbRef}=#FALSE");
		var missing = $"missing_{Guid.NewGuid():N}";
		var expectedFailure = await Factory.Services.GetRequiredService<ILocateService>().LocateAndNotifyIfInvalidWithCallState(
			Factory.CommandParserFor(actor.DbRef, actor.Handle),
			(await Mediator.Send(new GetObjectNodeQuery(actor.DbRef))).Expect<AnySharpObject>(),
			(await Mediator.Send(new GetObjectNodeQuery(actor.DbRef))).Expect<AnySharpObject>(), missing, LocateFlags.All);
		var body = $"mixed_{Guid.NewGuid():N}";
		var targets = $"{denied.DbRef} {accepted.DbRef}";
		if (missingTarget) targets = missingFirst ? $"{missing} {targets}" : $"{targets} {missing}";
		var result = await Command(actor, $"@nsprompt/silent {targets}={body}");
		await Notifications.Received().Prompt(TestHelpers.MatchingObject(accepted.DbRef), TestHelpers.MatchingMessage(body),
			TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Notifications.DidNotReceive().Prompt(TestHelpers.MatchingObject(denied.DbRef), TestHelpers.MatchingMessage(body),
			TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(missingTarget
			? expectedFailure.Expect<Error<CallState>>().Value.Message!.ToPlainText() : body);
		await Assert.That(result.HadErrors).IsFalse();
		var functionResult = (await Factory.CommandParserFor(actor.DbRef, actor.Handle)
			.FunctionParse(MarkupText.Plain($"[nsprompt({targets},{body})]")))!;
		await Assert.That(functionResult.Message!.ToPlainText()).IsEmpty();
	}

	[Test]
	[Arguments("emit")]
	[Arguments("nsemit")]
	[Arguments("nsoemit")]
	[Arguments("nsprompt")]
	public async Task FunctionsReturnEmptyForAdmittedBodiesIncludingEmpty(string name)
	{
		var actor = await Player();
		var recipient = await Player();
		var room = await Room(actor, recipient);
		foreach (var body in new[] { $"function_{Guid.NewGuid():N}", "" })
		{
			var arguments = name switch { "nsoemit" => $"{room}/unmatched,{body}", "nsprompt" => $"{recipient.DbRef},{body}", _ => body };
			var result = (await Factory.CommandParserFor(actor.DbRef, actor.Handle).FunctionParse(MarkupText.Plain($"[{name}({arguments})]")))!;
			await Assert.That(result.Message!.ToPlainText()).IsEmpty();
			await Assert.That(result.HadErrors).IsFalse();
			if (name == "nsprompt")
				await Notifications.Received().Prompt(TestHelpers.MatchingObject(recipient.DbRef), TestHelpers.MatchingMessage(body),
					TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		}
	}

	[Test]
	[Arguments("@EMIT")]
	[Arguments("@NSEMIT")]
	public async Task AdmittedRoomReturnsPayloadWhenEveryListenerIsFiltered(string name)
	{
		var actor = await Player();
		await Room(actor);
		var permissions = Substitute.For<IPermissionService>();
		var notify = Substitute.For<INotifyService>();
		var communication = ActivatorUtilities.CreateInstance<CommunicationService>(Factory.Services, permissions, notify);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services, communication);
		var body = $"filtered_{Guid.NewGuid():N}";
		var parser = Factory.CommandParserFor(actor.DbRef, actor.Handle);
		parser = parser.FromState(parser.CurrentState with
		{
			Switches = ["NOEVAL"],
			Arguments = new() { ["0"] = new CallState(body) }
		});
		var definition = new SharpCommandAttribute { Name = name };
		var result = (name == "@EMIT" ? await commands.Emit(parser, definition) : await commands.NoSpoofEmit(parser, definition)).Expect<CallState>();
		await Assert.That(result.Message!.ToPlainText()).IsEqualTo(body);
		await Assert.That(notify.ReceivedCalls()).IsEmpty();
	}
}
