using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class LockPermissionParityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IMediator Mediator => Factory.Services.GetRequiredService<IMediator>();
	private ISharpDatabase Database => Factory.Services.GetRequiredService<ISharpDatabase>();
	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();
	private ILockService Locks => Factory.Services.GetRequiredService<ILockService>();

	[Test]
	[Arguments("replace")]
	[Arguments("unset")]
	[Arguments("flags")]
	public async Task UnknownLockedCreatorDeniesMortalAndAllowsWizardRepair(string operation)
	{
		var player = await CreatePlayerAsync("LegacyLockOwner");
		var created = await RunAsync(player.Handle, $"@create LegacyLock{Guid.NewGuid():N}");
		var reference = DBRef.Parse(created);
		await RunAsync(player.Handle, $"@lock {created}=#TRUE");
		var staleTarget = (await Mediator.Send(new GetObjectNodeQuery(reference))).Expect<AnySharpObject>();
		var executor = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
		var legacy = new SharpLockData("#FALSE", LockService.LockFlags.Private | LockService.LockFlags.Locked, null);
		await Database.SetLockAsync(staleTarget.Object(), "Basic", legacy);

		// Supply the older object snapshot to prove mutation checks reload current protection.
		var result = operation switch
		{
			"replace" => await Locks.SetAsync(executor, staleTarget, "Basic", "#TRUE"),
			"unset" => await Locks.UnsetAsync(executor, staleTarget, "Basic"),
			_ => await Locks.SetFlagsAsync(executor, staleTarget, "Basic", "!locked")
		};
		await Assert.That(result is Error<string>).IsTrue();
		var persisted = (await Database.GetObjectNodeAsync(reference)).Expect<AnySharpObject>().Object().Locks["Basic"];
		await Assert.That(persisted.LockString).IsEqualTo("#FALSE");
		await Assert.That(persisted.Flags).IsEqualTo(legacy.Flags);
		await Assert.That(persisted.Creator is null).IsTrue();

		var wizard = await CreatePlayerAsync("LegacyLockWizard");
		await RunAsync(1, $"@set #{wizard.DbRef.Number}=WIZARD");
		var wizardObject = (await Mediator.Send(new GetObjectNodeQuery(wizard.DbRef))).Expect<AnySharpObject>();
		await Assert.That(await wizardObject.IsWizard()).IsTrue();
		await Assert.That(wizardObject.IsGod()).IsFalse();
		var repaired = await Locks.SetAsync(wizardObject, staleTarget, "Basic", "#TRUE");
		await Assert.That(repaired is Success).IsTrue();
		persisted = (await Database.GetObjectNodeAsync(reference)).Expect<AnySharpObject>().Object().Locks["Basic"];
		await Assert.That(persisted.LockString).IsEqualTo("#TRUE");
		await Assert.That(persisted.Flags).IsEqualTo(legacy.Flags);
		await Assert.That(persisted.Creator?.Number).IsEqualTo(wizard.DbRef.Number);
	}

	[Test]
	[Arguments("Examine")]
	[Arguments("Forward")]
	[Arguments("Control")]
	[Arguments("Destroy")]
	[Arguments("Chown")]
	public async Task OwnerOnlyDefaultsRejectOwnedObjectDespiteControl(string type)
	{
		var player = await CreatePlayerAsync("OwnerLockGate");
		var targetReference = await RunAsync(player.Handle, $"@create OwnerOnlyTarget{Guid.NewGuid():N}");
		var puppetReference = await RunAsync(player.Handle, $"@create OwnerOnlyPuppet{Guid.NewGuid():N}");
		var target = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(targetReference)))).Expect<AnySharpObject>();
		var puppet = (await Mediator.Send(new GetObjectNodeQuery(DBRef.Parse(puppetReference)))).Expect<AnySharpObject>();
		await Assert.That(await Factory.Services.GetRequiredService<IPermissionService>().Controls(puppet, target)).IsTrue();
		await Assert.That(await Locks.SetAsync(puppet, target, type, "#TRUE") is Error<string>).IsTrue();
		var owner = (await Mediator.Send(new GetObjectNodeQuery(player.DbRef))).Expect<AnySharpObject>();
		await Assert.That(await Locks.SetAsync(owner, target, type, "#TRUE") is Success).IsTrue();
		var saved = (await Database.GetObjectNodeAsync(DBRef.Parse(targetReference))).Expect<AnySharpObject>();
		await Assert.That(saved.Object().Locks[type].Flags.HasFlag(LockService.LockFlags.Owner)).IsTrue();
	}

	[Test]
	public async Task CloneUsesClonerAsCreatorAndOmitsNoCloneLocks()
	{
		var player = await CreatePlayerAsync("CloneLockOwner");
		var source = await RunAsync(player.Handle, $"@create CloneLockSource{Guid.NewGuid():N}");
		await RunAsync(1, $"@lock {source}=#FALSE");
		await RunAsync(1, $"@lset {source}/Basic=visual");
		await RunAsync(1, $"@lock/user:Skipped {source}=#TRUE");
		await RunAsync(1, $"@lset {source}/Skipped=no_clone");
		var clonedReference = await RunAsync(player.Handle, $"@clone {source}=CloneLockCopy{Guid.NewGuid():N}");
		var cloned = (await Database.GetObjectNodeAsync(DBRef.Parse(clonedReference))).Expect<AnySharpObject>();
		var copied = cloned.Object().Locks["Basic"];
		await Assert.That(copied.LockString).IsEqualTo("#FALSE");
		await Assert.That(copied.Flags).IsEqualTo(LockService.LockFlags.Private | LockService.LockFlags.Visual);
		await Assert.That(copied.Creator?.Number).IsEqualTo(player.DbRef.Number);
		await Assert.That(cloned.Object().Locks.ContainsKey("Skipped")).IsFalse();
		var original = (await Database.GetObjectNodeAsync(DBRef.Parse(source))).Expect<AnySharpObject>();
		await Assert.That(original.Object().Locks["Basic"].Creator?.Number).IsEqualTo(1);
		await Assert.That(original.Object().Locks["Skipped"].Flags.HasFlag(LockService.LockFlags.NoClone)).IsTrue();
	}

	private Task<TestIsolationHelpers.TestPlayer> CreatePlayerAsync(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, Mediator, Connections, prefix);

	private async Task<string> RunAsync(long handle, string command)
		=> (await Factory.CommandParser.CommandParse(handle, Connections, MarkupText.Plain(command))).Message!.ToPlainText();
}
