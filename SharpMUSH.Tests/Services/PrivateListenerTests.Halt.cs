using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Implementation.Handlers.ListenPattern;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	public static IEnumerable<(string Kind, bool Halted, bool Captured)> HaltAdmissionCases()
	{
		foreach (var kind in new[] { "thing", "room", "player" })
			foreach (var halted in new[] { false, true })
				foreach (var captured in new[] { false, true })
					yield return (kind, halted, captured);
	}

	[Test]
	[MethodDataSource(nameof(HaltAdmissionCases))]
	public async Task HaltAdmissionRejectsNonplayersForNamedAndCapturedActions(string kind, bool halted, bool captured)
	{
		var actor = await Player();
		var reference = kind switch
		{
			"player" => actor.DbRef,
			"room" => DBRef.Parse((await Admin($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText().Trim()),
			_ => await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "HaltAdmission")
		};
		await Admin($"@ahear {reference}=&HEARD me=admitted");
		await Admin($"@set {reference}={(halted ? "HALT" : "!HALT")}");
		var listener = await Node(reference);
		await Assert.That(await listener.HasFlag("HALT")).IsEqualTo(halted);
		var pipeline = await Build(actor, actor.DbRef);
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler),
			Factory.Services.GetRequiredService<IAttributeService>(), NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		var request = new ExecuteListenPatternCommand(listener, await Node(actor.DbRef), "AHEAR", []);
		if (captured) request = request with { Action = MarkupText.Plain("&HEARD me=admitted") };
		await handler.Handle(request, CancellationToken.None);
		var accepted = !halted || kind == "player";
		await Assert.That(pipeline.Queue.Count).IsEqualTo(accepted ? 1 : 0);
		await pipeline.Scheduler.Received(accepted ? 1 : 0).AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>());
	}

	[Test]
	[Arguments(false, false, false)]
	[Arguments(false, false, true)]
	[Arguments(false, true, false)]
	[Arguments(false, true, true)]
	[Arguments(true, false, false)]
	[Arguments(true, false, true)]
	[Arguments(true, true, false)]
	[Arguments(true, true, true)]
	public async Task HaltHearActionsDoNotSuppressPrivateOrPublicPuppetDelivery(bool publicSpeech, bool self, bool halted)
	{
		var owner = await Player();
		var reference = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "HaltPuppet");
		var room = DBRef.Parse((await Admin($"@dig {Guid.NewGuid():N}")).Message!.ToPlainText().Trim());
		await Admin($"@tel {reference}={room}");
		if (!publicSpeech) await Admin($"@tel {owner.DbRef}={room}");
		await Admin($"@chown {reference}={owner.DbRef}");
		await Admin($"@set {reference}=PUPPET");
		await Admin($"@listen {reference}=*");
		await Admin($"@ahear {reference}=&HEARD me=external");
		await Admin($"@amhear {reference}=&HEARD me=self");
		await Admin($"@aahear {reference}=&ALWAYS me=heard");
		await Admin($"@set {reference}={(halted ? "HALT" : "!HALT")}");
		var listener = await Node(reference);
		await Assert.That(await listener.HasFlag("HALT")).IsEqualTo(halted);
		var pipeline = await Build(owner, owner.DbRef);
		await pipeline.Notify.Notify(listener, "halt body", self ? listener : await Node(owner.DbRef),
			publicSpeech ? INotifyService.NotificationType.Emit : INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(halted ? 0 : 2);
		if (!halted)
			await Assert.That(pipeline.Queue.Select(item => item.State.CurrentEvaluation!.Name))
				.IsEquivalentTo([self ? "AMHEAR" : "AHEAR", "AAHEAR"]);
		var output = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
			.Single(message => message.Handle == 901);
		await Assert.That(MarkupTextSerializer.Deserialize(output.Markup).ToPlainText()).EndsWith("> halt body");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(false, true)]
	[Arguments(true, false)]
	[Arguments(true, true)]
	public async Task HaltedPlayersRetainListenActionsButNeverMonitorActions(bool self, bool halted)
	{
		var actor = await Player();
		var listenerPlayer = await Player();
		var reference = listenerPlayer.DbRef;
		await Admin($"@listen {reference}=*");
		await Admin($"@ahear {reference}=&HEARD me=external");
		await Admin($"@amhear {reference}=&HEARD me=self");
		await Admin($"@aahear {reference}=&ALWAYS me=heard");
		await Admin($"&PATTERN {reference}=^*:&MONITORED me=heard");
		await Admin($"@set {reference}/PATTERN=AAHEAR");
		await Admin($"@set {reference}=MONITOR !NO_COMMAND {(halted ? "HALT" : "!HALT")}");
		var listener = await Node(reference);
		await Assert.That(await listener.HasFlag("HALT")).IsEqualTo(halted);
		await Assert.That(await listener.HasFlag("NO_COMMAND")).IsFalse();
		var pipeline = await Build(actor, reference);
		await pipeline.Notify.Notify(listener, "player heard", self ? listener : await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(halted ? 2 : 3);
		string[] expected = halted ? [self ? "AMHEAR" : "AHEAR", "AAHEAR"] : [self ? "AMHEAR" : "AHEAR", "AAHEAR", "PATTERN"];
		await Assert.That(pipeline.Queue.Select(item => item.State.CurrentEvaluation!.Name))
			.IsEquivalentTo(expected);
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
			.Any(message => MarkupTextSerializer.Deserialize(message.Markup).ToPlainText().Contains("player heard", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task CancelledActionDoesNotReadListenerFlagsOrAttributes()
	{
		var actor = await Player();
		var pipeline = await Build(actor, actor.DbRef);
		var listener = new TestObjectFactory().CreateThing(9, "cancelled");
		listener.Object().Flags = new(() => throw new InvalidOperationException("Flags must not be read"));
		var attributes = Substitute.For<IAttributeService>();
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler), attributes,
			NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAsync<OperationCanceledException>(async () => await handler.Handle(
			new ExecuteListenPatternCommand(listener, listener, "AHEAR", []) { Action = MarkupText.Plain("think denied") }, cancellation.Token));
		await Assert.That(attributes.ReceivedCalls()).IsEmpty();
		await pipeline.Scheduler.DidNotReceive().AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>());
	}

	[Test]
	public async Task RestrictedActionDoesNotReadListenerFlagsOrAttributes()
	{
		var actor = await Player();
		var pipeline = await Build(actor, actor.DbRef);
		var listener = new TestObjectFactory().CreateThing(9, "restricted");
		listener.Object().Flags = new(() => throw new InvalidOperationException("Flags must not be read"));
		var attributes = Substitute.For<IAttributeService>();
		var handler = new ExecuteListenPatternCommandHandler(new Provider(Factory.Services, pipeline.Scheduler), attributes,
			NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		using var restrictions = new EvaluationRestrictions([]).Enter();
		await Assert.ThrowsAsync<RestrictedExpressionException>(async () => await handler.Handle(
			new ExecuteListenPatternCommand(listener, listener, "AHEAR", []) { Action = MarkupText.Plain("think denied") }, CancellationToken.None));
		await Assert.That(attributes.ReceivedCalls()).IsEmpty();
		await pipeline.Scheduler.DidNotReceive().AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>());
	}
}
