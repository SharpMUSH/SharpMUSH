using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class DidItServiceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private Mediator.IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<Mediator.IMediator>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IPermissionService PermissionService => WebAppFactoryArg.Services.GetRequiredService<IPermissionService>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private async Task<AnySharpObject> Node(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Known;

	private async Task<DBRef> Thing(string prefix)
		=> await TestIsolationHelpers.CreateTestThingAsync(GodParser, ConnectionService, prefix);

	[Test]
	public async ValueTask PlainThingIsNotAHearer()
	{
		var thing = await Thing("Deaf");
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsFalse();
	}

	[Test]
	public async ValueTask ThingWithListenIsAHearer()
	{
		var thing = await Thing("Ears");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&LISTEN {thing}=*"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsTrue();
	}

	[Test]
	public async ValueTask PuppetIsAHearer()
	{
		var thing = await Thing("Puppet");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {thing}=PUPPET"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsTrue();
	}

	[Test]
	public async ValueTask AudibleWithForwardListIsAHearer()
	{
		var thing = await Thing("Loud");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {thing}=AUDIBLE"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&FORWARDLIST {thing}=#1"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsTrue();
	}

	[Test]
	public async ValueTask ForwardListWithoutAudibleIsNotAHearer()
	{
		var thing = await Thing("Quiet");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&FORWARDLIST {thing}=#1"));
		await Assert.That(await PermissionService.IsHearer(await Node(thing))).IsFalse();
	}

	[Test]
	public async ValueTask ListenIsNotInheritedFromAParent()
	{
		var parent = await Thing("HearParent");
		var child = await Thing("HearChild");

		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"&LISTEN {parent}=*"));
		await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@parent {child}={parent}"));

		// The parent hears; the child does not inherit that.
		await Assert.That(await PermissionService.IsHearer(await Node(parent))).IsTrue();
		await Assert.That(await PermissionService.IsHearer(await Node(child))).IsFalse();
	}

	[Test]
	public async ValueTask HearingHonoursTheInteractLock()
	{
		var speaker = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InteractSpeaker");
		var deaf = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InteractDeaf");

		// An interact lock nobody passes.
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@lock/interact {deaf.DbRef}=#0"));

		await Assert.That(await PermissionService.CanInteract(
			await Node(speaker.DbRef), await Node(deaf.DbRef), IPermissionService.InteractType.Hear))
			.IsFalse();
	}

	[Test]
	public async ValueTask PresenceAndSightDoNotConsultTheInteractLock()
	{
		var mover = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InteractMover");
		var locked = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "InteractLocked");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@lock/interact {locked.DbRef}=#0"));

		// PennMUSH can_interact gates the Interact lock on INTERACT_HEAR alone, so an arrival
		// message (Presence) and an OXLEAVE (See) still reach a locked object.
		await Assert.That(await PermissionService.CanInteract(
			await Node(mover.DbRef), await Node(locked.DbRef), IPermissionService.InteractType.Presence))
			.IsTrue();

		await Assert.That(await PermissionService.CanInteract(
			await Node(mover.DbRef), await Node(locked.DbRef), IPermissionService.InteractType.See))
			.IsTrue();
	}

	private IDidItService DidItService => WebAppFactoryArg.Services.GetRequiredService<IDidItService>();

	private async Task<List<string>> MessagesWhile(DBRef who, Func<Task> action)
	{
		var recorder = WebAppFactoryArg.Notifications;
		var before = recorder.CountFor(who);
		await action();
		return [.. recorder.For(who).Skip(before)];
	}

	/// <summary>Digs a fresh room and teleports every player into it, silently.</summary>
	private async Task<AnySharpContainer> DigAndGather(params TestIsolationHelpers.TestPlayer[] players)
	{
		var roomName = TestIsolationHelpers.GenerateUniqueName("DidItRoom");
		var dig = await GodParser.CommandParse(1, ConnectionService, MarkupText.Plain($"@dig {roomName}"));
		var roomRef = dig.Message!.ToPlainText().Trim();

		foreach (var player in players)
		{
			await GodParser.CommandParse(1, ConnectionService,
				MarkupText.Plain($"@teleport/silent {player.DbRef}={roomRef}"));
		}

		var parsed = DBRef.TryParse(roomRef, out var dbref)
			? dbref!.Value
			: throw new InvalidOperationException($"@dig did not return a dbref: {roomRef}");

		return (await Mediator.Send(new GetObjectNodeQuery(parsed))).Known.AsContainer;
	}

	[Test]
	public async ValueTask WhatGoesToThePlayerAndIsEvaluated()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItWhat");
		var thing = await Thing("WhatHolder");
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&GREET {thing}=Hello [name(%#)]."));

		var messages = await MessagesWhile(actor.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(actor.DbRef),
				Thing: await Node(thing),
				What: "GREET")));

		await Assert.That(messages.Any(m => m.Contains("Hello") && m.Contains(actor.Name))).IsTrue();
	}

	[Test]
	public async ValueTask DefIsUsedWhenTheAttributeIsAbsent()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItDef");
		var thing = await Thing("NoGreet");

		var messages = await MessagesWhile(actor.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(actor.DbRef),
				Thing: await Node(thing),
				What: "GREET",
				Def: MarkupText.Plain("Nothing happens."))));

		await Assert.That(messages.Any(m => m.Contains("Nothing happens."))).IsTrue();
	}

	[Test]
	public async ValueTask ODefIsPrefixedWithTheActorsName()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItActor");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItWatcher");
		var roomDbRef = await DigAndGather(actor, watcher);
		var thing = await Thing("ODefHolder");

		var messages = await MessagesWhile(watcher.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(actor.DbRef),
				Thing: await Node(thing),
				OWhat: "OGREET",
				ODef: "waves.",
				Loc: roomDbRef)));

		await Assert.That(messages.Any(m => m == $"{actor.Name} waves.")).IsTrue();
	}

	[Test]
	public async ValueTask OWhatIsEvaluatedOncePerCallNotOncePerListener()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItOnceActor");
		var watcherA = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItOnceA");
		var watcherB = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItOnceB");
		var roomDbRef = await DigAndGather(actor, watcherA, watcherB);
		var thing = await Thing("CounterHolder");

		// The o-message increments a counter on the holder as a side effect. If the primitive
		// evaluated per listener the counter would reach 2, and both watchers would see the
		// message the OTHER listener's evaluation produced.
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&COUNT {thing}=0"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&OTICK {thing}=[set({thing},COUNT:[add(get({thing}/COUNT),1)])]ticks."));

		await DidItService.DidIt(GodParser, new DidItRequest(
			Player: await Node(actor.DbRef),
			Thing: await Node(thing),
			OWhat: "OTICK",
			Loc: roomDbRef));

		var count = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/COUNT)]"));
		await Assert.That(count!.Message!.ToPlainText().Trim()).IsEqualTo("1");
	}

	[Test]
	public async ValueTask ADarkLegalActorProducesNoOMessageButStillActs()
	{
		var wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItDark");
		var watcher = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "DidItDarkWatch");
		var roomDbRef = await DigAndGather(wizard, watcher);
		var thing = await Thing("DarkHolder");

		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {wizard.DbRef}=WIZARD"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@set {wizard.DbRef}=DARK"));
		await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"&AMARK {thing}=&MARKED me=yes"));

		var messages = await MessagesWhile(watcher.DbRef, async () =>
			await DidItService.DidIt(GodParser, new DidItRequest(
				Player: await Node(wizard.DbRef),
				Thing: await Node(thing),
				OWhat: "OMARK",
				ODef: "sneaks.",
				AWhat: "AMARK",
				Loc: roomDbRef)));

		await Assert.That(messages.Any(m => m.Contains("sneaks."))).IsFalse();

		await WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>()
			.DrainImmediateQueueForTests();

		var marked = await GodParser.FunctionParse(MarkupText.Plain($"[get({thing}/MARKED)]"));
		await Assert.That(marked!.Message!.ToPlainText().Trim()).IsEqualTo("yes");
	}
}
