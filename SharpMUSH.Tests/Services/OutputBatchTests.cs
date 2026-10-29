using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Services;

public class OutputBatchTests
{
	[Test]
	public async Task OutsideABatchThePublicationIsAwaitedWhereItIsMade()
	{
		var publication = new TaskCompletionSource();
		var deferred = OutputBatch.Defer(publication.Task);

		await Assert.That(deferred.IsCompleted).IsFalse();
		publication.SetResult();
		await deferred;
	}

	[Test]
	public async Task InsideABatchTheBatchWaitsForEveryPublication()
	{
		var first = new TaskCompletionSource();
		var second = new TaskCompletionSource();
		ValueTask ending;
		var batch = OutputBatch.Begin();
		try
		{
			await Assert.That(OutputBatch.Defer(first.Task).IsCompleted).IsTrue();
			await Assert.That(OutputBatch.Defer(second.Task).IsCompleted).IsTrue();
		}
		finally
		{
			ending = batch.DisposeAsync();
		}

		await Assert.That(ending.IsCompleted).IsFalse();
		first.SetResult();
		await Assert.That(ending.IsCompleted).IsFalse();
		second.SetResult();
		await ending;
	}

	[Test]
	public async Task ABatchRethrowsAFailedPublicationWhenItEnds()
	{
		var failed = new TaskCompletionSource();
		var batch = OutputBatch.Begin();
		_ = OutputBatch.Defer(failed.Task);
		failed.SetException(new IOException("broker down"));

		await Assert.That(async () => await batch.DisposeAsync()).Throws<IOException>();
	}

	[Test]
	public async Task ANestedBatchJoinsTheOuterOne()
	{
		var publication = new TaskCompletionSource();
		var outer = OutputBatch.Begin();
		var inner = OutputBatch.Begin();
		_ = OutputBatch.Defer(publication.Task);

		await Assert.That(inner.DisposeAsync().IsCompleted).IsTrue();
		var ending = outer.DisposeAsync();
		await Assert.That(ending.IsCompleted).IsFalse();
		publication.SetResult();
		await ending;
	}

	[Test]
	public async Task WorkThatOutlivesItsBatchWaitsForItsOwnPublications()
	{
		var started = new TaskCompletionSource();
		var resume = new TaskCompletionSource();
		var publication = new TaskCompletionSource();
		ValueTask? late = null;
		Task straggler;
		var batch = OutputBatch.Begin();
		try
		{
			straggler = Task.Run(async () =>
			{
				started.SetResult();
				await resume.Task;
				late = OutputBatch.Defer(publication.Task);
			});
			await started.Task;
		}
		finally
		{
			await batch.DisposeAsync();
		}

		resume.SetResult();
		await straggler;
		await Assert.That(late!.Value.IsCompleted).IsFalse();
		publication.SetResult();
		await late.Value;
	}
}
