using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Snapshots;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Services.Snapshots;

namespace SharpMUSH.Tests.Database;

public class ObjectSnapshotTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	private async Task<(CapabilityActor Actor, DBRef Target, SharpPlayer Player)> Setup()
	{
		var player = (await Get<IObjectStore>().GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var actor = await Get<IAdministrativeCapabilityService>().GetGameActorAsync(player.Object.DBRef);
		var target = await Get<IMediator>().Send(new CreateRoomCommand("snapshot-test-" + Guid.NewGuid().ToString("N"), player));
		target = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().DBRef;
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("original"), player));
		return (actor!, target, player);
	}

	[Test, NotInParallel]
	public async Task CapturePreviewRestorePreservesValuesAndClearsPendingMarker()
	{
		var (actor, target, player) = await Setup();
		var service = Get<IObjectSnapshotService>();
		var styled = MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain("original"));
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], styled, player));
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		await Get<IMediator>().Send(new SetLockCommand(node.Object(), "Basic", "#TRUE", player) { Flags = Library.Services.LockService.LockFlags.Visual });
		await Get<IManipulateSharpObjectService>().SetOrUnsetFlag(player, node, "DARK", false);
		var saved = await service.CaptureAsync(actor, target, "before edit");
		await Get<IManipulateSharpObjectService>().SetOrUnsetFlag(player, node, "!DARK", false);
		await Get<IMediator>().Send(new SetLockCommand(node.Object(), "Basic", "#FALSE", player));
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("changed"), player));
		var selection = new SnapshotSelection(["DESC"], Locks: true, Flags: true);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var result = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(result.Completed).IsTrue();
		var attribute = await Get<IAttributeStore>().GetAttributeAsync(target, ["DESC"]).LastAsync();
		await Assert.That(MarkupTextSerializer.Serialize(attribute.Value)).IsEqualTo(MarkupTextSerializer.Serialize(styled));
		var restored = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		await Assert.That(restored.Object().Locks["Basic"].LockString).IsEqualTo("#TRUE");
		await Assert.That(restored.Object().Locks["Basic"].Flags).IsEqualTo(Library.Services.LockService.LockFlags.Visual);
		await Assert.That((await restored.Object().Flags.Value.ToListAsync()).Any(f => f.Name == "DARK")).IsTrue();
		await Assert.That((await service.ListAsync(actor, target)).PendingRecoveryId).IsNull();
		await Assert.That((await service.ListAsync(actor, target)).Snapshots.Any(s => s.Id == result.RecoverySnapshotId)).IsTrue();
	}

	[Test, NotInParallel]
	public async Task StalePreviewAndRecycledIdentityAreRejected()
	{
		var (actor, target, player) = await Setup();
		var service = Get<IObjectSnapshotService>();
		var saved = await service.CaptureAsync(actor, target, "before");
		var selection = new SnapshotSelection(["DESC"]);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("later"), player));
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token));
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.ListAsync(actor, new DBRef(target.Number, target.CreationMilliseconds + 1)));
	}

	[Test, NotInParallel]
	public async Task CorruptDocumentIsRejectedAndRetentionPersists()
	{
		var (actor, target, _) = await Setup();
		var service = Get<IObjectSnapshotService>();
		for (var i = 0; i < 3; i++) await service.CaptureAsync(actor, target, "version " + i, 2);
		var history = await service.ListAsync(actor, target);
		await Assert.That(history.Snapshots.Length).IsEqualTo(2);
		var obj = (await Get<IObjectStore>().GetObjectNodeAsync(target)).AsRoom.Object;
		var persisted = await Get<IExpandedDataStore>().GetExpandedObjectData<SnapshotStorageRecord>(obj.Id!, ObjectSnapshotService.StorageKey);
		await Assert.That(persisted!.History.Snapshots.Length).IsEqualTo(2);
		var corrupt = history.Snapshots[0] with { Digest = "corrupt" };
		await Get<IExpandedDataStore>().SetExpandedObjectData(obj.Id!, ObjectSnapshotService.StorageKey, new SnapshotStorageRecord(history with { Snapshots = [corrupt] }));
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.PreviewAsync(actor, target, corrupt.Id, new(["DESC"])));
		foreach (var altered in new[] { history.Snapshots[0] with { ObjectType = "THING" }, history.Snapshots[0] with { ObjectId = "#999:1" }, history.Snapshots[0] with { SchemaVersion = 2 } })
		{
			var image = altered with { Digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(altered with { Digest = "" }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))))) };
			await Get<IExpandedDataStore>().SetExpandedObjectData(obj.Id!, ObjectSnapshotService.StorageKey, new SnapshotStorageRecord(new([image])));
			await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.PreviewAsync(actor, target, image.Id, new(["DESC"])));
		}
	}

	[Test, NotInParallel]
	public async Task MidRestoreFailureRetainsDurableBeforeImage()
	{
		var (actor, target, player) = await Setup();
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["OTHER"], MarkupText.Plain("old-other"), player));
		var saved = await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "before");
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("changed-desc"), player));
		await Get<IMediator>().Send(new ClearAttributeCommand(target, ["OTHER"]));
		var realAttributes = Get<IAttributeService>();
		var failing = Substitute.For<IAttributeService>();
		var writes = 0;
		failing.SetAttributeAsync(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<string>(), Arg.Any<MarkupText>())
			.Returns(call => ++writes == 2
				? ValueTask.FromException<OneOf<Success, Error<string>>>(new IOException("Injected failure"))
				: realAttributes.SetAttributeAsync(call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(0), call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(1), call.ArgAt<string>(2), call.ArgAt<MarkupText>(3)));
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), failing, Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection(["OTHER", "DESC"]);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var result = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(result.Completed).IsFalse();
		var history = await Get<IObjectSnapshotService>().ListAsync(actor, target);
		await Assert.That(history.PendingRecoveryId).IsEqualTo(result.RecoverySnapshotId);
		var recovery = history.Snapshots.Single(s => s.Id == result.RecoverySnapshotId);
		await Assert.That(MarkupTextSerializer.Deserialize(recovery.Attributes.Single(a => a.Name == "DESC").Markup).ToPlainText()).IsEqualTo("changed-desc");
		var recoveryPreview = await Get<IObjectSnapshotService>().PreviewAsync(actor, target, recovery.Id, selection);
		var recovered = await Get<IObjectSnapshotService>().RestoreAsync(actor, target, recovery.Id, selection, recoveryPreview.Token);
		await Assert.That(recovered.Completed).IsTrue();
		await Assert.That(await Get<IAttributeStore>().GetAttributeAsync(target, ["OTHER"]).AnyAsync()).IsFalse();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["DESC"]).LastAsync()).Value.ToPlainText()).IsEqualTo("changed-desc");
	}
	[Test, NotInParallel]
	public async Task RevocationBetweenPreviewAndRestorePreventsWrites()
	{
		var (actor, target, _) = await Setup();
		var saved = await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "before");
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			capabilities, Get<IPermissionService>(), Get<IAttributeService>(), Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection(["DESC"]);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token));
		await Assert.That((await Get<IObjectSnapshotService>().ListAsync(actor, target)).Snapshots.Length).IsEqualTo(1);
	}

	[Test, NotInParallel]
	public async Task MissingLockReferencesAreRejectedBeforeRestore()
	{
		var (actor, target, _) = await Setup();
		var obj = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object();
		await Get<IObjectStore>().SetLockAsync(obj, "Basic", new SharpLockData("=#999999:1"));
		var service = Get<IObjectSnapshotService>();
		var saved = await service.CaptureAsync(actor, target, "missing reference");
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.PreviewAsync(actor, target, saved.Id, new(["DESC"], Locks: true)));
	}


	[Test, NotInParallel]
	public async Task RecoveryRemovesLockCreatedBeforeLaterFailure()
	{
		var (actor, target, player) = await Setup();
		var obj = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object();
		await Get<IMediator>().Send(new SetLockCommand(obj, "Basic", "#TRUE", player));
		var real = Get<IObjectSnapshotService>();
		var saved = await real.CaptureAsync(actor, target, "lock before");
		await Get<IMediator>().Send(new UnsetLockCommand(obj, "Basic"));
		var failing = Substitute.For<IManipulateSharpObjectService>();
		failing.SetName(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<MarkupText>(), false)
			.Returns(_ => ValueTask.FromException<CallState>(new IOException("Injected later failure")));
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), Get<IAttributeService>(), failing, Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection([], Locks: true, Name: true);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var result = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(result.Completed).IsFalse();
		await Assert.That((await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks.ContainsKey("Basic")).IsTrue();
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await real.PreviewAsync(actor, target, result.RecoverySnapshotId, new([])));
		var recovery = await real.PreviewAsync(actor, target, result.RecoverySnapshotId, selection);
		await Assert.That((await real.RestoreAsync(actor, target, result.RecoverySnapshotId, selection, recovery.Token)).Completed).IsTrue();
		await Assert.That((await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks.ContainsKey("Basic")).IsFalse();
	}

	[Test, NotInParallel]
	public async Task HistoricalAncestorRestrictionsSurviveCurrentFlagRemoval()
	{
		var (actor, target, player) = await Setup();
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["BRANCH", "LEAF"], MarkupText.Plain("private"), player));
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		await Get<IAttributeService>().SetAttributeFlagsAsync(player, node, "BRANCH", ["MORTAL_DARK"]);
		var saved = await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "restricted branch");
		await Assert.That(saved.Attributes.Single(a => a.Name == "BRANCH`LEAF").Ancestors.Any(a => a.Flags.Contains("mortal_dark", StringComparer.OrdinalIgnoreCase))).IsTrue();
		await Get<IAttributeService>().SetAttributeFlagsAsync(player, node, "BRANCH", ["!MORTAL_DARK"]);
		var permissions = Substitute.For<IPermissionService>();
		permissions.Controls(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>()).Returns(true);
		permissions.CanViewAttribute(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<SharpAttribute[]>())
			.Returns(call => !call.ArgAt<SharpAttribute[]>(2).Any(a => a.Flags.Any(f => f.Name.Equals("mortal_dark", StringComparison.OrdinalIgnoreCase))));
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), permissions, Get<IAttributeService>(), Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		await Assert.That((await service.ListAsync(actor, target)).Snapshots.Length).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task RestoreOnlyDelegationCanListWithoutCapturing()
	{
		var (actor, target, _) = await Setup();
		await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "before");
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.SnapshotRestore, Arg.Any<CancellationToken>()).Returns(true);
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			capabilities, Get<IPermissionService>(), Get<IAttributeService>(), Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		await Assert.That((await service.ListAsync(actor, target)).Snapshots.Length).IsEqualTo(1);
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.CaptureAsync(actor, target, "denied"));
	}

	[Test, NotInParallel]
	public async Task GameCommandCapturesThroughTheSharedService()
	{
		var (actor, target, _) = await Setup();
		await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain($"@snapshot/capture {target}=game capture"));
		var history = await Get<IObjectSnapshotService>().ListAsync(actor, target);
		await Assert.That(history.Snapshots.Length).IsEqualTo(1);
		await Assert.That(history.Snapshots[0].Description).IsEqualTo("game capture");
	}
}
