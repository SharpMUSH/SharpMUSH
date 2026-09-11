using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
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
	[Arguments("DESC")]
	[Arguments("desc")]
	public async Task CapturePreviewRestorePreservesValuesAndClearsPendingMarker(string selectedName)
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
		var selection = new SnapshotSelection([selectedName], Locks: true, Flags: true);
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
			var failure = await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.PreviewAsync(actor, target, image.Id, new(["DESC"])));
			await Assert.That(failure!.Code).IsEqualTo("corrupt");
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
				? ValueTask.FromException<Result<Success>>(new IOException("Injected failure"))
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
		foreach (var extra in new[] { selection with { Flags = true }, selection with { Name = true }, selection with { Locks = true } })
		{
			SnapshotOperationException? rejected = null;
			try { await Get<IObjectSnapshotService>().PreviewAsync(actor, target, recovery.Id, extra); }
			catch (SnapshotOperationException exception) { rejected = exception; }
			await Assert.That(rejected?.Code).IsEqualTo("invalid");
		}
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
		await Get<IMediator>().Send(new SetLockCommand(obj, "Unrelated", "#TRUE", player));
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
		await Get<IMediator>().Send(new SetLockCommand(obj, "Unrelated", "#FALSE", player));
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await real.PreviewAsync(actor, target, result.RecoverySnapshotId, new([])));
		var recovery = await real.PreviewAsync(actor, target, result.RecoverySnapshotId, selection);
		await Assert.That((await real.RestoreAsync(actor, target, result.RecoverySnapshotId, selection, recovery.Token)).Completed).IsTrue();
		await Assert.That((await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks.ContainsKey("Basic")).IsFalse();
		await Assert.That((await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks["Unrelated"].LockString).IsEqualTo("#FALSE");
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
	[Arguments("CAPTURE")]
	[Arguments("LIST")]
	[Arguments("PREVIEW")]
	[Arguments("RESTORE")]
	[Arguments("RESOLVE")]
	public async Task GameSnapshotOperationsReceiveTheExecutionToken(string operation)
	{
		var (actor, target, player) = await Setup();
		var saved = await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "before");
		var snapshots = Substitute.For<IObjectSnapshotService>();
		snapshots.CaptureAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(saved);
		snapshots.ListAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(new SnapshotHistory([saved]));
		snapshots.PreviewAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<SnapshotSelection>(), Arg.Any<CancellationToken>())
			.Returns(new SnapshotPreview(saved.Id, target.ToString(), "token", new(["DESC"]), []));
		snapshots.RestoreAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<SnapshotSelection>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(new SnapshotRestoreResult(true, "recovery", null));
		snapshots.ResolveRecoveryAsync(Arg.Any<CapabilityActor>(), Arg.Any<DBRef>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
		var services = Substitute.For<IServiceProvider>();
		services.GetService(Arg.Any<Type>()).Returns(call => call.Arg<Type>() == typeof(IObjectSnapshotService)
			? snapshots : Factory.Services.GetService(call.Arg<Type>()));
		var parser = Substitute.For<IMUSHCodeParser>();
		parser.ServiceProvider.Returns(services);
		parser.CurrentState.Returns(ParserState.RootFor(player.Object.DBRef) with
		{
			Switches = [operation],
			Arguments = new() { ["0"] = new(target.ToString()), ["1"] = new(saved.Id + ",token") }
		});
		var commands = (SharpMUSH.Implementation.Commands.Commands)Get<ILibraryProvider<CommandDefinition>>();
		var metadata = typeof(SharpMUSH.Implementation.Commands.Commands).GetMethod("Snapshot")!.GetCustomAttribute<SharpCommandAttribute>()!;
		snapshots.ClearReceivedCalls();
		using var budget = ExecutionBudget.FromMilliseconds(30000);
		using var scope = budget.Enter();
		await commands.Snapshot(parser, metadata);
		var tokens = snapshots.ReceivedCalls().SelectMany(call => call.GetArguments().OfType<CancellationToken>()).ToArray();
		await Assert.That(tokens.Length).IsGreaterThan(0);
		await Assert.That(tokens.All(token => token == budget.Token)).IsTrue();
	}

	[Test, NotInParallel]
	public async Task CancelledRestoreStopsMutationsAndRetainsUsableRecovery()
	{
		var (actor, target, player) = await Setup();
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["OTHER"], MarkupText.Plain("original-other"), player));
		var real = Get<IObjectSnapshotService>();
		var saved = await real.CaptureAsync(actor, target, "before");
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("changed-desc"), player));
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["OTHER"], MarkupText.Plain("changed-other"), player));
		using var cancellation = new CancellationTokenSource();
		var writes = 0;
		var diagnosticWritesAfterCancellation = 0;
		var attributes = Substitute.For<IAttributeService>();
		async ValueTask<Result<Success>> WriteThenCancel(NSubstitute.Core.CallInfo call)
		{
			var result = await Get<IAttributeService>().SetAttributeAsync(call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(0),
				call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(1), call.ArgAt<string>(2), call.ArgAt<MarkupText>(3));
			writes++;
			cancellation.Cancel();
			return result;
		}
		attributes.SetAttributeAsync(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<string>(), Arg.Any<MarkupText>())
			.Returns(WriteThenCancel);
		var expanded = Substitute.For<IExpandedDataStore>();
		expanded.GetExpandedObjectData<SnapshotStorageRecord>(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => Get<IExpandedDataStore>().GetExpandedObjectData<SnapshotStorageRecord>(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<CancellationToken>(2)));
		expanded.SetExpandedObjectData(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(call =>
			{
				if (cancellation.IsCancellationRequested) diagnosticWritesAfterCancellation++;
				return Get<IExpandedDataStore>().SetExpandedObjectData(call.ArgAt<string>(0), call.ArgAt<string>(1), call.ArgAt<object>(2), call.ArgAt<CancellationToken>(3));
			});
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), expanded,
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), attributes, Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection(["DESC", "OTHER"]);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var result = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token, cancellation.Token);
		await Assert.That(result.Completed).IsFalse();
		await Assert.That(writes).IsEqualTo(1);
		await Assert.That(diagnosticWritesAfterCancellation).IsEqualTo(0);
		var history = await real.ListAsync(actor, target);
		await Assert.That(history.PendingRecoveryId).IsEqualTo(result.RecoverySnapshotId);
		var recovery = await real.PreviewAsync(actor, target, result.RecoverySnapshotId, selection);
		await Assert.That((await real.RestoreAsync(actor, target, result.RecoverySnapshotId, selection, recovery.Token)).Completed).IsTrue();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["DESC"]).LastAsync()).Value.ToPlainText()).IsEqualTo("changed-desc");
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["OTHER"]).LastAsync()).Value.ToPlainText()).IsEqualTo("changed-other");
	}

	[Test, NotInParallel]
	public async Task GameCommandCapturesAndResolvesThroughTheSharedService()
	{
		var (actor, target, _) = await Setup();
		await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain($"@snapshot/capture {target}=game capture"));
		var history = await Get<IObjectSnapshotService>().ListAsync(actor, target);
		await Assert.That(history.Snapshots.Length).IsEqualTo(1);
		await Assert.That(history.Snapshots[0].Description).IsEqualTo("game capture");
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object();
		await Get<IExpandedDataStore>().SetExpandedObjectData(node.Id!, ObjectSnapshotService.StorageKey, new SnapshotStorageRecord(history with { PendingRecoveryId = history.Snapshots[0].Id }));
		await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain($"@snapshot/resolve {target}={history.Snapshots[0].Id}"));
		var resolved = await Get<IObjectSnapshotService>().ListAsync(actor, target);
		await Assert.That(resolved.PendingRecoveryId).IsNull();
		await Assert.That(resolved.LastResolution!.AccountId).IsEqualTo(actor.AccountId);
	}

	[Test, NotInParallel]
	public async Task RevocationAfterValueWriteStopsAttributeFlagMutation()
	{
		var (actor, target, player) = await Setup();
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["GUARDED"], MarkupText.Plain("saved"), player));
		var realAttributes = Get<IAttributeService>();
		await realAttributes.SetAttributeFlagsAsync(player, node, "GUARDED", ["VISUAL"]);
		var saved = await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "before");
		await realAttributes.SetAttributeFlagsAsync(player, node, "GUARDED", ["!VISUAL"]);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["GUARDED"], MarkupText.Plain("current"), player));
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
		var attributes = Substitute.For<IAttributeService>();
		async ValueTask<Result<Success>> RevokeAfterWrite(NSubstitute.Core.CallInfo call)
		{
			var result = await realAttributes.SetAttributeAsync(call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(0), call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(1), call.ArgAt<string>(2), call.ArgAt<MarkupText>(3));
			capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
			return result;
		}
		attributes.SetAttributeAsync(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<string>(), Arg.Any<MarkupText>()).Returns(RevokeAfterWrite);
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			capabilities, Get<IPermissionService>(), attributes, Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection(["GUARDED"]);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var result = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(result.Completed).IsFalse();
		var changed = await Get<IAttributeStore>().GetAttributeAsync(target, ["GUARDED"]).LastAsync();
		await Assert.That(changed.Value.ToPlainText()).IsEqualTo("saved");
		await Assert.That(changed.Flags.Any(f => f.Name.Equals("visual", StringComparison.OrdinalIgnoreCase))).IsFalse();
		await Assert.That((await Get<IObjectSnapshotService>().ListAsync(actor, target)).PendingRecoveryId).IsEqualTo(result.RecoverySnapshotId);
		var recoveryImage = (await Get<IObjectSnapshotService>().ListAsync(actor, target)).Snapshots.Single(s => s.Id == result.RecoverySnapshotId);
		await Assert.That(recoveryImage.DefaultAttributes()).IsEquivalentTo(new[] { "GUARDED" });
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("unrelated later edit"), player));
		var recoverySelection = new SnapshotSelection(recoveryImage.DefaultAttributes());
		var recoveryPreview = await Get<IObjectSnapshotService>().PreviewAsync(actor, target, recoveryImage.Id, recoverySelection);
		await Assert.That((await Get<IObjectSnapshotService>().RestoreAsync(actor, target, recoveryImage.Id, recoverySelection, recoveryPreview.Token)).Completed).IsTrue();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["DESC"]).LastAsync()).Value.ToPlainText()).IsEqualTo("unrelated later edit");
	}

	[Test, NotInParallel]
	public async Task LockProtectionAddedDuringRestoreStopsTheLockMutation()
	{
		var (actor, target, player) = await Setup();
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		await Get<IMediator>().Send(new SetLockCommand(node.Object(), "Basic", "#TRUE", player));
		var saved = await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "before");
		await Get<IMediator>().Send(new SetLockCommand(node.Object(), "Basic", "#FALSE", player));
		var realAttributes = Get<IAttributeService>();
		var attributes = Substitute.For<IAttributeService>();
		async ValueTask<Result<Success>> ProtectAfterWrite(NSubstitute.Core.CallInfo call)
		{
			var result = await realAttributes.SetAttributeAsync(call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(0), call.ArgAt<Library.DiscriminatedUnions.AnySharpObject>(1), call.ArgAt<string>(2), call.ArgAt<MarkupText>(3));
			await Get<IMediator>().Send(new SetLockCommand(node.Object(), "Basic", "#FALSE", player) { Flags = Library.Services.LockService.LockFlags.Locked });
			return result;
		}
		attributes.SetAttributeAsync(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<string>(), Arg.Any<MarkupText>()).Returns(ProtectAfterWrite);
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), attributes, Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection(["DESC"], Locks: true);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		await Assert.That((await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token)).Completed).IsFalse();
		var current = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks["Basic"];
		await Assert.That(current.LockString).IsEqualTo("#FALSE");
		await Assert.That(current.Flags).IsEqualTo(Library.Services.LockService.LockFlags.Locked);
	}

	[Test, NotInParallel]
	public async Task AuthorizedControllerCanResolveAnOrphanedRecoveryMarker()
	{
		var (originalActor, target, _) = await Setup();
		var original = await Get<IObjectSnapshotService>().CaptureAsync(originalActor, target, "old account recovery");
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object();
		await Get<IExpandedDataStore>().SetExpandedObjectData(node.Id!, ObjectSnapshotService.StorageKey, new SnapshotStorageRecord(new([original], original.Id)));
		var actor = originalActor with { AccountId = "new-controller" };
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			capabilities, Get<IPermissionService>(), Get<IAttributeService>(), Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		await Assert.That((await service.ListAsync(actor, target)).Snapshots.Length).IsEqualTo(0);
		var own = await service.CaptureAsync(actor, target, "new account");
		var selection = new SnapshotSelection(["DESC"]);
		var preview = await service.PreviewAsync(actor, target, own.Id, selection);
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.RestoreAsync(actor, target, own.Id, selection, preview.Token));
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.ResolveRecoveryAsync(actor, target, "stale-marker"));
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
		await Assert.ThrowsAsync<SnapshotOperationException>(async () => await service.ResolveRecoveryAsync(actor, target, original.Id));
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
		await service.ResolveRecoveryAsync(actor, target, original.Id);
		var stored = (await Get<IExpandedDataStore>().GetExpandedObjectData<SnapshotStorageRecord>(node.Id!, ObjectSnapshotService.StorageKey))!.History;
		await Assert.That(stored.PendingRecoveryId).IsNull();
		await Assert.That(stored.Snapshots.Any(s => s.Id == original.Id)).IsTrue();
		await Assert.That(stored.LastResolution!.AccountId).IsEqualTo(actor.AccountId);
		await Assert.That((await service.RestoreAsync(actor, target, own.Id, selection, preview.Token)).Completed).IsTrue();
	}
	[Test, NotInParallel]
	public async Task RecoveryMetadataCannotExceedTheFinalImageSizeLimit()
	{
		var (actor, target, player) = await Setup();
		var service = Get<IObjectSnapshotService>();
		var names = Enumerable.Range(0, 30).Select(i => "ATTR" + i.ToString("D3") + new string('X', 50)).ToArray();
		foreach (var name in names) await Get<IMediator>().Send(new SetAttributeCommand(target, [name], MarkupText.Plain("saved"), player));
		var saved = await service.CaptureAsync(actor, target, "saved");
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		foreach (var name in names) await Get<IAttributeService>().ClearAttributeAsync(player, node, name, IAttributeService.AttributePatternMode.Exact);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("x"), player));
		var probe = await service.CaptureAsync(actor, target, "probe");
		var bytes = System.Text.Encoding.UTF8.GetByteCount(System.Text.Json.JsonSerializer.Serialize(probe, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)));
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain(new string('x', 2 * 1024 * 1024 - bytes - 500 + 1)), player));
		var selection = new SnapshotSelection(names.Append("DESC").ToArray());
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		SnapshotOperationException? failure = null;
		try { await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token); }
		catch (SnapshotOperationException ex) { failure = ex; }
		await Assert.That(failure?.Code).IsEqualTo("limit");
		var history = await service.ListAsync(actor, target);
		await Assert.That(history.PendingRecoveryId).IsNull();
		await Assert.That(history.Snapshots.Length).IsEqualTo(2);
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, [names[0]]).ToArrayAsync()).Length).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task SelectiveRestoreIgnoresUnrelatedOversizedAttributes()
	{
		var (actor, target, player) = await Setup();
		var service = Get<IObjectSnapshotService>();
		var saved = await service.CaptureAsync(actor, target, "small original");
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["DESC"], MarkupText.Plain("changed"), player));
		var unrelated = new string('x', 2 * 1024 * 1024 + 1);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["UNRELATED"], MarkupText.Plain(unrelated), player));
		var selection = new SnapshotSelection(["DESC"]);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var result = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(result.Completed).IsTrue();
		var history = await service.ListAsync(actor, target);
		var recovery = history.Snapshots.Single(image => image.Id == result.RecoverySnapshotId);
		await Assert.That(recovery.Attributes.Select(attribute => attribute.Name).SequenceEqual(["DESC"])).IsTrue();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["UNRELATED"]).LastAsync()).Value.ToPlainText()).IsEqualTo(unrelated);
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["DESC"]).LastAsync()).Value.ToPlainText()).IsEqualTo("original");
	}

	[Test, NotInParallel]
	public async Task NestedRecoveryPreservesInheritedAbsentLockMarkers()
	{
		var (actor, target, player) = await Setup();
		var obj = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object();
		await Get<IMediator>().Send(new SetLockCommand(obj, "Basic", "#TRUE", player));
		var real = Get<IObjectSnapshotService>();
		var saved = await real.CaptureAsync(actor, target, "before");
		await Get<IMediator>().Send(new UnsetLockCommand(obj, "Basic"));
		var failing = Substitute.For<IManipulateSharpObjectService>();
		failing.SetName(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<MarkupText>(), false)
			.Returns(_ => ValueTask.FromException<CallState>(new IOException("Injected later failure")));
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), Get<IAttributeService>(), failing, Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection([], Locks: true, Name: true);
		var firstPreview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var first = await service.RestoreAsync(actor, target, saved.Id, selection, firstPreview.Token);
		await Assert.That(first.Completed).IsFalse();
		await Get<IMediator>().Send(new UnsetLockCommand(obj, "Basic"));
		var secondPreview = await service.PreviewAsync(actor, target, first.RecoverySnapshotId, selection);
		var second = await service.RestoreAsync(actor, target, first.RecoverySnapshotId, selection, secondPreview.Token);
		await Assert.That(second.Completed).IsFalse();
		await Assert.That((await real.ListAsync(actor, target)).Snapshots.Single(s => s.Id == second.RecoverySnapshotId).AbsentLocks).Contains("Basic");
		await Get<IMediator>().Send(new SetLockCommand(obj, "Basic", "#TRUE", player));
		var finalPreview = await real.PreviewAsync(actor, target, second.RecoverySnapshotId, selection);
		await Assert.That((await real.RestoreAsync(actor, target, second.RecoverySnapshotId, selection, finalPreview.Token)).Completed).IsTrue();
		await Assert.That((await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks.ContainsKey("Basic")).IsFalse();
	}

	/// <summary>
	/// An image written before lock names were canonical names <see cref="LockType.Teleport"/> the way
	/// that world spelled it — <c>tport</c>, which is why <see cref="LockNames"/> still carries it as
	/// an alias. The before-image a restore retains has to carry the live lock under the name the
	/// object is keyed by: recorded as absent instead, recovery removes the lock rather than restoring
	/// its value — and an absent lock reads as "no lock", which passes everybody.
	/// </summary>
	[Test, NotInParallel]
	public async Task RecoveryKeepsALockAPreUpgradeSnapshotSpelledTheOldWay()
	{
		var (actor, target, player) = await Setup();
		var obj = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object();
		await Get<IMediator>().Send(new SetLockCommand(obj, nameof(LockType.Teleport), "#TRUE", player));
		var real = Get<IObjectSnapshotService>();
		var saved = await real.CaptureAsync(actor, target, "pre-upgrade spelling");
		// Rewrite the stored image to the spelling a world older than the fix would have written,
		// re-digesting it so it is exactly what that world would hold rather than a corrupt row.
		await RespellStoredLockAsync(target, saved.Id, nameof(LockType.Teleport), "tport");
		await Get<IMediator>().Send(new SetLockCommand(obj, nameof(LockType.Teleport), "#FALSE", player));

		var failing = Substitute.For<IManipulateSharpObjectService>();
		failing.SetName(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<MarkupText>(), false)
			.Returns(_ => ValueTask.FromException<CallState>(new IOException("Injected later failure")));
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), Get<IAttributeService>(), failing, Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection([], Locks: true, Name: true);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var failed = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(failed.Completed).IsFalse();

		var recovery = (await real.ListAsync(actor, target)).Snapshots.Single(s => s.Id == failed.RecoverySnapshotId);
		await Assert.That(recovery.Locks.ContainsKey(nameof(LockType.Teleport))).IsTrue()
			.Because("the durable before-image must carry the live lock it is the only record of");
		await Assert.That(recovery.AbsentLocks.Select(LockNames.Canonical)).DoesNotContain(nameof(LockType.Teleport));

		var recoverySelection = new SnapshotSelection(recovery.DefaultAttributes(), Locks: true, Name: true);
		var recoveryPreview = await real.PreviewAsync(actor, target, recovery.Id, recoverySelection);
		await Assert.That((await real.RestoreAsync(actor, target, recovery.Id, recoverySelection, recoveryPreview.Token)).Completed).IsTrue();
		var locks = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Locks;
		await Assert.That(locks.ContainsKey(nameof(LockType.Teleport))).IsTrue()
			.Because("recovery must put the lock back, not delete it");
		await Assert.That(locks[nameof(LockType.Teleport)].LockString).IsEqualTo("#FALSE");
	}

	/// <summary>Rewrites one stored snapshot's lock key and re-digests it, producing the image a world older than lock-name canonicalisation would hold.</summary>
	private async Task RespellStoredLockAsync(DBRef target, string snapshotId, string from, string to)
	{
		var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var id = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known.Object().Id!;
		var stored = await Get<IExpandedDataStore>().GetExpandedObjectData<SnapshotStorageRecord>(id, ObjectSnapshotService.StorageKey)
			?? throw new InvalidOperationException("The snapshot history was not stored.");
		var rewritten = stored.History.Snapshots.Select(snapshot =>
		{
			if (snapshot.Id != snapshotId) return snapshot;
			var legacy = snapshot with
			{
				Locks = snapshot.Locks.ToDictionary(
					pair => string.Equals(pair.Key, from, StringComparison.Ordinal) ? to : pair.Key,
					pair => pair.Value, StringComparer.Ordinal)
			};
			return legacy with
			{
				Digest = Convert.ToHexString(SHA256.HashData(
					Encoding.UTF8.GetBytes(JsonSerializer.Serialize(legacy with { Digest = "" }, json))))
			};
		}).ToArray();
		await Get<IExpandedDataStore>().SetExpandedObjectData(id, ObjectSnapshotService.StorageKey,
			new SnapshotStorageRecord(stored.History with { Snapshots = rewritten }));
	}

	[Test, NotInParallel]
	public async Task NestedAttributeRecoveryRemovesOnlyPreviouslyAbsentParents()
	{
		var (actor, target, player) = await Setup();
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["A", "B"], MarkupText.Plain("nested"), player));
		var real = Get<IObjectSnapshotService>();
		var saved = await real.CaptureAsync(actor, target, "nested before");
		await Get<IMediator>().Send(new ClearAttributeCommand(target, ["A", "B"]));
		await Get<IMediator>().Send(new ClearAttributeCommand(target, ["A"]));
		var failing = Substitute.For<IManipulateSharpObjectService>();
		failing.SetName(Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<Library.DiscriminatedUnions.AnySharpObject>(), Arg.Any<MarkupText>(), false)
			.Returns(_ => ValueTask.FromException<CallState>(new IOException("Injected after nested write")));
		var service = new ObjectSnapshotService(Get<IObjectStore>(), Get<IAttributeStore>(), Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), Get<IAttributeService>(), failing, Get<ILockService>(), Get<IMediator>());
		var selection = new SnapshotSelection(["A`B"], Name: true);
		var preview = await service.PreviewAsync(actor, target, saved.Id, selection);
		var failed = await service.RestoreAsync(actor, target, saved.Id, selection, preview.Token);
		await Assert.That(failed.Completed).IsFalse();
		var recovery = (await real.ListAsync(actor, target)).Snapshots.Single(s => s.Id == failed.RecoverySnapshotId);
		await Assert.That(recovery.AbsentAttributes).Contains("A");
		var recoverySelection = new SnapshotSelection(recovery.DefaultAttributes(), Name: true);
		var recoveryPreview = await real.PreviewAsync(actor, target, recovery.Id, recoverySelection);
		await Assert.That((await real.RestoreAsync(actor, target, recovery.Id, recoverySelection, recoveryPreview.Token)).Completed).IsTrue();
		await Assert.That((await Get<IAttributeStore>().GetAttributeAsync(target, ["A"]).ToArrayAsync()).Length).IsEqualTo(0);
	}

	[Test, NotInParallel]
	public async Task HistoryReusesCurrentAttributeReadsWithinOneRequest()
	{
		var (actor, target, player) = await Setup();
		var node = (await Get<IObjectStore>().GetObjectNodeAsync(target)).Known;
		await Get<IAttributeService>().SetAttributeFlagAsync(player, node, "DESC", "VISUAL");
		for (var i = 0; i < 3; i++) await Get<IObjectSnapshotService>().CaptureAsync(actor, target, "version " + i);
		var reads = 0;
		var ownerReads = 0;
		var flagReads = 0;
		var objects = Substitute.For<IObjectStore>();
		objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(call => { if (call.ArgAt<DBRef>(0).Equals(player.Object.DBRef)) ownerReads++; return Get<IObjectStore>().GetObjectNodeAsync(call.ArgAt<DBRef>(0), call.ArgAt<CancellationToken>(1)); });
		var attributes = Substitute.For<IAttributeStore>();
		attributes.GetAttributeAsync(Arg.Any<DBRef>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
			.Returns(call => { reads++; return Get<IAttributeStore>().GetAttributeAsync(call.ArgAt<DBRef>(0), call.ArgAt<string[]>(1), call.ArgAt<CancellationToken>(2)); });
		attributes.GetAttributeFlagAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
			.Returns(call => { flagReads++; return Get<IAttributeStore>().GetAttributeFlagAsync(call.ArgAt<string>(0), call.ArgAt<CancellationToken>(1)); });
		var service = new ObjectSnapshotService(objects, attributes, Get<IExpandedDataStore>(),
			Get<IAdministrativeCapabilityService>(), Get<IPermissionService>(), Get<IAttributeService>(), Get<IManipulateSharpObjectService>(), Get<ILockService>(), Get<IMediator>());
		await Assert.That((await service.ListAsync(actor, target)).Snapshots.Length).IsEqualTo(3);
		await Assert.That(reads).IsEqualTo(1);
		await Assert.That(ownerReads).IsEqualTo(2); // Fresh actor authorization plus one historical owner lookup.
		await Assert.That(flagReads).IsEqualTo(1);
	}

}
