using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class LineDiffTests
{
	[Test]
	public async Task IdenticalTexts_AllLinesUnchanged()
	{
		var diff = LineDiff.Compute("a\nb\nc", "a\nb\nc");

		await Assert.That(diff.Count).IsEqualTo(3);
		await Assert.That(diff.All(l => l.Kind == LineDiff.LineKind.Unchanged)).IsTrue();
	}

	[Test]
	public async Task AddedLine_MarkedAdded()
	{
		var diff = LineDiff.Compute("a\nc", "a\nb\nc");

		await Assert.That(diff.Count).IsEqualTo(3);
		await Assert.That(diff[1].Kind).IsEqualTo(LineDiff.LineKind.Added);
		await Assert.That(diff[1].Text).IsEqualTo("b");
	}

	[Test]
	public async Task RemovedLine_MarkedRemoved()
	{
		var diff = LineDiff.Compute("a\nb\nc", "a\nc");

		await Assert.That(diff.Count).IsEqualTo(3);
		await Assert.That(diff[1].Kind).IsEqualTo(LineDiff.LineKind.Removed);
		await Assert.That(diff[1].Text).IsEqualTo("b");
	}

	[Test]
	public async Task ChangedLine_MarkedRemovedThenAdded()
	{
		var diff = LineDiff.Compute("a\nold\nc", "a\nnew\nc");

		var kinds = diff.Select(l => l.Kind).ToList();
		await Assert.That(kinds).Contains(LineDiff.LineKind.Removed);
		await Assert.That(kinds).Contains(LineDiff.LineKind.Added);
		await Assert.That(diff.First(l => l.Kind == LineDiff.LineKind.Removed).Text).IsEqualTo("old");
		await Assert.That(diff.First(l => l.Kind == LineDiff.LineKind.Added).Text).IsEqualTo("new");
	}

	[Test]
	public async Task EmptyOldText_AllLinesAdded()
	{
		var diff = LineDiff.Compute("", "a\nb");

		await Assert.That(diff.Count).IsEqualTo(2);
		await Assert.That(diff.All(l => l.Kind == LineDiff.LineKind.Added)).IsTrue();
	}

	[Test]
	public async Task EmptyNewText_AllLinesRemoved()
	{
		var diff = LineDiff.Compute("a\nb", "");

		await Assert.That(diff.Count).IsEqualTo(2);
		await Assert.That(diff.All(l => l.Kind == LineDiff.LineKind.Removed)).IsTrue();
	}

	[Test]
	public async Task CrlfAndLfTexts_DiffAsEqual()
	{
		var diff = LineDiff.Compute("a\r\nb", "a\nb");

		await Assert.That(diff.All(l => l.Kind == LineDiff.LineKind.Unchanged)).IsTrue();
	}
}
