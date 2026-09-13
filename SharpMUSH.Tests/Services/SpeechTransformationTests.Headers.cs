using System.Collections.Concurrent;
using System.Text;
using MarkupString;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public partial class SpeechTransformationTests
{
	private async Task<AnySharpObject> Node(DBRef reference)
		=> (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();
	private async Task Flag(DBRef reference, string name)
	{
		var flag = await Mediator.Send(new GetObjectFlagQuery(name));
		await Assert.That(flag).IsNotNull();
		await Mediator.Send(new SetObjectFlagCommand(await Node(reference), flag!));
	}
	private async Task<(NotifyService Notify, IMessageBus Bus, ConnectionService Connections)> NotificationPipeline(DBRef recipient,
		IListenerRoutingService? routing = null, IHttpOutputCapture? capture = null)
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		await connections.Register(902, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>(
				new Dictionary<string, string> { ["SessionId"] = "speech-session" }));
		await connections.Bind(902, recipient);
		return (new NotifyService(bus, connections, new LocalizationService(), DisabledRealityPolicy.Instance, routing, Mediator, capture), bus, connections);
	}
	private static string[] Output(IMessageBus bus) => bus.ReceivedCalls()
		.SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
		.Select(message => MarkupTextSerializer.Deserialize(message.Markup).ToPlainText()).ToArray();

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task NoSpoofHeadersArePreparedForTheRecipient(bool paranoid)
	{
		var speaker = await Player();
		var recipient = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		if (paranoid) await Flag(recipient.DbRef, "PARANOID");
		await Assert.That(await (await Node(recipient.DbRef)).HasFlag("NOSPOOF")).IsTrue();
		var pipeline = await NotificationPipeline(recipient.DbRef);
		await pipeline.Notify.Notify(recipient.DbRef, "hello", await Node(speaker.DbRef), INotifyService.NotificationType.PrivateEmit);
		var name = paranoid ? $"{speaker.Name}(#{speaker.DbRef.Number})" : $"{speaker.Name}:";
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo($"[{name}] hello");
	}

	[Test]
	[Arguments(INotifyService.NotificationType.NSEmit)]
	[Arguments(INotifyService.NotificationType.NSSay)]
	[Arguments(INotifyService.NotificationType.NSPose)]
	[Arguments(INotifyService.NotificationType.NSSemiPose)]
	[Arguments(INotifyService.NotificationType.NSPrivateEmit)]
	[Arguments(INotifyService.NotificationType.NSAnnounce)]
	[Arguments(INotifyService.NotificationType.Announce)]
	public async Task AuthorizedNoSpoofTypesAndAdministrativeAnnouncementsDoNotAddHeaders(INotifyService.NotificationType type)
	{
		var actor = await Player();
		var recipient = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		await Flag(recipient.DbRef, "PARANOID");
		var pipeline = await NotificationPipeline(recipient.DbRef);
		await pipeline.Notify.Notify(recipient.DbRef, "hello", await Node(actor.DbRef), type);
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo("hello");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task SelfHeadersRequireBothNoSpoofAndParanoid(bool noSpoof, bool paranoid)
	{
		var actor = await Player();
		if (noSpoof) await Flag(actor.DbRef, "NOSPOOF");
		if (paranoid) await Flag(actor.DbRef, "PARANOID");
		var pipeline = await NotificationPipeline(actor.DbRef);
		await pipeline.Notify.Notify(actor.DbRef, "hello", await Node(actor.DbRef), INotifyService.NotificationType.Emit);
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo(noSpoof && paranoid ? $"[{actor.Name}(#{actor.DbRef.Number})] hello" : "hello");
	}

	[Test]
	public async Task RawListenerAndHttpCaptureInputPrecedeHeaders()
	{
		var actor = await Player();
		var recipient = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		var routing = Substitute.For<IListenerRoutingService>();
		var pipeline = await NotificationPipeline(recipient.DbRef, routing);
		await pipeline.Notify.Notify(recipient.DbRef, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await routing.Received().ProcessNotificationAsync(Arg.Any<NotificationContext>(), TestHelpers.MatchingMessage("hello"),
			TestHelpers.MatchingObject(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo($"[{actor.Name}:] hello");
		var capture = Substitute.For<IHttpOutputCapture>();
		capture.TryCapture(recipient.DbRef.Number, "raw").Returns(true);
		var captured = await NotificationPipeline(recipient.DbRef, routing, capture);
		await captured.Notify.Notify(recipient.DbRef, "raw", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(Output(captured.Bus)).IsEmpty();
		capture.Received().TryCapture(recipient.DbRef.Number, "raw");
	}

	[Test]
	[Arguments("object")]
	[Arguments("handle")]
	[Arguments("handles")]
	public async Task EmptyPromptRemainsEmptyForParanoidRecipient(string route)
	{
		var actor = await Player();
		var recipient = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		await Flag(recipient.DbRef, "PARANOID");
		var pipeline = await NotificationPipeline(recipient.DbRef);
		var speaker = await Node(actor.DbRef);
		if (route == "object") await pipeline.Notify.Prompt(recipient.DbRef, MarkupText.Empty, speaker, INotifyService.NotificationType.PrivateEmit);
		else if (route == "handle") await pipeline.Notify.Prompt(902L, MarkupText.Empty, speaker, INotifyService.NotificationType.PrivateEmit);
		else await pipeline.Notify.Prompt([902L], MarkupText.Empty, speaker, INotifyService.NotificationType.PrivateEmit);
		var prompt = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupPromptMessage>()).Single();
		await Assert.That(MarkupTextSerializer.Deserialize(prompt.Markup).Length).IsEqualTo(0);
		await PrivateListenerTests.AssertPromptConsumer(prompt, "", "speech-session");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PrivateCommandSuppressesHeadersOnlyWhenAuthorized(bool authorized)
	{
		var actor = await Player();
		var recipient = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		if (authorized) await Admin($"@power {actor.DbRef}=Can_Spoof");
		var pipeline = await NotificationPipeline(recipient.DbRef);
		var communication = ActivatorUtilities.CreateInstance<CommunicationService>(Factory.Services, pipeline.Notify);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services, pipeline.Notify, communication);
		var parser = (MUSHCodeParser)Factory.CommandParserFor(actor.DbRef, actor.Handle) with { CommandLibrary = commands.Get() };
		await parser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@nspemit/silent {recipient.DbRef}=hello"));
		await Assert.That(string.Join("|", Output(pipeline.Bus))).IsEqualTo(authorized ? "hello" : $"[{actor.Name}:] hello");
	}

	[Test]
	[Arguments(false, false)]
	[Arguments(true, false)]
	[Arguments(false, true)]
	[Arguments(true, true)]
	public async Task RebindingDuringHeaderReadsNeverDeliversTheOldRecipientHeader(bool prompt, bool session)
	{
		var actor = await Player();
		var recipient = await Player();
		var replacement = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		var pipeline = await NotificationPipeline(recipient.DbRef);
		var mediator = Substitute.For<IMediator>();
		async ValueTask<AnyOptionalSharpObject> Rebind(GetObjectNodeQuery query, CancellationToken token)
		{
			if (session) pipeline.Connections.Get(902)!.Metadata["SessionId"] = "replacement";
			else await pipeline.Connections.Bind(902, replacement.DbRef);
			return await Mediator.Send(query, token);
		}
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Rebind(call.Arg<GetObjectNodeQuery>(), call.Arg<CancellationToken>()));
		var notify = new NotifyService(pipeline.Bus, pipeline.Connections, new LocalizationService(), DisabledRealityPolicy.Instance, mediator: mediator);
		if (prompt) await notify.Prompt(902L, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		else await notify.Notify(902L, "hello", await Node(actor.DbRef), INotifyService.NotificationType.PrivateEmit);
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments()).OfType<IHandleMessage>()).IsEmpty();
	}

	[Test]
	public async Task HandleArrayFormatsEachRecipientAndRetainsBodyMarkup()
	{
		var actor = await Player();
		var recipient = await Player();
		var plainRecipient = await Player();
		await Flag(recipient.DbRef, "NOSPOOF");
		var pipeline = await NotificationPipeline(recipient.DbRef);
		await pipeline.Connections.Register(903, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>());
		await pipeline.Connections.Bind(903, plainRecipient.DbRef);
		var body = (await Factory.CommandParser.FunctionParse(MarkupText.Plain("[ansi(r,hello)]")))!.Message!;
		await pipeline.Notify.Notify([902L, 903L], body, await Node(actor.DbRef), INotifyService.NotificationType.Emit);
		var output = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>()).ToDictionary(message => message.Handle);
		await Assert.That(output[902].SessionId).IsEqualTo("speech-session");
		await Assert.That(MarkupTextSerializer.Deserialize(output[902].Markup).Render(MarkupFormat.Ansi))
			.IsEqualTo(MarkupText.Concat(MarkupText.Plain($"[{actor.Name}:] "), body).Render(MarkupFormat.Ansi));
		await Assert.That(MarkupTextSerializer.Deserialize(output[903].Markup).Render(MarkupFormat.Ansi)).IsEqualTo(body.Render(MarkupFormat.Ansi));
	}
}
