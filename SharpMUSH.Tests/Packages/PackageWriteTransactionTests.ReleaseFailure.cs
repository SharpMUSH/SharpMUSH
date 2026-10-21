using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Packages;

public partial class PackageWriteTransactionTests
{
	/// <summary>Throws before it returns a task, as an inconsistent fixture ownership does.</summary>
	private sealed class SynchronouslyFailingRetention : IAsyncDisposable
	{
		public ValueTask DisposeAsync() => throw new InvalidOperationException("Fixture operation ownership is inconsistent.");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task ARetentionThatThrowsSynchronouslyStillFaultsEveryCloser(bool explicitRevert)
	{
		var factory = (PackageWriteTransactionFactory)WebAppFactoryArg.Services.GetRequiredService<IPackageWriteTransactionFactory>();
		var writes = factory.Issue(await PackageManagerAsync(), new SynchronouslyFailingRetention());
		var first = explicitRevert ? writes.RevertAsync(new SharpMUSH.Library.DiscriminatedUnions.Error<string>("failure")) : writes.DisposeAsync().AsTask();
		var second = writes.DisposeAsync().AsTask();
		// Nothing was tracked, so recovery runs to the release before returning: both closers are already faulted.
		await Assert.That(first.IsFaulted).IsTrue();
		await Assert.That(second.IsFaulted).IsTrue();
		await Assert.That(async () => await second).Throws<InvalidOperationException>();
	}
}
