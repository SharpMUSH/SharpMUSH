using NSubstitute;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;

namespace SharpMUSH.Tests.Services;

public class RealityPolicyCancellationTests
{
	[Test]
	public async Task ExplicitRequestTokenIsPreservedAlongsideAnAmbientBudget()
	{
		var store = Substitute.For<IExpandedDataStore>();
		var policy = new RealityPolicy(store, Substitute.For<IObjectStore>());
		using var request = new CancellationTokenSource();
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		await policy.IsEnabledAsync(request.Token);
		await store.Received(1).GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, request.Token);
	}

	[Test]
	[Arguments("enabled", "configuration")]
	[Arguments("configuration", "configuration")]
	[Arguments("perception", "configuration")]
	[Arguments("perception", "identity")]
	[Arguments("perception", "profile")]
	[Arguments("description", "profile")]
	[Arguments("observation", "profile")]
	[Arguments("object", "identity")]
	[Arguments("object", "profile")]
	[Arguments("scan", "identity")]
	[Arguments("scan", "profile")]
	public async Task DefaultPolicyReadsInheritTheCurrentExecutionBudget(string operation, string stage)
	{
		var factory = new TestObjectFactory();
		var receiver = factory.CreatePlayer(40, "receiver");
		var target = factory.CreatePlayer(41, "target");
		receiver.Object().Id = "receiver";
		target.Object().Id = "target";
		var store = Substitute.For<IExpandedDataStore>();
		var objects = Substitute.For<IObjectStore>();
		store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
			.Returns(new RealityConfiguration(1, true, ["normal"]));
		objects.GetObjectNodeAsync(receiver.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(receiver.AsPlayer));
		objects.GetObjectNodeAsync(target.Object().DBRef, Arg.Any<CancellationToken>()).Returns(new AnyOptionalSharpObject(target.AsPlayer));
		var blocked = operation == "scan" ? target : receiver;
		var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
		using var release = new CancellationTokenSource();
		async ValueTask<T> Wait<T>(CancellationToken token)
		{
			entered.TrySetResult(token);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, release.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
			throw new InvalidOperationException("Blocked provider must be cancelled.");
		}
		if (stage == "configuration")
			store.GetExpandedServerData<RealityConfiguration>(RealityPolicy.ConfigurationKey, Arg.Any<CancellationToken>())
				.Returns(call => Wait<RealityConfiguration?>(call.Arg<CancellationToken>()));
		else if (stage == "identity")
			objects.GetObjectNodeAsync(blocked.Object().DBRef, Arg.Any<CancellationToken>())
				.Returns(call => Wait<AnyOptionalSharpObject>(call.Arg<CancellationToken>()));
		else
			store.GetExpandedObjectData<ObjectReality>(blocked.Object().Id!, RealityPolicy.ObjectKey, Arg.Any<CancellationToken>())
				.Returns(call => Wait<ObjectReality?>(call.Arg<CancellationToken>()));

		var policy = new RealityPolicy(store, objects);
		using var cancellation = new CancellationTokenSource();
		using var budget = new ExecutionBudget(Timeout.InfiniteTimeSpan, cancellation.Token);
		using var scope = budget.Enter();
		async Task Invoke()
		{
			switch (operation)
			{
				case "enabled": await policy.IsEnabledAsync(); break;
				case "configuration": await policy.ConfigurationAsync(); break;
				case "perception": await policy.CanPerceiveAsync(receiver.Object().DBRef, target.Object().DBRef); break;
				case "description": await policy.DescriptionAttributeAsync(receiver.Object().DBRef, target.Object().DBRef); break;
				case "object": await policy.ReadObjectAsync(receiver.Object().DBRef); break;
				case "observation": await policy.ObserveAsync(receiver.Object().DBRef); break;
				case "scan":
					var observe = await policy.ObserveAsync(receiver.Object().DBRef);
					await observe(target.Object().DBRef, default);
					break;
			}
		}
		var invocation = Invoke();
		try
		{
			await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo(budget.Token);
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
