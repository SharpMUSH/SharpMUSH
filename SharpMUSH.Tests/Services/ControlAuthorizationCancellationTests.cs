using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests.Server;

namespace SharpMUSH.Tests.Services;

public class ControlAuthorizationCancellationTests
{
	[Test]
	[Arguments("zone")]
	[Arguments("shared-owner")]
	[Arguments("actor-owner")]
	[Arguments("target-owner")]
	[Arguments("inherit-owner")]
	[Arguments("inherit-flags")]
	[Arguments("mistrust-flags")]
	[Arguments("guest-powers")]
	[Arguments("actor-wizard")]
	[Arguments("target-wizard")]
	[Arguments("target-royalty")]
	[Arguments("shared-flags")]
	public async Task ControlAuthorizationReadsHonorTheExecutionBudget(string stage)
	{
		var factory = new TestObjectFactory();
		var actor = factory.CreatePlayer(40, "actor");
		var owner = factory.CreatePlayer(42, "other owner");
		var target = factory.CreateThing(41, "target", owner: owner);
		actor.Object().Id = "actor"; owner.Object().Id = "owner"; target.Object().Id = "target";
		actor.AsPlayer.Id = "player-actor"; owner.AsPlayer.Id = "player-owner"; target.AsThing.Id = "thing-target";
		var options = Substitute.For<IOptionsMonitor<SharpMUSHOptions>>();
		var baseline = TestSharpMushOptions.Create();
		options.CurrentValue.Returns(baseline with { Database = baseline.Database with { ZoneControlZmpOnly = false } });
		var service = new PermissionService(Substitute.For<ILockService>(), options, Substitute.For<IRealityPolicy>(),
			Substitute.For<IConnectionService>(), new Lazy<IAttributeService>(Substitute.For<IAttributeService>()));
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		async Task<T> Block<T>(CancellationToken token)
		{
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			throw new InvalidOperationException("Blocked authorization read must cancel.");
		}
		var flagReads = 0;
		async IAsyncEnumerable<SharpObjectFlag> Flags([EnumeratorCancellation] CancellationToken token = default)
		{
			var skip = stage switch { "mistrust-flags" or "shared-flags" => 1, "target-royalty" => 2, _ => 0 };
			if (++flagReads > skip) await Block<bool>(token);
			yield break;
		}
		async IAsyncEnumerable<SharpPower> Powers([EnumeratorCancellation] CancellationToken token = default)
		{
			await Block<bool>(token);
			yield break;
		}
		switch (stage)
		{
			case "zone": target.Object().Zone = new(token => Block<AnyOptionalSharpObject>(token)); break;
			case "shared-owner":
				target.Object().Zone = new(_ =>
				{
					target.Object().Owner = new(token => Block<SharpPlayer>(token));
					return Task.FromResult<AnyOptionalSharpObject>(new None());
				});
				break;
			case "actor-owner": actor.Object().Owner = new(token => Block<SharpPlayer>(token)); break;
			case "target-owner":
			case "inherit-owner": target.Object().Owner = new(token => Block<SharpPlayer>(token)); break;
			case "inherit-flags": owner.Object().Flags = new(() => Flags()); break;
			case "mistrust-flags":
			case "actor-wizard": actor.Object().Flags = new(() => Flags()); break;
			case "target-wizard":
			case "target-royalty": target.Object().Flags = new(() => Flags()); break;
			case "shared-flags": owner.Object().Flags = new(() => Flags()); break;
			case "guest-powers": actor.Object().Powers = new(() => Powers()); break;
		}
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		var invocation = (stage == "inherit-owner" ? target.Inheritable(budget.Token) : service.Controls(actor, target)).AsTask();
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
}
