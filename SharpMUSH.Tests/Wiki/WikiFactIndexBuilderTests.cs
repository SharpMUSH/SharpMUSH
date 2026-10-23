using Mediator;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

public class WikiFactIndexBuilderTests
{
	[Test]
	public async Task StoppingWaitsForTheBatchBeingWrittenAndStartsNoOther()
	{
		var batch = new TaskCompletionSource<WikiFactIndexStatus>();
		var mediator = Substitute.For<IMediator>();
		mediator.Send(Arg.Any<BuildWikiFactIndexBatchCommand>(), Arg.Any<CancellationToken>())
			.Returns(_ => new ValueTask<WikiFactIndexStatus>(batch.Task));
		using var builder = new WikiFactIndexBuilder(mediator, NullLogger<WikiFactIndexBuilder>.Instance);

		builder.Start();
		while (mediator.ReceivedCalls().Count() == 0) await Task.Yield();
		var stopped = builder.StopAsync();
		await Assert.That(stopped.IsCompleted).IsFalse();

		batch.SetResult(new WikiFactIndexStatus(WikiFactIndexPhase.Building, 1, 2));
		await stopped;
		await Assert.That(builder.Idle.IsCompleted).IsTrue();
		await Assert.That(mediator.ReceivedCalls().Count()).IsEqualTo(1);
	}
}
