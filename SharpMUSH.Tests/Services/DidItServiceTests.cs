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
}
