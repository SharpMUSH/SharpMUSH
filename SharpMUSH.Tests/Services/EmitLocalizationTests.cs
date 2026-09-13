using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using System.Collections.Concurrent;
using System.Text;

namespace SharpMUSH.Tests.Services;

[NotInParallel]
public class EmitLocalizationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private readonly List<long> _handles = [];
	private async Task<TestIsolationHelpers.TestPlayer> Player(string prefix)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, prefix);
		_handles.Add(player.Handle);
		return player;
	}

	[After(Test)]
	public async Task DisconnectPlayers()
	{
		foreach (var handle in _handles) await Connections.Disconnect(handle);
	}
	private async Task Command(string command) => await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
	private async Task<AnySharpObject> Node(DBRef reference) => (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();

	private async Task<(ICommunicationService Service, IMessageBus Bus, IDidItService DidIt)> Pipeline(DBRef actor, DBRef? observer = null)
	{
		var bus = Substitute.For<IMessageBus>();
		var connections = new ConnectionService(Substitute.For<IPublisher>());
		foreach (var (handle, locale) in new[] { (701L, "en"), (702L, "fr") })
		{
			await connections.Register(handle, "127.0.0.1", "localhost", "telnet",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
				new ConcurrentDictionary<string, string>(new Dictionary<string, string> { ["Locale"] = locale }));
			await connections.Bind(handle, actor);
		}
		if (observer is { } watcher)
		{
			await connections.Register(703, "127.0.0.1", "localhost", "telnet",
				_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8,
				new ConcurrentDictionary<string, string>());
			await connections.Bind(703, watcher);
		}
		var notify = new NotifyService(bus, connections, new LocalizationService(), DisabledRealityPolicy.Instance,
			httpOutputCapture: new HttpOutputCapture());
		IDidItService? didIt = null;
		var service = ActivatorUtilities.CreateInstance<CommunicationService>(Factory.Services,
			notify, connections, new Lazy<IDidItService>(() => didIt!));
		didIt = new DidItService(Mediator, Factory.Services.GetRequiredService<IAttributeService>(), notify, service);
		return (service, bus, didIt);
	}

	private static string[] Messages(IMessageBus bus, long handle) => bus.ReceivedCalls()
		.SelectMany(call => call.GetArguments().OfType<MarkupOutputMessage>())
		.Where(message => message.Handle == handle)
		.Select(message => MarkupTextSerializer.Deserialize(message.Markup).ToPlainText()).ToArray();

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task PrivateRefusalUsesEachConnectionLocale(bool haven)
	{
		var actor = await Player("LocaleActor");
		var target = await Player("LocaleTarget");
		await Command(haven ? $"@set {target.DbRef}=HAVEN" : $"@lock/page {target.DbRef}=#FALSE");
		var (service, bus, _) = await Pipeline(actor.DbRef);
		await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
			new EmitRequest(EmitScope.Private, MarkupText.Plain("blocked"), [target.DbRef.ToString()], Silent: true));
		var name = (await Node(target.DbRef)).Object().Name;
		await Assert.That(Messages(bus, 701)).Contains($"I'm sorry, but {name} wishes to be left alone now.");
		await Assert.That(Messages(bus, 702)).Contains($"Désolé, mais {name} souhaite rester seul pour le moment.");
	}

	[Test]
	[Arguments("invalid", "'invalid' is not a port number.", "'invalid' n'est pas un numéro de port.")]
	[Arguments("0", "'0' is not a port number.", "'0' n'est pas un numéro de port.")]
	[Arguments("999999", "That port is not active.", "Ce port n'est pas actif.")]
	public async Task PortErrorsUseEachConnectionLocale(string target, string english, string french)
	{
		var god = await Node(new DBRef(1));
		var (service, bus, _) = await Pipeline(god.Object().DBRef);
		await service.EmitAsync(Factory.CommandParser, new EmitRequest(EmitScope.Private, MarkupText.Plain("blocked"), [target], PortTargets: true));
		await Assert.That(Messages(bus, 701)).Contains(english);
		await Assert.That(Messages(bus, 702)).Contains(french);
	}

	[Test]
	public async Task OmitNoMatchesUsesEachConnectionLocale()
	{
		var actor = await Player("LocaleOmit");
		var (service, bus, _) = await Pipeline(actor.DbRef);
		await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
			new EmitRequest(EmitScope.Omit, MarkupText.Plain("blocked"), [Guid.NewGuid().ToString("N")]));
		await Assert.That(Messages(bus, 701)).Contains("No matching objects.");
		await Assert.That(Messages(bus, 702)).Contains("Aucun objet correspondant.");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OtherAndActionFailuresRetainLocalizedDefault(bool capture)
	{
		var actor = await Player("LocaleTriad");
		var target = await Player("LocaleTriadLock");
		var observer = await Player("LocaleWitness");
		var room = await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain($"@dig {Guid.NewGuid():N}"));
		foreach (var player in new[] { actor, target, observer }) await Command($"@tel {player.DbRef}={room.Message}");
		await Command($"@lock/page {target.DbRef}=#FALSE");
		await Command($"&PAGE_LOCK`OFAILURE {target.DbRef}=is refused.");
		await Command($"&PAGE_LOCK`AFAILURE {target.DbRef}=&MARKED me=yes");
		var (service, bus, _) = await Pipeline(actor.DbRef, observer.DbRef);
		var context = new HttpResponseContext();
		using (capture ? new HttpOutputCapture().BeginCapture(actor.DbRef.Number, context) : null)
		{
			await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
				new EmitRequest(EmitScope.Private, MarkupText.Plain("blocked"), [target.DbRef.ToString()], Silent: true));
		}
		await Factory.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests();
		var marked = await Factory.CommandParser.FunctionParse(MarkupText.Plain($"[get({target.DbRef}/MARKED)]"));
		await Assert.That(marked!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
		await Assert.That(Messages(bus, 703)).Contains($"{(await Node(actor.DbRef)).Object().Name} is refused.");
		var name = (await Node(target.DbRef)).Object().Name;
		if (capture)
		{
			await Assert.That(context.Body.ToString()).IsEqualTo($"I'm sorry, but {name} wishes to be left alone now.\n");
			await Assert.That(Messages(bus, 701)).IsEmpty();
			await Assert.That(Messages(bus, 702)).IsEmpty();
		}
		else
		{
			await Assert.That(Messages(bus, 701)).Contains($"I'm sorry, but {name} wishes to be left alone now.");
			await Assert.That(Messages(bus, 702)).Contains($"Désolé, mais {name} souhaite rester seul pour le moment.");
		}
	}

	[Test]
	[Arguments(EmitScope.Immediate, "You may not speak here!", "Vous ne pouvez pas parler ici !")]
	[Arguments(EmitScope.Room, "You may not speak there!", "Vous ne pouvez pas parler là !")]
	public async Task SpeechFailureUsesEachConnectionLocale(EmitScope scope, string english, string french)
	{
		var actor = await Player("LocaleSpeech");
		var container = await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LocaleContainer");
		await Command($"@tel {actor.DbRef}={container}");
		await Command($"@lock/speech {container}=#FALSE");
		var (service, bus, _) = await Pipeline(actor.DbRef);
		await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
			new EmitRequest(scope, MarkupText.Plain("blocked"), [container.ToString()], Silent: true));
		await Assert.That(Messages(bus, 701)).Contains(english);
		await Assert.That(Messages(bus, 702)).Contains(french);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OmitLimitUsesEachConnectionLocale(bool explicitLocation)
	{
		var actor = await Player("LocaleLimit");
		var (service, bus, _) = await Pipeline(actor.DbRef);
		var location = await (await Node(actor.DbRef)).Where();
		await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
			new EmitRequest(EmitScope.Omit, MarkupText.Plain("blocked"), Enumerable.Repeat(actor.DbRef.ToString(), 11).ToArray(),
				OmitLocation: explicitLocation ? location.Object().DBRef.ToString() : null));
		await Assert.That(Messages(bus, 701)).Contains("Too many people to oemit to.");
		await Assert.That(Messages(bus, 702)).Contains("Trop de destinataires pour oemit.");
	}

	[Test]
	[Arguments("custom failure", false)]
	[Arguments("", false)]
	[Arguments("", true)]
	public async Task PresentFailureSuppressesDefault(string failure, bool inherited)
	{
		var actor = await Player("LocaleCustom");
		var target = await Player("LocaleLock");
		await Command($"@lock/page {target.DbRef}=#FALSE");
		var holder = inherited ? await TestIsolationHelpers.CreateTestThingAsync(Factory.CommandParser, Connections, "LocaleParent") : target.DbRef;
		if (inherited) await Command($"@parent {target.DbRef}={holder}");
		await Mediator.Send(new SetAttributeCommand(holder, ["PAGE_LOCK", "FAILURE"], MarkupText.Plain(failure), (await Node(new DBRef(1))).Expect<SharpPlayer>()));
		var attribute = await Factory.Services.GetRequiredService<IAttributeService>().GetAttributeAsync(
			await Node(target.DbRef), await Node(target.DbRef), "PAGE_LOCK`FAILURE", IAttributeService.AttributeMode.Execute, parent: true);
		await Assert.That(attribute.IsAttribute).IsTrue();
		var (service, bus, _) = await Pipeline(actor.DbRef);
		await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
			new EmitRequest(EmitScope.Private, MarkupText.Plain("blocked"), [target.DbRef.ToString()], Silent: true));
		await Assert.That(Messages(bus, 701)).IsEquivalentTo(failure.Length == 0 ? [] : new[] { failure });
		await Assert.That(Messages(bus, 702)).IsEquivalentTo(failure.Length == 0 ? [] : new[] { failure });
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ListSuppressesDefaultButRetainsCustomFailure(bool custom)
	{
		var actor = await Player("LocaleList");
		var target = await Player("LocaleListLock");
		await Command($"@lock/page {target.DbRef}=#FALSE");
		if (custom) await Command($"&PAGE_LOCK`FAILURE {target.DbRef}=custom list failure");
		var (service, bus, _) = await Pipeline(actor.DbRef);
		await service.EmitAsync(Factory.CommandParserFor(actor.DbRef, actor.Handle),
			new EmitRequest(EmitScope.Private, MarkupText.Plain("blocked"), [target.DbRef.ToString()], Silent: true, List: true));
		await Assert.That(Messages(bus, 701)).IsEquivalentTo(custom ? new[] { "custom list failure" } : []);
		await Assert.That(Messages(bus, 702)).IsEquivalentTo(custom ? new[] { "custom list failure" } : []);
	}

	[Test]
	public async Task LiteralDefaultRetainsPrecedenceOverLocalizedDefault()
	{
		var actor = await Player("LocaleLegacy");
		var (_, bus, didIt) = await Pipeline(actor.DbRef);
		var used = await didIt.DidIt(Factory.CommandParser, new DidItRequest(await Node(actor.DbRef), await Node(actor.DbRef),
			What: "UNSET_FAILURE", Def: MarkupText.Plain("literal fallback"))
		{
			DefaultNotification = new LocalizedNotification(nameof(ErrorMessages.Notifications.MayNotSpeakHere))
		});
		await Assert.That(used).IsFalse();
		await Assert.That(Messages(bus, 701)).IsEquivalentTo(new[] { "literal fallback" });
		await Assert.That(Messages(bus, 702)).IsEquivalentTo(new[] { "literal fallback" });
	}
}
