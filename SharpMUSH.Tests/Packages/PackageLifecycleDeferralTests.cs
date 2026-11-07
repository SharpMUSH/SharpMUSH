using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

public class PackageLifecycleDeferralTests
{
	[Test]
	public async Task CapturedChildCannotAppendToAnAlreadyReturnedHookList()
	{
		var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Task<bool>? late = null;
		var collected = await PackageLifecycleDeferral.CollectAsync(() =>
		{
			PackageLifecycleDeferral.TryDefer("#1", "AINSTALL");
			late = Task.Run(async () => { await release.Task; return PackageLifecycleDeferral.TryDefer("#2", "AUPDATE"); });
			return Task.FromResult(42);
		});
		release.TrySetResult();
		var acceptedLate = await late!.WaitAsync(TimeSpan.FromSeconds(10));
		await Assert.That(acceptedLate).IsFalse();
		await Assert.That(collected.Result == 42 && collected.Hooks.Count == 1
			&& collected.Hooks[0] == new PackageLifecycleHook("#1", "AINSTALL")).IsTrue();
		await Assert.That(PackageLifecycleDeferral.TryDefer("#3", "AINSTALL")).IsFalse();
	}

	[Test]
	public async Task RetainedHooksKeepEachCommittedVersionPair()
	{
		var first = new PackageLifecycleHook("#123", "AUPDATE")
		{
			FromVersion = "2.2.0",
			ToVersion = "2.8.0",
			CreatedAttributes = "store/FIELD`BACKGROUND`ATTRIBUTE"
		};
		var second = new PackageLifecycleHook("#123", "AUPDATE")
		{
			FromVersion = "2.8.0",
			ToVersion = "2.9.0"
		};
		var collected = await PackageLifecycleDeferral.CollectAsync(() =>
		{
			PackageLifecycleDeferral.TryDefer(first);
			PackageLifecycleDeferral.TryDefer(second);
			return Task.FromResult(true);
		});
		await Assert.That(collected.Hooks).IsEquivalentTo(new[] { first, second });
		await Assert.That(collected.Hooks[0].FromVersion).IsEqualTo("2.2.0");
		await Assert.That(collected.Hooks[0].CreatedAttributes).IsEqualTo("store/FIELD`BACKGROUND`ATTRIBUTE");
		await Assert.That(collected.Hooks[1].ToVersion).IsEqualTo("2.9.0");
	}

}
