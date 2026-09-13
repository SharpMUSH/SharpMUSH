using System.Runtime.CompilerServices;
using NSubstitute;
using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Services;

public class ListenAttributeSnapshotHandlerTests
{
	[Test]
	public async Task PreCanceledRequestDoesNotReadStores()
	{
		var objects = Substitute.For<IObjectStore>();
		var attributes = Substitute.For<IAttributeStore>();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var handler = new GetListenAttributeSnapshotQueryHandler(objects, attributes);
		await Assert.ThrowsAsync<OperationCanceledException>(() => handler.Handle(new(new DBRef(42, 123)), cancellation.Token).AsTask());
		await Assert.That(objects.ReceivedCalls()).IsEmpty();
		await Assert.That(attributes.ReceivedCalls()).IsEmpty();
	}

	[Test]
	public async Task InvalidIdentityDoesNotReadAttributesByRecycledNumber()
	{
		var objects = Substitute.For<IObjectStore>();
		var attributes = Substitute.For<IAttributeStore>();
		var reference = new DBRef(42, 123);
		objects.GetObjectNodeAsync(reference, Arg.Any<CancellationToken>()).Returns(new ValueTask<AnyOptionalSharpObject>(new None()));
		var snapshot = await new GetListenAttributeSnapshotQueryHandler(objects, attributes).Handle(new(reference), CancellationToken.None);
		await Assert.That(snapshot).IsEmpty();
		await Assert.That(attributes.ReceivedCalls()).IsEmpty();
	}

	[Test]
	public async Task CancellationReachesPendingIdentityRead()
	{
		var objects = Substitute.For<IObjectStore>();
		var attributes = Substitute.For<IAttributeStore>();
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		objects.GetObjectNodeAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(async ValueTask<AnyOptionalSharpObject> (call) =>
			{
				entered.TrySetResult();
				await Task.Delay(Timeout.InfiniteTimeSpan, call.Arg<CancellationToken>());
				return new None();
			});
		var pending = new GetListenAttributeSnapshotQueryHandler(objects, attributes).Handle(new(new DBRef(42, 123)), cancellation.Token).AsTask();
		try
		{
			await entered.Task.WaitAsync(cancellation.Token);
			cancellation.Cancel();
			await Assert.ThrowsAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
			await Assert.That(attributes.ReceivedCalls()).IsEmpty();
		}
		finally { cancellation.Cancel(); }
	}

	[Test]
	public async Task CancellationDuringAttributeReadDoesNotReturnPartialSnapshot()
	{
		var objects = Substitute.For<IObjectStore>();
		var attributes = Substitute.For<IAttributeStore>();
		using var cancellation = new CancellationTokenSource();
		var node = new TestObjectFactory().CreatePlayer(42, "Snapshot");
		var reference = node.Object().DBRef;
		objects.GetObjectNodeAsync(reference, cancellation.Token).Returns(new ValueTask<AnyOptionalSharpObject>(node.WithNoneOption()));
		attributes.GetAttributesAsync(reference, "**", cancellation.Token).Returns(Partial(cancellation, cancellation.Token));
		await Assert.ThrowsAsync<OperationCanceledException>(() =>
			new GetListenAttributeSnapshotQueryHandler(objects, attributes).Handle(new(reference), cancellation.Token).AsTask());
	}

	private static async IAsyncEnumerable<SharpAttribute> Partial(CancellationTokenSource cancellation,
		[EnumeratorCancellation] CancellationToken token)
	{
		yield return new SharpAttribute("id", "key", "ACTION", [], null, "ACTION",
			new(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
			new(_ => Task.FromResult<SharpPlayer?>(null)), new(_ => Task.FromResult<SharpAttributeEntry?>(null)));
		await Task.Yield();
		cancellation.Cancel();
		token.ThrowIfCancellationRequested();
	}
}
