using System.Collections.Concurrent;
using System.Text;
using MarkupString;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Implementation.Handlers.ListenPattern;
using SharpMUSH.Library.Commands.ListenPattern;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.SchedulerModels;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Tests.Services;

public partial class PrivateListenerTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, "PrivateListen");
		_handles.Add(player.Handle);
		return player;
	}
	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task<CallState> Admin(string command) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
	private async Task<AnySharpObject> Node(DBRef reference) => (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();
	private sealed class Provider(IServiceProvider inner, params object[] replacements) : IServiceProvider
	{
		public object? GetService(Type type) => replacements.FirstOrDefault(type.IsInstanceOfType) ?? inner.GetService(type);
	}
	private sealed record FixedOptions(SharpMUSHOptions CurrentValue) : IOptionsWrapper<SharpMUSHOptions>;
	private sealed record Queued(MarkupText Command, ParserState State);
	private sealed record Pipeline(NotifyService Notify, IMessageBus Bus, ConnectionService Connections,
		MUSHCodeParser Parser, List<Queued> Queue, ITaskScheduler Scheduler);
	private async Task<Pipeline> Build(TestIsolationHelpers.TestPlayer actor, DBRef recipient,
		bool playerListen = true, bool playerAHear = true)
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		await connections.Register(901, "127.0.0.1", "localhost", "telnet", _ => ValueTask.CompletedTask,
			_ => ValueTask.CompletedTask, () => Encoding.UTF8, new ConcurrentDictionary<string, string>(
				new Dictionary<string, string> { ["SessionId"] = "private-listen", ["OutputPrefix"] = "PREFIX", ["OutputSuffix"] = "SUFFIX" }));
		await connections.Bind(901, recipient);
		var queue = new List<Queued>();
		var scheduler = Substitute.For<ITaskScheduler>();
		scheduler.AdmitCommandList(Arg.Any<MarkupText>(), Arg.Any<ParserState>()).Returns(call =>
		{
			queue.Add(new Queued(call.Arg<MarkupText>(), call.Arg<ParserState>()));
			return new QueueAdmissionResult(queue.Count, QueueRejectionReason.None);
		});
		var parser = (MUSHCodeParser)Factory.CommandParserFor(actor.DbRef, actor.Handle);
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = new FixedOptions(baseline with { Attribute = baseline.Attribute with { PlayerListen = playerListen, PlayerAHear = playerAHear } });
		var provider = new Provider(Factory.Services, scheduler, options);
		var handler = new ExecuteListenPatternCommandHandler(provider, Factory.Services.GetRequiredService<IAttributeService>(),
			NullLogger<ExecuteListenPatternCommandHandler>.Instance);
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<GetObjectNodeQuery>(), Arg.Any<CancellationToken>())
			.Returns(call => Mediator.Send(call.Arg<GetObjectNodeQuery>(), call.Arg<CancellationToken>()));
		mediator.Send(Arg.Any<ExecuteListenPatternCommand>(), Arg.Any<CancellationToken>())
			.Returns(call => handler.Handle(call.Arg<ExecuteListenPatternCommand>(), call.Arg<CancellationToken>()));
		var routing = new ListenerRoutingService(mediator, new ListenPatternMatcher(Mediator, options),
			Factory.Services.GetRequiredService<IPermissionService>(), Factory.Services.GetRequiredService<ILockService>(),
			connections, provider, bus, DisabledRealityPolicy.Instance);
		var notify = new NotifyService(bus, connections, new LocalizationService(), DisabledRealityPolicy.Instance, routing, mediator);
		var communication = ActivatorUtilities.CreateInstance<CommunicationService>(Factory.Services, notify, connections);
		var functions = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Functions.Functions>(Factory.Services, notify, communication);
		var commands = ActivatorUtilities.CreateInstance<SharpMUSH.Implementation.Commands.Commands>(Factory.Services, notify, communication, functions);
		parser = parser with { CommandLibrary = commands.Get(), FunctionLibrary = functions.Get() };
		return new Pipeline(notify, bus, connections, parser, queue, scheduler);
	}

	[Test]
	[Arguments("pemit", false)]
	[Arguments("nspemit", false)]
	[Arguments("prompt", false)]
	[Arguments("nsprompt", false)]
	[Arguments("pemit", true)]
	[Arguments("nspemit", true)]
	[Arguments("prompt", true)]
	[Arguments("nsprompt", true)]
	public async Task PrivateSurfacesQueueCapturedListenerCommands(string name, bool function)
	{
		var actor = await Player();
		var listener = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "CapturedListener");
		if (name.StartsWith("ns", StringComparison.Ordinal)) await Admin($"@power {actor.DbRef}=Can_Spoof");
		await Admin($"@listen {listener}=hello * and *");
		await Admin($"@ahear {listener}=&CAPTURE me=%0|%1|%#|%!|%@; &SECOND me=ran");
		var pipeline = await Build(actor, actor.DbRef);
		if (function) await pipeline.Parser.FunctionParse(MarkupText.Plain($"[{name}({listener},hello one and two)]"));
		else await pipeline.Parser.CommandParse(actor.Handle, Connections, MarkupText.Plain($"@{name}/silent {listener}=hello one and two"));
		await Assert.That(pipeline.Queue.Count).IsEqualTo(1);
		var queued = pipeline.Queue.Single();
		await Assert.That(queued.State.Executor).IsEqualTo(listener);
		await Assert.That(queued.State.Enactor).IsEqualTo(actor.DbRef);
		await Assert.That(queued.State.Caller).IsEqualTo(actor.DbRef);
		await Assert.That(queued.State.EnvironmentRegisters["0"].Message!.ToPlainText()).IsEqualTo("one");
		await Assert.That(queued.State.EnvironmentRegisters["1"].Message!.ToPlainText()).IsEqualTo("two");
		await Admin($"@ahear {listener}=&CAPTURE me=changed");
		await pipeline.Parser.FromState(queued.State).CommandListParse(queued.Command);
		var captured = await Factory.Services.GetRequiredService<IAttributeService>().GetAttributeAsync(await Node(listener), await Node(listener),
			"CAPTURE", IAttributeService.AttributeMode.Read, false);
		await Assert.That(captured.Expect<SharpAttribute[]>().Last().Value.ToPlainText())
			.IsEqualTo($"one|two|#{actor.DbRef.Number}|#{listener.Number}|#{actor.DbRef.Number}");
		var second = await Factory.Services.GetRequiredService<IAttributeService>().GetAttributeAsync(await Node(listener), await Node(listener),
			"SECOND", IAttributeService.AttributeMode.Read, false);
		await Assert.That(second.Expect<SharpAttribute[]>().Last().Value.ToPlainText()).IsEqualTo("ran");
	}

	[Test]
	[Arguments("dbref")]
	[Arguments("handle")]
	[Arguments("array")]
	public async Task EmptyPromptPublishesActualProtocolMessage(string route)
	{
		var actor = await Player();
		var pipeline = await Build(actor, actor.DbRef);
		if (route == "dbref") await pipeline.Notify.Prompt(actor.DbRef, MarkupText.Empty, await Node(actor.DbRef));
		else if (route == "handle") await pipeline.Notify.Prompt(901L, MarkupText.Empty, await Node(actor.DbRef));
		else await pipeline.Notify.Prompt(new long[] { 901 }, MarkupText.Empty, await Node(actor.DbRef));
		var prompt = pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupPromptMessage>()).Single();
		await Assert.That(MarkupTextSerializer.Deserialize(prompt.Markup).ToPlainText()).IsEmpty();
		await Assert.That(pipeline.Bus.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())).IsEmpty();
		await AssertPromptConsumer(prompt, "");
	}

	private static async Task AssertPromptConsumer(MarkupPromptMessage prompt, string expected)
	{
		var writes = new List<byte[]>();
		var outputWrites = 0;
		var connections = Substitute.For<SharpMUSH.ConnectionServer.Services.IConnectionServerService>();
		var client = new SharpMUSH.ConnectionServer.Services.ConnectionServerService.ConnectionData(prompt.Handle, null,
			SharpMUSH.ConnectionServer.Services.ConnectionServerService.ConnectionState.Connected,
			_ => { outputWrites++; return ValueTask.CompletedTask; }, bytes => { writes.Add(bytes); return ValueTask.CompletedTask; },
			() => Encoding.UTF8, () => { }, null, new SharpMUSH.ConnectionServer.Models.ProtocolCapabilities(), null, "telnet");
		connections.Get(prompt.Handle).Returns(client);
		var transform = Substitute.For<SharpMUSH.ConnectionServer.Services.IOutputTransformService>();
		transform.TransformAsync(Arg.Any<byte[]>(), Arg.Any<SharpMUSH.ConnectionServer.Models.ProtocolCapabilities>(),
			Arg.Any<SharpMUSH.ConnectionServer.Models.PlayerOutputPreferences?>(), Arg.Any<CancellationToken>())
			.Returns(call => ValueTask.FromResult(call.Arg<byte[]>()));
		await new SharpMUSH.ConnectionServer.Consumers.MarkupPromptConsumer(connections,
			new SharpMUSH.ConnectionServer.Services.MarkupOutputRenderer(), transform,
			NullLogger<SharpMUSH.ConnectionServer.Consumers.MarkupPromptConsumer>.Instance).HandleAsync(prompt);
		await Assert.That(writes.Count).IsEqualTo(1);
		await Assert.That(Encoding.UTF8.GetString(writes.Single())).IsEqualTo(expected);
		await Assert.That(outputWrites).IsEqualTo(0);
	}
}
