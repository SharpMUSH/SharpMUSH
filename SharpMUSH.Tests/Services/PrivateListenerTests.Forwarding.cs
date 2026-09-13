using MarkupString;
using NSubstitute;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	private static string[] ForwardedOutput(Pipeline pipeline) => pipeline.Bus.ReceivedCalls()
		.SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
		.Select(message => MarkupTextSerializer.Deserialize(message.Markup).ToPlainText()).ToArray();

	private static void RemoveOutputFraming(Pipeline pipeline)
	{
		var metadata = pipeline.Connections.Get(901)!.Metadata;
		metadata.TryRemove("OutputPrefix", out _);
		metadata.TryRemove("OutputSuffix", out _);
	}

	private async Task SetRaw(DBRef target, string name, string value)
		=> await Factory.Services.GetRequiredService<IAttributeService>().SetAttributeAsync(await Node(new DBRef(1)),
			await Node(target), name, MarkupText.Plain(value));

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task MatchingListenForwardsToMortalContentsIndependentlyOfListenLock(bool denyListen)
	{
		var speaker = await Player();
		var recipient = await Player();
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "ForwardingContainer");
		await Admin($"@tel/inside {recipient.DbRef}={container}");
		await Admin($"@listen {container}=heard *");
		await Admin($"@ahear {container}=&HEARD me=%0");
		if (denyListen) await Admin($"@lock/listen {container}=#FALSE");
		var pipeline = await Build(speaker, recipient.DbRef);
		await pipeline.Notify.Notify(container, "heard inside", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		var output = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
			.Select(message => MarkupTextSerializer.Deserialize(message.Markup).ToPlainText()).ToArray();
		await Assert.That(output.Any(message => message.Contains("heard inside", StringComparison.Ordinal))).IsTrue();
		await Assert.That(pipeline.Queue.Count).IsEqualTo(denyListen ? 0 : 1);
	}

	[Test]
	public async Task IncomingFilterLockReceivesMessageInActualAttributeEvaluation()
	{
		var speaker = await Player();
		var recipient = await Player();
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FilterLockContainer");
		await Admin($"@tel/inside {recipient.DbRef}={container}");
		await Admin($"@listen {container}=*");
		await Admin($"&CHECK {container}=%0");
		await Admin($"@lock/infilter {container}=CHECK/allowed");
		var pipeline = await Build(speaker, recipient.DbRef);
		await pipeline.Notify.Notify(container, "blocked", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())).IsEmpty();
		await pipeline.Notify.Notify(container, "allowed", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
			.Any(message => MarkupTextSerializer.Deserialize(message.Markup).ToPlainText().Contains("allowed", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	[Arguments(@"blocked\,text", "blocked,text", true)]
	[Arguments("(a,b)", "(a,b)", true)]
	[Arguments("[strcat(a,b)]", "[strcat(a,b)]", true)]
	[Arguments("[strcat(a,b)]", "ab", false)]
	[Arguments("%q<a,b>", "%q<a,b>", true)]
	[Arguments(" block", "block", false)]
	[Arguments(" block", " block", true)]
	[Arguments(">5", "6", true)]
	public async Task IncomingFiltersStayLiteralAndBlockOnlyMatchingMessages(string filter, string body, bool blocked)
	{
		var speaker = await Player();
		var recipient = await Player();
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LiteralFilter");
		await Admin($"@tel/inside {recipient.DbRef}={container}");
		await SetRaw(container, "LISTEN", "*");
		await SetRaw(container, "INFILTER", filter);
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(container, body, await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline).SequenceEqual(blocked ? [] : new[] { body })).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task InheritedFilterCanBeSuppressedByLocalEmptyAttribute(bool localEmpty)
	{
		var speaker = await Player();
		var recipient = await Player();
		var parent = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FilterParent");
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FilterChild");
		await Admin($"@parent {container}={parent}");
		await Admin($"@tel/inside {recipient.DbRef}={container}");
		await SetRaw(container, "LISTEN", "*");
		await SetRaw(parent, "INFILTER", "*");
		if (localEmpty) await SetRaw(container, "INFILTER", "");
		var pipeline = await Build(speaker, recipient.DbRef);
		await pipeline.Notify.Notify(container, "hello", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline).Length).IsEqualTo(localEmpty ? 1 : 0);
	}

	[Test]
	[Arguments("prefix", "")]
	[Arguments("", "")]
	[Arguments("prefix", "body")]
	public async Task ForwardedPromptClearsFramingAndRetainsSuccessfulPrefixSpacing(string prefix, string body)
	{
		var speaker = await Player();
		var recipient = await Player();
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "PromptForwarder");
		await Admin($"@tel/inside {recipient.DbRef}={container}");
		await SetRaw(container, "LISTEN", "*");
		await SetRaw(container, "INPREFIX", prefix);
		var pipeline = await Build(speaker, recipient.DbRef);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Prompt(container, body, await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(pipeline).SequenceEqual([prefix + " " + body])).IsTrue();
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupPromptMessage>())).IsEmpty();
	}

	[Test]
	public async Task NestedRelayReplacesPrefixUsesRawBodyAndStopsTerminalActions()
	{
		var speaker = await Player();
		var recipient = await Player();
		var first = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "FirstRelay");
		var second = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "SecondRelay");
		var terminal = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "TerminalRelay");
		await Admin($"@tel/inside {second}={first}");
		await Admin($"@tel/inside {terminal}={second}");
		await Admin($"@tel/inside {recipient.DbRef}={terminal}");
		foreach (var item in new[] { first, second, terminal })
		{
			await SetRaw(item, "LISTEN", "*");
			await SetRaw(item, "AHEAR", "&HEARD me=%0");
		}
		await SetRaw(first, "INPREFIX", "first");
		await SetRaw(second, "INPREFIX", "second:%0");
		await SetRaw(second, "CHECK", "[comp(%0,first body)]");
		await Admin($"@lock/infilter {second}=CHECK/0");
		using (LockEvaluationArguments.Enter(new Dictionary<string, MarkupText> { ["0"] = MarkupText.Plain("first body") }))
			await Assert.That(await Factory.Services.GetRequiredService<ILockService>().Evaluate(LockType.InFilter,
				await Node(second), await Node(speaker.DbRef))).IsTrue()
				.Because(SharpMUSH.Library.Services.LockService.Get(LockType.InFilter, await Node(second)));
		await Admin($"@set {terminal}=MONITOR");
		await SetRaw(terminal, "PATTERN", "^*:&MONITORED me=yes");
		var pipeline = await Build(speaker, terminal);
		RemoveOutputFraming(pipeline);
		await pipeline.Notify.Notify(first, "body", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Queue.Count).IsEqualTo(2);
		await Assert.That(string.Join('|', ForwardedOutput(pipeline))).IsEqualTo("second:body body");
		await Assert.That(pipeline.Queue.Select(item => item.State.Executor).SequenceEqual(new DBRef?[] { first, second })).IsTrue();
		var otherPipeline = await Build(speaker, recipient.DbRef);
		await otherPipeline.Notify.Notify(first, "body", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(ForwardedOutput(otherPipeline)).IsEmpty();
	}
}
