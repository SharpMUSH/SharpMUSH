using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Services;

public class LinkedExecutionBudgetTests
{
	[Test]
	public async Task NoTokenPreservesTheExistingBudgetAndRestoresIt()
	{
		var original = ExecutionBudget.Current;
		using (ExecutionBudget.EnterLinked(CancellationToken.None))
			await Assert.That(ReferenceEquals(ExecutionBudget.Current, original)).IsTrue();
		using var parent = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var outer = parent.Enter();
		using (ExecutionBudget.EnterLinked(CancellationToken.None))
			await Assert.That(ReferenceEquals(ExecutionBudget.Current, parent)).IsTrue();
		await Assert.That(ReferenceEquals(ExecutionBudget.Current, parent)).IsTrue();
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task LinkedScopePreservesDeadlineAndBothCancellationSources(bool cancelParent)
	{
		using var request = new CancellationTokenSource();
		using var caller = new CancellationTokenSource();
		using var parent = new ExecutionBudget(TimeSpan.FromSeconds(30), caller.Token);
		using var outer = parent.Enter();
		using (ExecutionBudget.EnterLinked(request.Token))
		{
			await Assert.That(ExecutionBudget.Current!.Remaining <= TimeSpan.FromSeconds(30)).IsTrue();
			(cancelParent ? caller : request).Cancel();
			await Assert.That(ExecutionBudget.CurrentToken.IsCancellationRequested).IsTrue();
		}
		await Assert.That(ReferenceEquals(ExecutionBudget.Current, parent)).IsTrue();
	}
}
