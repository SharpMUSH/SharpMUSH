using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Two imports overlapping in time must not see each other's PennMUSH-to-SharpMUSH dbref mapping.
/// </summary>
/// <remarks>
/// The converter is a singleton, so any per-conversion state it keeps on itself is shared by every
/// caller. A second import starting while a first is still running clears the mapping the first is
/// midway through using, and both then write into the same plain
/// <see cref="Dictionary{TKey,TValue}"/> — which throws "Operations that change non-concurrent
/// collections must have exclusive access" when the writes actually collide, and silently resolves
/// one import's attributes, parents and exit links through the other's mapping when they do not.
/// <para>Both imports run against one <see cref="IsolatedImportWorld"/>'s converter, so they share its
/// singleton as they would in the server, and nothing they create lands in the shared session world.</para>
/// </remarks>
[NotInParallel]
public class PennMUSHConcurrentImportTests
{
	private const int ObjectCount = 20;

	private static readonly TimeSpan HandoffTimeout = TimeSpan.FromSeconds(30);

	private static PennMUSHDatabase Fixture(string tag, int firstDbref) => new()
	{
		Version = $"Concurrent Fixture {tag}",
		Objects = [.. Enumerable.Range(0, ObjectCount).Select(i => new PennMUSHObject
		{
			DBRef = firstDbref + i,
			Name = $"Concurrent{tag}Thing{i}",
			Type = PennMUSHObjectType.Thing,
			CreationTime = 1_333_333_333L + i,
			ModificationTime = 1_333_333_333L + i,
			Attributes =
			[
				new PennMUSHAttribute { Name = $"IMPORT{tag}", Value = $"{tag}-{i}", Flags = [] }
			]
		})]
	};

	/// <summary>
	/// Invokes the callback on the reporting thread. <see cref="Progress{T}"/> posts instead, which
	/// would let the conversion run on past the handoff this test needs to hold it at.
	/// </summary>
	private sealed class InlineProgress(Action<ConversionProgress> onReport) : IProgress<ConversionProgress>
	{
		public void Report(ConversionProgress value) => onReport(value);
	}

	/// <summary>
	/// Holds one import between its object phase and its attribute phase, runs a second import to
	/// completion in that window, and then lets the first finish. Deterministic where a free-running
	/// pair is not: the second import's start is guaranteed to fall inside the first import's run.
	/// </summary>
	[Test]
	public async Task AnImportStartedMidwayThroughAnotherDoesNotStealItsDbrefMapping()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var converter = world.Converter;

		var objectsCreated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var secondImportDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var held = false;

		var progress = new InlineProgress(report =>
		{
			if (held || report.ProcessedObjects < ObjectCount)
			{
				return;
			}

			held = true;
			objectsCreated.SetResult();
			// Wait returns whether it succeeded. Discarding it would let a timed-out handoff resume
			// the first import anyway: the overlap never happens, the assertions still pass, and the
			// test reports success without having exercised concurrency at all.
			if (!secondImportDone.Task.Wait(HandoffTimeout))
			{
				throw new TimeoutException(
					"The second import did not finish inside the handoff window, so the two never overlapped.");
			}
		});

		var first = Task.Run(() => converter.ConvertDatabaseAsync(Fixture("Held", 5100), progress));

		// Also unblocks on `first` completing, so an import that faults before the handoff fails on
		// its own reported error rather than on this wait timing out.
		await Task.WhenAny(objectsCreated.Task, first).WaitAsync(HandoffTimeout);

		var second = await converter.ConvertDatabaseAsync(Fixture("Interloper", 5200));
		secondImportDone.SetResult();

		var firstResult = await first;

		await Assert.That(second.Errors).IsEmpty()
			.Because($"the interloping import reported: {string.Join(" | ", second.Errors)}");
		await Assert.That(second.ThingsConverted).IsEqualTo(ObjectCount);
		await Assert.That(second.AttributesConverted).IsEqualTo(ObjectCount);

		await Assert.That(firstResult.Errors).IsEmpty()
			.Because($"the held import reported: {string.Join(" | ", firstResult.Errors)}");
		await Assert.That(firstResult.ThingsConverted).IsEqualTo(ObjectCount);
		await Assert.That(firstResult.AttributesConverted).IsEqualTo(ObjectCount)
			.Because("the held import lost its dbref mapping to the one that started underneath it");
	}

	/// <summary>
	/// The unsynchronised half: several imports genuinely in parallel, all writing the mapping at
	/// once. Non-deterministic by nature — it is the collision the barrier test cannot stage — so it
	/// guards the fix rather than proving the defect.
	/// </summary>
	[Test]
	public async Task ImportsRunningInParallelEachConvertTheirOwnObjects()
	{
		await using var world = await IsolatedImportWorld.CreateAsync();
		var converter = world.Converter;
		string[] tags = ["P0", "P1", "P2", "P3"];

		var results = await Task.WhenAll(tags.Select((tag, index) =>
			Task.Run(() => converter.ConvertDatabaseAsync(Fixture(tag, 5300 + index * 100)))));

		foreach (var (result, tag) in results.Zip(tags))
		{
			await Assert.That(result.Errors).IsEmpty()
				.Because($"import {tag} reported: {string.Join(" | ", result.Errors)}");
			await Assert.That(result.ThingsConverted).IsEqualTo(ObjectCount);
			await Assert.That(result.AttributesConverted).IsEqualTo(ObjectCount);
		}
	}
}
