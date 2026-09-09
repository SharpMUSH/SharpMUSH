using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Parser;

public class ExecutionBudgetTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task CancelledParserDoesNotReportDeadlineExpiry()
	{
		using var source = new CancellationTokenSource();
		using var budget = ExecutionBudget.FromMilliseconds(0, source.Token);
		source.Cancel();
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(Factory.ExecutorDBRef) with { ExecutionBudget = budget });
		var cancelled = false;
		try { await parser.FunctionParse(MarkupText.Plain("add(1,2)")); }
		catch (OperationCanceledException) { cancelled = true; }
		await Assert.That(cancelled).IsTrue();
		await Assert.That(budget.IsExpired).IsFalse();
	}

	[Test]
	public async Task NestedParserCannotResetAnExpiredDeadline()
	{
		using var budget = new ExecutionBudget(TimeSpan.Zero);
		var parser = Factory.FunctionParser.FromState(ParserState.RootFor(Factory.ExecutorDBRef) with { ExecutionBudget = budget });
		var result = await parser.FunctionParse(MarkupText.Plain("add(1,add(2,3))"));
		await Assert.That(result?.Message?.ToPlainText()).IsEqualTo(ExecutionBudget.Error);
		var unrelated = await Factory.FunctionParser.FunctionParse(MarkupText.Plain("add(1,2)"));
		await Assert.That(unrelated?.Message?.ToPlainText()).IsEqualTo("3");
	}

	[Test]
	public async Task SlowSqlConsumesTheSharedDeadlineAndNextEvaluationStillWorks()
	{
		var sql = Factory.Services.GetRequiredService<ISqlService>();
		using var budget = new ExecutionBudget(TimeSpan.FromMilliseconds(200));
		var watch = System.Diagnostics.Stopwatch.StartNew();
		var cancelled = false;
		using (budget.Enter())
		{
			try { await sql.ExecuteQueryAsync("SELECT SLEEP(10)"); }
			catch (OperationCanceledException) { cancelled = true; }
		}
		await Assert.That(cancelled).IsTrue();
		await Assert.That(watch.Elapsed).IsLessThan(TimeSpan.FromSeconds(5));
		var next = await sql.ExecuteQueryAsync("SELECT 1");
		await Assert.That(next.Single().Values.Single()!.ToString()).IsEqualTo("1");
	}
}
