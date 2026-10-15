using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Packages;

/// <summary>The line-based three-way merge an upgrade runs on a wiki page's body.</summary>
public class LineMergeTests
{
	private const string Base = "# Coast Road\nIntro line.\nMiddle line.\nLast line.";

	[Test]
	public async Task EditsToDifferentLinesMergeCleanly()
	{
		var ours = "# Coast Road\nIntro line, edited by staff.\nMiddle line.\nLast line.";
		var theirs = "# Coast Road\nIntro line.\nMiddle line.\nLast line, edited by the package.";

		var merge = LineMerge.Merge(Base, ours, theirs);

		await Assert.That(merge.IsClean).IsTrue();
		await Assert.That(merge.Resolve(preferOurs: true))
			.IsEqualTo("# Coast Road\nIntro line, edited by staff.\nMiddle line.\nLast line, edited by the package.");
		await Assert.That(merge.Resolve(preferOurs: false)).IsEqualTo(merge.Resolve(preferOurs: true));
	}

	[Test]
	public async Task AnInsertionAndADeletionElsewhereMergeCleanly()
	{
		var ours = "# Coast Road\nA staff note.\nIntro line.\nMiddle line.\nLast line.";
		var theirs = "# Coast Road\nIntro line.\nLast line.";

		var merge = LineMerge.Merge(Base, ours, theirs);

		await Assert.That(merge.IsClean).IsTrue();
		await Assert.That(merge.Resolve(preferOurs: true)).IsEqualTo("# Coast Road\nA staff note.\nIntro line.\nLast line.");
	}

	[Test]
	public async Task TheSameEditOnBothSidesIsNoClash()
	{
		var both = "# Coast Road\nIntro line.\nMiddle line, fixed.\nLast line.";

		var merge = LineMerge.Merge(Base, both, both);

		await Assert.That(merge.IsClean).IsTrue();
		await Assert.That(merge.Resolve(preferOurs: true)).IsEqualTo(both);
	}

	[Test]
	public async Task EditsToTheSameLineClash()
	{
		var ours = "# Coast Road\nIntro line.\nMiddle line, staff.\nLast line.";
		var theirs = "# Coast Road\nIntro line.\nMiddle line, package.\nLast line, package.";

		var merge = LineMerge.Merge(Base, ours, theirs);

		await Assert.That(merge.IsClean).IsFalse();
		await Assert.That(merge.Conflicts.Count).IsEqualTo(1);
		await Assert.That(merge.Resolve(preferOurs: true))
			.IsEqualTo("# Coast Road\nIntro line.\nMiddle line, staff.\nLast line.");
		await Assert.That(merge.Resolve(preferOurs: false))
			.IsEqualTo("# Coast Road\nIntro line.\nMiddle line, package.\nLast line, package.");
	}

	[Test]
	public async Task AClashIsMarkedWithTheGamesLinesFirst()
	{
		var ours = "# Coast Road\nIntro line, staff.\nMiddle line.\nLast line.";
		var theirs = "# Coast Road\nIntro line, package.\nMiddle line.\nLast line, package.";

		var marked = LineMerge.Merge(Base, ours, theirs).Marked("game", "coast-guide 1.1.0");

		await Assert.That(marked).IsEqualTo(
			"# Coast Road\n<<<<<<< game\nIntro line, staff.\n=======\nIntro line, package.\n>>>>>>> coast-guide 1.1.0\n"
			+ "Middle line.\nLast line, package.");
	}

	[Test]
	public async Task OneClashAmongCleanRegionsKeepsTheCleanOnes()
	{
		var baseText = "a\nb\nc\nd\ne";
		var ours = "a\nB-ours\nc\nd\ne-ours";
		var theirs = "a-theirs\nB-theirs\nc\nd\ne";

		var merge = LineMerge.Merge(baseText, ours, theirs);

		await Assert.That(merge.Conflicts.Count).IsEqualTo(1);
		await Assert.That(merge.Resolve(preferOurs: true)).IsEqualTo("a\nB-ours\nc\nd\ne-ours");
		await Assert.That(merge.Resolve(preferOurs: false)).IsEqualTo("a-theirs\nB-theirs\nc\nd\ne-ours");
	}

	[Test]
	public async Task ALargeBodyMergesWithinTheTable()
	{
		var lines = Enumerable.Range(0, 3000).Select(i => $"line {i}").ToArray();
		var baseText = string.Join('\n', lines);
		var ours = string.Join('\n', lines.Select((l, i) => i == 10 ? "staff" : l));
		var theirs = string.Join('\n', lines.Select((l, i) => i == 2900 ? "package" : l));

		var merge = LineMerge.Merge(baseText, ours, theirs);

		await Assert.That(merge.IsClean).IsTrue();
		var merged = merge.Resolve(preferOurs: true).Split('\n');
		await Assert.That(merged[10]).IsEqualTo("staff");
		await Assert.That(merged[2900]).IsEqualTo("package");
		await Assert.That(merged.Length).IsEqualTo(3000);
	}
}
