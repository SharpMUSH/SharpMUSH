using System.Runtime.CompilerServices;
using SharpMUSH.Library;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

public class AmbientIdentityHelperTests
{
	[Test]
	[Arguments("flag")]
	[Arguments("power")]
	[Arguments("owner")]
	[Arguments("composite")]
	[Arguments("inherit")]
	public async Task LegacyHelpersPropagateTheActiveBudgetToLazyReads(string kind)
	{
		var factory = new TestObjectFactory();
		var actor = factory.CreatePlayer(40, "actor");
		var target = factory.CreateThing(41, "target", owner: actor.Expect<SharpPlayer>());
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		async Task<T> Block<T>(CancellationToken token)
		{
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			throw new InvalidOperationException();
		}
		async IAsyncEnumerable<SharpObjectFlag> Flags([EnumeratorCancellation] CancellationToken token = default)
		{
			await Block<bool>(token);
			yield break;
		}
		async IAsyncEnumerable<SharpPower> Powers([EnumeratorCancellation] CancellationToken token = default)
		{
			await Block<bool>(token);
			yield break;
		}
		actor.Object().Flags = new(() => Flags());
		actor.Object().Powers = new(() => Powers());
		if (kind == "owner") actor.Object().Owner = new(token => Block<SharpPlayer>(token));
		if (kind == "inherit") target.Object().Owner = new(token => Block<SharpPlayer>(token));
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		var invocation = (kind switch
		{
			"flag" => actor.HasFlag("DARK"),
			"power" => actor.HasPower("Guest"),
			"owner" => actor.Owns(target),
			"inherit" => target.Inheritable(),
			_ => actor.IsSee_All()
		}).AsTask();
		try
		{
			await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(2))).IsEqualTo(budget.Token);
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(async () => await invocation.WaitAsync(TimeSpan.FromSeconds(2)));
		}
		finally
		{
			release.Cancel();
			try { await invocation; } catch (OperationCanceledException) { }
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task OutsideBudgetAndExplicitTokenCallsPreserveTheirTokenContract(bool explicitToken)
	{
		var actor = new TestObjectFactory().CreatePlayer(40, "actor");
		CancellationToken observed = default;
		async IAsyncEnumerable<SharpObjectFlag> Flags([EnumeratorCancellation] CancellationToken token = default)
		{
			observed = token;
			await Task.CompletedTask;
			yield break;
		}
		actor.Object().Flags = new(() => Flags());
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = explicitToken ? budget.Enter() : null;
		await Assert.That(explicitToken ? await actor.HasFlag("DARK", CancellationToken.None) : await actor.HasFlag("DARK")).IsFalse();
		await Assert.That(observed).IsEqualTo(CancellationToken.None);
	}
}
