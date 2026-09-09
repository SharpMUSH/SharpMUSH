using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class RestrictedEvaluationContextTests
{
	[Test]
	public async Task NestedContextsIntersectAndRestoreAfterExceptions()
	{
		var outer = new EvaluationRestrictions(["add"]);
		using (outer.Enter())
		{
			try
			{
				using var inner = new EvaluationRestrictions(["add", "sub"]).Enter();
				await Assert.That(EvaluationRestrictions.Current!.Allows("add")).IsTrue();
				await Assert.That(EvaluationRestrictions.Current.Allows("sub")).IsFalse();
				await Assert.That(EvaluationRestrictions.Current.AllowObjectDataAccess).IsFalse();
				throw new InvalidOperationException("test unwind");
			}
			catch (InvalidOperationException) { }
			await Assert.That(EvaluationRestrictions.Current).IsEqualTo(outer);
		}
		await Assert.That(EvaluationRestrictions.Current).IsNull();
	}

	[Test]
	public async Task ConcurrentContextsNeverShareAllowlists()
	{
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var first = Task.Run(async () =>
		{
			using var scope = new EvaluationRestrictions(["add"]).Enter();
			entered.SetResult();
			await finish.Task;
			await Assert.That(EvaluationRestrictions.Current!.Allows("add")).IsTrue();
			await Assert.That(EvaluationRestrictions.Current.Allows("sub")).IsFalse();
		});
		await entered.Task;
		try
		{
			using var scope = new EvaluationRestrictions(["sub"]).Enter();
			await Assert.That(EvaluationRestrictions.Current!.Allows("sub")).IsTrue();
			await Assert.That(EvaluationRestrictions.Current.Allows("add")).IsFalse();
		}
		finally { finish.SetResult(); }
		await first;
		await Assert.That(EvaluationRestrictions.Current).IsNull();
	}
}
