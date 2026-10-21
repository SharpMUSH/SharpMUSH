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
}
