using MarkupString;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[Test]
	public async Task SpeakerInteractRefusalStopsReactionsAfterExecutorAdmitsOutput()
	{
		var executor = await Player();
		var speaker = await Player();
		var recipient = await Player();
		var outer = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "AdmissionOuter");
		var inner = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "AdmissionInner");
		await Admin($"@tel/inside {inner}={outer}");
		await Admin($"@tel/inside {recipient.DbRef}={inner}");
		await SetRaw(outer, "LISTEN", "*");
		await SetRaw(inner, "LISTEN", "*");
		await SetRaw(inner, "AHEAR", "&HEARD me=yes");
		await Admin($"@lock/interact {inner}=={executor.DbRef}");
		var permissions = Factory.Services.GetRequiredService<IPermissionService>();
		await Assert.That(await permissions.CanInteract(await Node(executor.DbRef), await Node(inner),
			IPermissionService.InteractType.Hear, await Node(speaker.DbRef))).IsTrue();
		await Assert.That(await permissions.CanInteract(await Node(speaker.DbRef), await Node(inner),
			IPermissionService.InteractType.Hear)).IsFalse();
		var context = new NotificationContext(outer, speaker.DbRef, []) { Executor = executor.DbRef };
		var pipeline = await Build(executor, inner);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.NotifyContextAsync(context, MarkupText.Plain("body"), await Node(speaker.DbRef),
			INotifyService.NotificationType.PrivateEmit);
		await Assert.That(string.Join('|', ForwardedOutput(pipeline))).IsEqualTo("body");
		await Assert.That(pipeline.Queue).IsEmpty();
		var childPipeline = await Build(executor, recipient.DbRef);
		await childPipeline.Notify.NotifyContextAsync(context, MarkupText.Plain("body"), await Node(speaker.DbRef),
			INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(childPipeline)).IsEmpty();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ForwardedHearAdmissionRetainsDistinctExecutorAndSpeaker(bool allowed)
	{
		var executor = await Player();
		var speaker = await Player();
		var recipient = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "AuthorityForwarder");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await SetRaw(listener, "LISTEN", "*");
		var permissions = Substitute.For<IPermissionService>();
		permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), IPermissionService.InteractType.Hear).Returns(true);
		permissions.CanInteract(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>(), IPermissionService.InteractType.Hear,
			Arg.Any<AnySharpObject>()).Returns(allowed);
		var pipeline = await Build(executor, recipient.DbRef, permission: permissions);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.NotifyContextAsync(new NotificationContext(listener, speaker.DbRef, []) { Executor = executor.DbRef },
			MarkupText.Plain("body"), await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await permissions.Received(1).CanInteract(Arg.Is<AnySharpObject>(node => node.Object().DBRef == executor.DbRef),
			Arg.Is<AnySharpObject>(node => node.Object().DBRef == recipient.DbRef), IPermissionService.InteractType.Hear,
			Arg.Is<AnySharpObject>(node => node.Object().DBRef == speaker.DbRef));
		await Assert.That(ForwardedOutput(pipeline).Length).IsEqualTo(allowed ? 1 : 0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ForwardedMarkupAndRawCapturesPrecedeSingleRecipientHeader(bool noSpoofVariant)
	{
		var speaker = await Player();
		var recipient = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "StyledForwarder");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await Admin($"@set {recipient.DbRef}=NOSPOOF");
		await SetRaw(listener, "LISTEN", "*");
		await SetRaw(listener, "INPREFIX", "[ansi(g,prefix)]");
		await SetRaw(recipient.DbRef, "LISTEN", "*");
		await SetRaw(recipient.DbRef, "AHEAR", "&HEARD me=%0");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		var body = (await pipeline.Parser.FunctionParse(MarkupText.Plain("ansi(r,body)")))!.Message!;
		await pipeline.Notify.Notify(listener, body, await Node(speaker.DbRef), noSpoofVariant
			? INotifyService.NotificationType.NSPrivateEmit : INotifyService.NotificationType.PrivateEmit);
		var output = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<SharpMUSH.Messaging.Messages.MarkupOutputMessage>()).Single();
		var delivered = MarkupTextSerializer.Deserialize(output.Markup);
		await Assert.That(delivered.ToPlainText()).EndsWith("prefix body");
		await Assert.That(delivered.ToPlainText().Count(character => character == '[')).IsEqualTo(noSpoofVariant ? 0 : 1);
		await Assert.That(delivered.Runs.Count).IsGreaterThanOrEqualTo(2);
		var captured = pipeline.Queue.Single().State.EnvironmentRegisters["0"].Message!;
		await Assert.That(captured.ToPlainText()).IsEqualTo("prefix body");
		await Assert.That(captured.Runs.Count).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	[Arguments(false, false, false)]
	[Arguments(false, true, true)]
	[Arguments(true, false, true)]
	public async Task TerminalPuppetPermissionIsSeparateFromPrivateForcedRelay(bool privateMessage, bool puppetOk, bool delivered)
	{
		var owner = await Player();
		var puppet = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "TerminalPuppet");
		var room = DBRef.Parse((await Admin("@dig " + Guid.NewGuid().ToString("N"))).Message!.ToPlainText().Trim());
		await Admin($"@tel {puppet}={room}");
		if (privateMessage) await Admin($"@tel {owner.DbRef}={room}");
		await Admin($"@chown {puppet}={owner.DbRef}");
		await Admin($"@set {puppet}=PUPPET !HALT");
		await SetRaw(puppet, "LISTEN", "*");
		await SetRaw(puppet, "AHEAR", "&HEARD me=yes");
		var pipeline = await Build(owner, owner.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.NotifyContextAsync(new NotificationContext(puppet, room, [])
		{
			Relay = NotificationRelay.NoRelay, PuppetOk = puppetOk
		}, MarkupText.Plain("body"), await Node(owner.DbRef), privateMessage
			? INotifyService.NotificationType.PrivateEmit : INotifyService.NotificationType.Emit);
		await Assert.That(ForwardedOutput(pipeline).Length).IsEqualTo(delivered ? 1 : 0);
		await Assert.That(pipeline.Queue).IsEmpty();
	}

	[Test]
	public async Task PublicContentsForwardingGrantsTerminalPuppetPermission()
	{
		var owner = await Player();
		var first = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PuppetOuter");
		var second = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PuppetInner");
		var puppet = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PuppetTerminal");
		await Admin($"@tel/inside {second}={first}");
		await Admin($"@tel/inside {puppet}={second}");
		await Admin($"@chown {puppet}={owner.DbRef}");
		await Admin($"@set {puppet}=PUPPET !HALT");
		await SetRaw(first, "LISTEN", "*");
		await SetRaw(second, "LISTEN", "*");
		var pipeline = await Build(owner, owner.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(first, "body", await Node(owner.DbRef), INotifyService.NotificationType.Emit);
		await Assert.That(ForwardedOutput(pipeline).Single()).EndsWith("> body");
	}

	[Test]
	[Arguments(false, false, "BLOCKED", true)]
	[Arguments(false, true, "BLOCKED", false)]
	[Arguments(true, false, "BLOCKED", true)]
	[Arguments(true, true, "BLOCKED", false)]
	public async Task StoredIncomingFilterFlagsChooseRegexAndCase(bool regexp, bool caseSensitive, string body, bool blocked)
	{
		var speaker = await Player();
		var recipient = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FlaggedFilter");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await SetRaw(listener, "LISTEN", "*");
		await SetRaw(listener, "INFILTER", regexp ? "^blocked$" : "blocked");
		if (regexp) await Admin($"@set {listener}/INFILTER=REGEXP");
		if (caseSensitive) await Admin($"@set {listener}/INFILTER=CASE");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(listener, body, await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline).Length).IsEqualTo(blocked ? 0 : 1);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ActualRoomEmitOriginAvoidsDuplicateForwardingAndHonorsOmit(bool omit)
	{
		var speaker = await Player();
		var recipient = await Player();
		var room = DBRef.Parse((await Admin("@dig " + Guid.NewGuid().ToString("N"))).Message!.ToPlainText().Trim());
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "RoomForwarder");
		await Admin($"@chown {room}={speaker.DbRef}");
		await Admin($"@tel {speaker.DbRef}={room}");
		await Admin($"@tel {listener}={room}");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await SetRaw(room, "LISTEN", "*");
		await SetRaw(listener, "LISTEN", "*");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		var command = omit ? $"@oemit {room}/{listener}=body" : $"@remit/silent {room}=body";
		await pipeline.Parser.CommandParse(MarkupText.Plain(command));
		await Assert.That(string.Join('|', ForwardedOutput(pipeline))).IsEqualTo(omit ? "" : "body");
	}

	[Test]
	public async Task StampedExclusionsSurviveNestedForwardingAndInputArrayMutation()
	{
		var speaker = await Player();
		var recipient = await Player();
		var first = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ExcludedOuter");
		var second = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ExcludedInner");
		await Admin($"@tel/inside {second}={first}");
		await Admin($"@tel/inside {recipient.DbRef}={second}");
		await SetRaw(first, "LISTEN", "*");
		await SetRaw(second, "LISTEN", "*");
		var excluded = new[] { recipient.DbRef };
		var context = new NotificationContext(first, speaker.DbRef, excluded);
		excluded[0] = new DBRef(recipient.DbRef.Number, -1);
		var pipeline = await Build(speaker, recipient.DbRef);
		await pipeline.Notify.NotifyContextAsync(context, MarkupText.Plain("body"), await Node(speaker.DbRef),
			INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline)).IsEmpty();
	}

	[Test]
	[Arguments(true, true, false, true, 1)]
	[Arguments(true, false, false, true, 0)]
	[Arguments(false, true, false, false, 0)]
	[Arguments(false, false, true, true, 0)]
	public async Task PlayerActionOptionsDoNotBecomeForwardingGates(bool playerListen, bool playerAHear,
		bool publicMessage, bool forwarded, int actions)
	{
		var speaker = await Player();
		var listener = await Player();
		var recipient = await Player();
		await Admin($"@tel/inside {recipient.DbRef}={listener.DbRef}");
		await SetRaw(listener.DbRef, "LISTEN", "*");
		await SetRaw(listener.DbRef, "AHEAR", "&HEARD me=yes");
		var pipeline = await Build(speaker, recipient.DbRef, playerListen, playerAHear);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(listener.DbRef, "body", await Node(speaker.DbRef), publicMessage
			? INotifyService.NotificationType.Emit : INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline).Length).IsEqualTo(forwarded ? 1 : 0);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(actions);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task HaltedListenerStillForwardsButMonitorAloneDoesNot(bool monitorOnly)
	{
		var speaker = await Player();
		var recipient = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "HaltedForwarder");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await Admin($"@set {listener}=MONITOR HALT");
		await SetRaw(listener, "PATTERN", "^*:&MONITORED me=yes");
		await SetRaw(listener, "AHEAR", "&HEARD me=yes");
		if (!monitorOnly) await SetRaw(listener, "LISTEN", "*");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(listener, "body", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline).Length).IsEqualTo(monitorOnly ? 0 : 1);
		await Assert.That(pipeline.Queue).IsEmpty();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InheritedPrefixUsesListenerCallerAndSpeakerEnactorWithLocalEmptyOverride(bool localEmpty)
	{
		var speaker = await Player();
		var recipient = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PrefixChild");
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PrefixParent");
		await Admin($"@parent {listener}={parent}");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await SetRaw(listener, "LISTEN", "*");
		await SetRaw(parent, "INPREFIX", "%!/%@/%#/%0");
		if (localEmpty) await SetRaw(listener, "INPREFIX", "");
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(listener, "body", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		var expected = localEmpty ? " body" : $"#{listener.Number}/#{listener.Number}/#{speaker.DbRef.Number}/body body";
		await Assert.That(string.Join('|', ForwardedOutput(pipeline))).IsEqualTo(expected);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ExplicitUnresolvableExecutorCannotBorrowSpeakerAuthority(bool stale)
	{
		var speaker = await Player();
		var recipient = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ExecutorForwarder");
		await Admin($"@tel/inside {recipient.DbRef}={listener}");
		await SetRaw(listener, "LISTEN", "*");
		var pipeline = await Build(speaker, recipient.DbRef);
		var executor = stale ? new DBRef(speaker.DbRef.Number, -1) : new DBRef(int.MaxValue);
		await pipeline.Notify.NotifyContextAsync(new NotificationContext(listener, speaker.DbRef, []) { Executor = executor },
			MarkupText.Plain("body"), await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline)).IsEmpty();
	}
}
