using SharpMUSH.Tools.WikiSemantics.Native;

namespace SharpMUSH.Tests.Database.Lightning;

// Pure evidence-validator checks. Actual native COMMIT attribution remains covered by the child proofs.
public class CategoryCommitStackTests
{
	private const string Commit = "at SharpMUSH.Database.Lightning.Store.LightningStore.Commit(LightningTransaction tx)";
	private const string Batch = "at SharpMUSH.Database.Lightning.Store.LightningStore.WriteBatch(IReadOnlyList`1 items)";
	private const string Writer = "at SharpMUSH.Database.Lightning.Store.LightningWriter.RunBatch(List`1 jobs)";

	[Test]
	[Arguments("\n")]
	[Arguments("\r\n")]
	public async Task LongLocationsAndAsyncTailPreserveTheActualOrderedFrames(string newline)
	{
		var location = " in /" + new string('p', 4096) + "/source.cs:line 123";
		var raw = string.Join(newline, "   " + Commit + location, "   " + Batch + location,
			"   " + Writer + location, string.Concat(Enumerable.Repeat("--- unrelated async continuation ---" + newline, 100)));

		var actual = CategoryCommitStack.Capture(raw, grouped: true);

		await Assert.That(actual).IsEqualTo(string.Join('\n', Commit, Batch, Writer));
		await Assert.That(actual.Length <= 2048).IsTrue();
	}

	[Test]
	public async Task SingleProofPreservesItsCommitWitnessWithoutInventingGroupFrames()
	{
		var actual = CategoryCommitStack.Capture("   " + Commit + " in /source.cs:line 7\n"
			+ new string('x', 8192), grouped: false);

		await Assert.That(actual).IsEqualTo(Commit);
		await Assert.That(actual).DoesNotContain("RunBatch");
	}

	[Test]
	[Arguments("missing-commit")]
	[Arguments("missing-batch")]
	[Arguments("missing-writer")]
	[Arguments("out-of-order")]
	[Arguments("substring-only")]
	[Arguments("child-only")]
	[Arguments("oversized-frame")]
	[Arguments("empty")]
	public async Task InvalidGroupWitnessIsRejected(string invalid)
	{
		var raw = invalid switch
		{
			"missing-commit" => string.Join('\n', Batch, Writer),
			"missing-batch" => string.Join('\n', Commit, Writer),
			"missing-writer" => string.Join('\n', Commit, Batch),
			"out-of-order" => string.Join('\n', Writer, Batch, Commit),
			"substring-only" => string.Join('\n', "at Other.Method(String LightningStore.Commit)", Batch, Writer),
			"child-only" => string.Join('\n', "at LightningDB.LightningTransaction.Commit()", Batch, Writer),
			"oversized-frame" => string.Join('\n', Commit[..^1] + new string('x', 512) + ")", Batch, Writer),
			"empty" => "",
			_ => throw new InvalidOperationException("Unknown invalid witness fixture")
		};

		await Assert.That(() => CategoryCommitStack.Capture(raw, grouped: true)).Throws<InvalidDataException>();
	}
}
