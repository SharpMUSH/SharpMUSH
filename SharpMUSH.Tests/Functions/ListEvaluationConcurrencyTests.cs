using System.Linq.Expressions;
using System.Reflection;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class ListEvaluationConcurrencyTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	public async Task ConcurrentComparisonsCannotEraseAnEarlierFailure()
	{
		var type = typeof(SharpMUSH.Implementation.Functions.Functions).GetNestedType("ListEvaluationErrors", BindingFlags.NonPublic)!;
		var receiver = Expression.Parameter(typeof(object));
		var value = Expression.Parameter(typeof(CallState));
		var record = Expression.Lambda<Action<object, CallState>>(Expression.Block(
			Expression.Call(Expression.Convert(receiver, type), type.GetMethod("Record")!, value),
			Expression.Empty()), receiver, value).Compile();
		var complete = Expression.Lambda<Func<object, CallState>>(Expression.Call(
			Expression.Convert(receiver, type), type.GetMethod("Complete")!, Expression.Constant(CallState.Empty)), receiver).Compile();
		const int rounds = 20000;
		var clean = CallState.Empty;
		var failed = CallState.Empty with { HadErrors = true };
		var accumulators = Enumerable.Range(0, rounds).Select(_ => Activator.CreateInstance(type, true)!).ToArray();
		using var barrier = new Barrier(4);
		var workers = Enumerable.Range(0, 4).Select(worker => Task.Factory.StartNew(() =>
		{
			for (var round = 0; round < rounds; round++)
			{
				barrier.SignalAndWait();
				record(accumulators[round], worker == 0 ? failed : clean);
				barrier.SignalAndWait();
			}
		}, CancellationToken.None, TaskCreationOptions.LongRunning, System.Threading.Tasks.TaskScheduler.Default)).ToArray();
		await Task.WhenAll(workers);
		await Assert.That(accumulators.Count(accumulator => !complete(accumulator).HadErrors)).IsEqualTo(0);
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task RawEmptyDelimiterRetainsExistingMetadataAtActualFunctionBoundary(bool failed)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var entry in original.FunctionLibrary) library.Add(entry.Key, entry.Value);
		library.Add("emptyprobe", (new FunctionDefinition(
			new SharpFunctionAttribute { Name = "emptyprobe", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 0 },
			_ => ValueTask.FromResult(CallState.Empty with { HadErrors = failed })), true));
		var parser = original with { FunctionLibrary = library };
		var result = await parser.FunctionParse(MarkupText.Plain("revwords(a b,emptyprobe())"));
		await Assert.That(result!.Message!.Text).IsEqualTo("b a");
		await Assert.That(result.HadErrors).IsEqualTo(failed);
	}
}
