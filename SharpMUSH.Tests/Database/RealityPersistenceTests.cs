using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database;

public class RealityPersistenceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();

	[Test, NotInParallel]
	public async Task LayerChangesSurviveReloadWithoutWideningRemovedSets()
	{
		var store = Get<IExpandedDataStore>();
		await store.SetExpandedServerData(RealityPolicy.ConfigurationKey, RealityConfiguration.Default);
		var objects = Get<IObjectStore>();
		var player = (await objects.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var target = await Get<IMediator>().Send(new CreateRoomCommand("reality-" + Guid.NewGuid().ToString("N"), player));
		target = (await objects.GetObjectNodeAsync(target)).Known.Object().DBRef;
		var capabilities = Substitute.For<IAdministrativeCapabilityService>();
		capabilities.AuthorizeAsync(Arg.Any<CapabilityActor>(), PortalPermission.RealityAdmin, Arg.Any<CancellationToken>()).Returns(true);
		var actor = new CapabilityActor("admin", player.Object.DBRef, player.Object.DBRef);
		var policy = new RealityPolicy(store, objects);
		var admin = new RealityAdministration(policy, capabilities, objects, Get<IPermissionService>(), Get<IValidateService>());
		try
		{
			await admin.ExecuteAsync(actor, "add", "ghost", "");
			await admin.ExecuteAsync(actor, "rx", player.Object.DBRef.ToString(), "ghost");
			await admin.ExecuteAsync(actor, "tx", target.ToString(), "ghost");
			await admin.ExecuteAsync(actor, "describe", target.ToString(), "ghost/GHOSTDESC");
			await admin.ExecuteAsync(actor, "enable", "", "");
			await Assert.That(await policy.IsEnabledAsync()).IsTrue();
			var reloaded = new RealityPolicy(store, objects);
			await Assert.That(await reloaded.CanPerceiveAsync(player.Object.DBRef, target)).IsTrue();
			await Assert.That(await reloaded.DescriptionAttributeAsync(player.Object.DBRef, target)).IsEqualTo("GHOSTDESC");
			await admin.ExecuteAsync(actor, "describe", target.ToString(), "ghost");
			await Assert.That(await policy.DescriptionAttributeAsync(player.Object.DBRef, target)).IsNull();
			await admin.ExecuteAsync(actor, "remove", "ghost", "");
			await Assert.That(await policy.CanPerceiveAsync(player.Object.DBRef, target)).IsFalse();
			await Assert.That(await new RealityPolicy(store, objects).CanPerceiveAsync(player.Object.DBRef, target)).IsFalse();
		}
		finally
		{
			await store.SetExpandedServerData(RealityPolicy.ConfigurationKey, RealityConfiguration.Default);
			await store.SetExpandedObjectData(player.Object.Id!, RealityPolicy.ObjectKey, ObjectReality.Default(player.Object.DBRef));
		}
	}
}
