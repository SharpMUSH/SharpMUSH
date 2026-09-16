using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class HandlePublicationLaneTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

	private sealed class Recorder
	{
		private readonly Lock _gate = new();
		public List<string> Started { get; } = [];

		public Func<CancellationToken, Task> Publish(string name, Task? until = null) => async _ =>
		{
			lock (_gate) Started.Add(name);
			if (until is not null) await until;
		};

		public string[] Snapshot()
		{
			lock (_gate) return [.. Started];
		}
	}

	[Test]
	public async Task LaterPlaceWaitsForAnEarlierBlockedPublication()
	{
		var lane = new HandlePublicationLane();
		var recorder = new Recorder();
		var release = new TaskCompletionSource();
		var first = lane.Reserve(1);
		var second = lane.Reserve(1);

		var secondPublished = PublishThrough(lane, second, recorder.Publish("second"));
		var firstPublished = PublishThrough(lane, first, recorder.Publish("first", release.Task));

		await WaitUntil(() => recorder.Snapshot().Contains("first"));
		await Task.Delay(100);
		await Assert.That(recorder.Snapshot()).IsEquivalentTo(["first"]);

		release.SetResult();
		await Task.WhenAll(firstPublished, secondPublished).WaitAsync(Wait);
		await Assert.That(recorder.Snapshot()).IsEquivalentTo(["first", "second"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task AbandonedPlaceReleasesLaterOnes()
	{
		var lane = new HandlePublicationLane();
		var recorder = new Recorder();
		var abandoned = lane.Reserve(1);

		var later = lane.PublishAsync(1, recorder.Publish("later"), CancellationToken.None);
		await Task.Delay(100);
		await Assert.That(recorder.Snapshot()).IsEmpty();

		abandoned.Dispose();
		await later.WaitAsync(Wait);
		await Assert.That(recorder.Snapshot()).IsEquivalentTo(["later"]);
	}

	[Test]
	public async Task CancelledWaiterNeitherPublishesNorLetsLaterPlacesOvertake()
	{
		var lane = new HandlePublicationLane();
		var recorder = new Recorder();
		var release = new TaskCompletionSource();
		using var cancel = new CancellationTokenSource();

		var first = lane.PublishAsync(1, recorder.Publish("first", release.Task), CancellationToken.None);
		var cancelled = lane.PublishAsync(1, recorder.Publish("cancelled"), cancel.Token);
		var third = lane.PublishAsync(1, recorder.Publish("third"), CancellationToken.None);
		await WaitUntil(() => recorder.Snapshot().Contains("first"));

		await cancel.CancelAsync();
		await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.WaitAsync(Wait));
		await Task.Delay(100);
		await Assert.That(recorder.Snapshot()).IsEquivalentTo(["first"]);

		release.SetResult();
		await Task.WhenAll(first, third).WaitAsync(Wait);
		await Assert.That(recorder.Snapshot()).IsEquivalentTo(["first", "third"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task HandlesAreIndependent()
	{
		var lane = new HandlePublicationLane();
		var recorder = new Recorder();
		var release = new TaskCompletionSource();

		var blocked = lane.PublishAsync(1, recorder.Publish("one", release.Task), CancellationToken.None);
		await lane.PublishAsync(2, recorder.Publish("two"), CancellationToken.None).WaitAsync(Wait);

		release.SetResult();
		await blocked.WaitAsync(Wait);
		await Assert.That(recorder.Snapshot()).IsEquivalentTo(["one", "two"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task BoundPlaceIsUsedOnceAndOnlyForItsHandle()
	{
		var lane = new HandlePublicationLane();
		var recorder = new Recorder();
		var release = new TaskCompletionSource();
		var bound = lane.Reserve(1);
		var waiting = lane.PublishAsync(1, recorder.Publish("reserved later"), CancellationToken.None);

		using (lane.Bind(bound))
		{
			await lane.PublishAsync(2, recorder.Publish("other handle"), CancellationToken.None).WaitAsync(Wait);
			var throughBound = lane.PublishAsync(1, recorder.Publish("bound", release.Task), CancellationToken.None);
			var afterBound = lane.PublishAsync(1, recorder.Publish("after bound"), CancellationToken.None);
			await WaitUntil(() => recorder.Snapshot().Contains("bound"));
			release.SetResult();
			await Task.WhenAll(throughBound, afterBound, waiting).WaitAsync(Wait);
		}

		await Assert.That(recorder.Snapshot())
			.IsEquivalentTo(["other handle", "bound", "reserved later", "after bound"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	private static Task PublishThrough(HandlePublicationLane lane, HandlePublicationLane.Slot slot, Func<CancellationToken, Task> publish)
		=> Task.Run(async () =>
		{
			using (lane.Bind(slot)) await lane.PublishAsync(slot.Handle, publish, CancellationToken.None);
		});

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + Wait;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline) throw new TimeoutException();
			await Task.Delay(10);
		}
	}
}
