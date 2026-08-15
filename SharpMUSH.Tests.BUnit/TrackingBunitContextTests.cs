namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// Covers both teardown paths of <see cref="TrackingBunitContext"/>.
/// <para>
/// This exists because the first version released tracked objects only from
/// <c>Dispose(disposing: true)</c>, which bUnit never reaches when the context is torn down
/// asynchronously — the whole mechanism silently did nothing, and every test still passed. A tracker
/// that quietly stops tracking is worse than none, so both paths are pinned here.
/// </para>
/// </summary>
public class TrackingBunitContextTests
{
	private sealed class Probe : TrackingBunitContext;

	private sealed class Marker : IDisposable
	{
		public int DisposeCount { get; private set; }
		public void Dispose() => DisposeCount++;
	}

	[Test]
	public async Task AsyncTeardown_ReleasesTrackedDisposables()
	{
		var marker = new Marker();
		var context = new Probe();
		context.Track(marker);

		await context.DisposeAsync();

		await Assert.That(marker.DisposeCount).IsEqualTo(1);
	}

	[Test]
	public async Task SyncDispose_ReleasesTrackedDisposables()
	{
		var marker = new Marker();
		var context = new Probe();
		context.Track(marker);

		context.Dispose();

		await Assert.That(marker.DisposeCount).IsEqualTo(1);
	}

	[Test]
	public async Task Track_ReturnsTheSameInstance()
	{
		var marker = new Marker();
		using var context = new Probe();

		await Assert.That(context.Track(marker)).IsSameReferenceAs(marker);
	}

	[Test]
	public async Task ReleasesInReverseOrderOfRegistration()
	{
		var order = new List<string>();
		var context = new Probe();
		context.Track(new Ordered("first", order));
		context.Track(new Ordered("second", order));

		await context.DisposeAsync();

		await Assert.That(string.Join(",", order)).IsEqualTo("second,first");
	}

	private sealed class Ordered(string name, List<string> order) : IDisposable
	{
		public void Dispose() => order.Add(name);
	}
}
