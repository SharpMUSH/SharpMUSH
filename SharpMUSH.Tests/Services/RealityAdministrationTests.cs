using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Services;

public class RealityAdministrationTests
{
	[Test]
	public async Task DirectCallerCancellationBoundsControlAuthorization()
	{
		var player = new TestObjectFactory().CreatePlayer(45, "admin");
		player.Object().Id = "admin";
		var store = Substitute.For<IExpandedDataStore>();
		var objects = Substitute.For<IObjectStore>();
		objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(player));
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.RealityAdmin, Arg.Any<CancellationToken>()).Returns(true);
		var permissions = Substitute.For<IPermissionService>();
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>()).Returns(async ValueTask<bool> (_) =>
		{
			var token = ExecutionBudget.CurrentToken;
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			return true;
		});
		var service = new RealityAdministration(new(store, objects), capabilities, objects, permissions, Substitute.For<IValidateService>());
		using var cancellation = new CancellationTokenSource();
		var invocation = service.ExecuteAsync(new("admin", player.Object().DBRef, player.Object().DBRef),
			"inspect", player.Object().DBRef.ToString(), "", cancellation.Token);
		try
		{
			await Assert.That((await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).CanBeCanceled).IsTrue();
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
			await store.DidNotReceiveWithAnyArgs().SetExpandedObjectData(default!, default!, default!);
		}
		finally
		{
			release.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	public async Task RevokedAccountCannotChangeConfiguration()
	{
		var store = Substitute.For<IExpandedDataStore>();
		var objects = Substitute.For<IObjectStore>();
		var service = new RealityAdministration(new(store, objects), Substitute.For<IAdministrativeCapabilityService>(),
			objects, Substitute.For<IPermissionService>(), Substitute.For<IValidateService>());
		await Assert.That(async () => await service.ExecuteAsync(new("disabled", new DBRef(1, 1), new DBRef(1, 1)), "enable", "", ""))
			.Throws<UnauthorizedAccessException>();
		await store.DidNotReceiveWithAnyArgs().SetExpandedServerData(default!, default!);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ObjectMutationRequiresCurrentCapabilityAndControls(bool revokeAfterControl)
	{
		var store = Substitute.For<IExpandedDataStore>();
		var objects = Substitute.For<IObjectStore>();
		var actor = new TestObjectFactory().CreatePlayer(40, "admin");
		var target = new TestObjectFactory().CreatePlayer(41, "other");
		actor.Object().Id = "admin"; target.Object().Id = "other";
		objects.GetObjectNodeAsync(actor.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(actor));
		objects.GetObjectNodeAsync(target.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(target));
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		var allowed = true;
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.RealityAdmin, Arg.Any<CancellationToken>()).Returns(_ => allowed);
		var permissions = Substitute.For<IPermissionService>();
		if (revokeAfterControl) permissions.Controls(Arg.Any<AnySharpObject>(), Arg.Any<AnySharpObject>())
			.Returns(_ => { allowed = false; return true; });
		var service = new RealityAdministration(new(store, objects), capabilities, objects, permissions, Substitute.For<IValidateService>());
		await Assert.That(async () => await service.ExecuteAsync(new("admin", actor.Object().DBRef, actor.Object().DBRef),
			"rx", target.Object().DBRef.ToString(), "normal")).Throws<UnauthorizedAccessException>();
		await store.DidNotReceiveWithAnyArgs().SetExpandedObjectData(default!, default!, default!);
	}
	[Test]
	public async Task FailedConfigurationWriteDoesNotPublishEnabledPolicy()
	{
		var store = Substitute.For<IExpandedDataStore>();
		store.SetExpandedServerData(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
			.Returns(_ => ValueTask.FromException(new IOException("write failed")));
		var objects = Substitute.For<IObjectStore>();
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.RealityAdmin, Arg.Any<CancellationToken>()).Returns(true);
		var player = new TestObjectFactory().CreatePlayer(42, "admin");
		objects.GetObjectNodeAsync(player.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(player));
		var policy = new RealityPolicy(store, objects);
		await Assert.That(await policy.IsEnabledAsync()).IsFalse();
		var service = new RealityAdministration(policy, capabilities, objects, Substitute.For<IPermissionService>(), Substitute.For<IValidateService>());
		await Assert.That(async () => await service.ExecuteAsync(new("admin", player.Object().DBRef, player.Object().DBRef), "enable", "", ""))
			.Throws<IOException>();
		await Assert.That(await policy.IsEnabledAsync()).IsFalse();
	}

}
