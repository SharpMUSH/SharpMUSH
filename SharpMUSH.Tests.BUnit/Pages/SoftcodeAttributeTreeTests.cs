using SharpMUSH.Client.Models;
using SharpMUSH.Client.Pages;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// How the Softcode Editor arranges an object's attributes by their backtick-separated names, without
/// rendering the page.
/// </summary>
public class SoftcodeAttributeTreeTests
{
	private static List<MushAttribute> Attributes(params string[] names) =>
		names.Select(n => new MushAttribute { Name = n, Value = n.ToLowerInvariant() }).ToList();

	private static readonly HashSet<string> NothingExpanded = [];

	private static List<string> Paths(IEnumerable<SoftcodeAttributeNode> rows) => rows.Select(r => r.Path).ToList();

	[Test]
	public async Task Build_NestsEachAttributeUnderTheBranchItsNameNames()
	{
		// The API lists a tree level by level, so a leaf can arrive before its branch.
		var tree = SoftcodeAttributeTree.Build(Attributes("DESCRIBE", "CMD", "FN", "CMD`WHO", "FN`B", "FN`A", "CMD`WHO`FORMAT"));

		await Assert.That(Paths(tree)).IsEquivalentTo(["CMD", "DESCRIBE", "FN"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var cmd = tree[0];
		await Assert.That(cmd.Depth).IsEqualTo(0);
		await Assert.That(cmd.DescendantCount).IsEqualTo(2);
		await Assert.That(cmd.Children.Single().Segment).IsEqualTo("WHO");
		await Assert.That(cmd.Children.Single().Children.Single().Path).IsEqualTo("CMD`WHO`FORMAT");
		await Assert.That(cmd.Children.Single().Children.Single().Depth).IsEqualTo(2);

		// Siblings are alphabetical, whatever order they were listed in.
		await Assert.That(Paths(tree[2].Children)).IsEquivalentTo(["FN`A", "FN`B"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(tree[1].IsBranch).IsFalse();
	}

	[Test]
	public async Task Build_KeepsALeafWhoseBranchWasNotListed()
	{
		// The listing stops at a depth and at what the reader may see; the leaf still gets a place.
		var tree = SoftcodeAttributeTree.Build(Attributes("DATA`SCORE"));

		var data = tree.Single();
		await Assert.That(data.Path).IsEqualTo("DATA");
		await Assert.That(data.Attribute).IsNull();
		await Assert.That(data.Children.Single().Attribute!.Name).IsEqualTo("DATA`SCORE");
	}

	[Test]
	public async Task Rows_ShowOnlyWhatExpandedBranchesHold()
	{
		var tree = SoftcodeAttributeTree.Build(Attributes("CMD", "CMD`WHO", "CMD`WHO`FORMAT", "DESCRIBE"));

		await Assert.That(Paths(SoftcodeAttributeTree.Rows(tree, NothingExpanded, null)))
			.IsEquivalentTo(["CMD", "DESCRIBE"], TUnit.Assertions.Enums.CollectionOrdering.Matching);

		var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cmd" };
		await Assert.That(Paths(SoftcodeAttributeTree.Rows(tree, expanded, null)))
			.IsEquivalentTo(["CMD", "CMD`WHO", "DESCRIBE"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Rows_WhileFiltering_ShowEveryMatchWithTheBranchesLeadingToIt()
	{
		var tree = SoftcodeAttributeTree.Build(Attributes("CMD", "CMD`WHO", "CMD`WHO`FORMAT", "CMD`WHERE", "DESCRIBE"));

		await Assert.That(Paths(SoftcodeAttributeTree.Rows(tree, NothingExpanded, "format")))
			.IsEquivalentTo(["CMD", "CMD`WHO", "CMD`WHO`FORMAT"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
	}

	[Test]
	public async Task Ancestors_AreTheBranchesAboveAName_OutermostFirst()
	{
		await Assert.That(SoftcodeAttributeTree.Ancestors("FOO`BAR`BAZ").ToList())
			.IsEquivalentTo(["FOO", "FOO`BAR"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(SoftcodeAttributeTree.Ancestors("FOO").ToList()).IsEmpty();
		await Assert.That(SoftcodeAttributeTree.Segment("FOO`BAR`BAZ")).IsEqualTo("BAZ");
	}
}
