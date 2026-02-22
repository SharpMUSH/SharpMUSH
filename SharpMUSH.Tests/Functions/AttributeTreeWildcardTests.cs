using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class AttributeTreeWildcardTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// Setup helper to create a comprehensive attribute tree for testing
	private async Task SetupAttributeTree()
	{
		// Create a comprehensive attribute tree structure:
		// ROOT
		// ROOT`CHILD1
		// ROOT`CHILD1`GRANDCHILD1
		// ROOT`CHILD1`GRANDCHILD2
		// ROOT`CHILD2
		// ROOT`CHILD2`GRANDCHILD3
		// ROOTOTHER (similar name but not part of tree)

		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOT,root_value)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOT`CHILD1,child1_value)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOT`CHILD1`GRANDCHILD1,gc1_value)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOT`CHILD1`GRANDCHILD2,gc2_value)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOT`CHILD2,child2_value)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOT`CHILD2`GRANDCHILD3,gc3_value)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/ROOTOTHER,other_value)]"));
	}

	/// <summary>
	/// Test Case 1: Pattern "ROOT*" should match only ROOT and ROOTOTHER, NOT any with backticks
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildcard_Star_NoBacktick()
	{
		await SetupAttributeTree();
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/ROOT*)]")))?.Message!;

		// * should NOT match backtick, so only ROOT and ROOTOTHER should match
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x).ToArray();

		await Assert.That(attrs).Contains("ROOT");
		await Assert.That(attrs).Contains("ROOTOTHER");
		await Assert.That(attrs).DoesNotContain("ROOT`CHILD1");
		await Assert.That(attrs).DoesNotContain("ROOT`CHILD1`GRANDCHILD1");
	}

	/// <summary>
	/// Test Case 2: Pattern "ROOT**" should match ROOT and all descendants
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildcard_DoubleStar_MatchAll()
	{
		await SetupAttributeTree();
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/ROOT**)]")))?.Message!;

		// ** should match everything including backticks
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("ROOT");
		await Assert.That(attrs).Contains("ROOT`CHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD2");
		await Assert.That(attrs).Contains("ROOT`CHILD2");
		await Assert.That(attrs).Contains("ROOT`CHILD2`GRANDCHILD3");
		await Assert.That(attrs).Contains("ROOTOTHER");
	}

	/// <summary>
	/// Test Case 3: Pattern "ROOT`*" should match only immediate children under ROOT
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildcard_ImmediateChildren()
	{
		await SetupAttributeTree();
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/ROOT`*)]")))?.Message!;

		// ROOT`* should match only immediate children (no deeper nesting)
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("ROOT`CHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD2");
		await Assert.That(attrs).DoesNotContain("ROOT"); // No backtick, so excluded
		await Assert.That(attrs).DoesNotContain("ROOT`CHILD1`GRANDCHILD1"); // Has extra backtick
		await Assert.That(attrs).DoesNotContain("ROOTOTHER"); // Different attribute
	}

	/// <summary>
	/// Test Case 4: Pattern "ROOT`**" should match entire tree under ROOT (excluding ROOT itself)
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildcard_EntireSubtree()
	{
		await SetupAttributeTree();
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/ROOT`**)]")))?.Message!;

		// ROOT`** should match all descendants but not ROOT itself
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("ROOT`CHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD2");
		await Assert.That(attrs).Contains("ROOT`CHILD2");
		await Assert.That(attrs).Contains("ROOT`CHILD2`GRANDCHILD3");
		await Assert.That(attrs).DoesNotContain("ROOT"); // Pattern starts with backtick
		await Assert.That(attrs).DoesNotContain("ROOTOTHER"); // Different attribute
	}

	/// <summary>
	/// Test Case 5: Pattern "ROOT`CHILD1`*" should match only immediate grandchildren under ROOT`CHILD1
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildcard_Grandchildren()
	{
		await SetupAttributeTree();
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/ROOT`CHILD1`*)]")))?.Message!;

		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD2");
		await Assert.That(attrs).DoesNotContain("ROOT");
		await Assert.That(attrs).DoesNotContain("ROOT`CHILD1");
		await Assert.That(attrs).DoesNotContain("ROOT`CHILD2");
	}

	/// <summary>
	/// Test Case 6: Question mark wildcard "ROOT`CHILD?" should match single character
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildcard_QuestionMark()
	{
		await SetupAttributeTree();
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/ROOT`CHILD?)]")))?.Message!;

		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("ROOT`CHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD2");
		await Assert.That(attrs).DoesNotContain("ROOT");
		await Assert.That(attrs).DoesNotContain("ROOT`CHILD1`GRANDCHILD1");
	}

	/// <summary>
	/// Test Case 7: Test with nattr() function to count attributes
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Nattr_Counting()
	{
		await SetupAttributeTree();

		// Count with * (should not include tree branches)
		var count1 = (await Parser.FunctionParse(MModule.single("[nattr(%!/ROOT*)]")))?.Message!.ToPlainText();
		await Assert.That(count1).IsEqualTo("2"); // ROOT and ROOTOTHER

		// Count with ** (should include all)
		var count2 = (await Parser.FunctionParse(MModule.single("[nattr(%!/ROOT**)]")))?.Message!.ToPlainText();
		await Assert.That(count2).IsEqualTo("7"); // All 7 attributes

		// Count immediate children
		var count3 = (await Parser.FunctionParse(MModule.single("[nattr(%!/ROOT`*)]")))?.Message!.ToPlainText();
		await Assert.That(count3).IsEqualTo("2"); // CHILD1 and CHILD2
	}

	/// <summary>
	/// Test Case 8: Test with grep() to search attribute values in tree
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Grep_InAttributeTree()
	{
		await SetupAttributeTree();

		// grep with * should not traverse tree
		var result1 = (await Parser.FunctionParse(MModule.single("[grep(%!,ROOT*,value)]")))?.Message!;
		var attrs1 = result1.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();
		await Assert.That(attrs1).Contains("ROOT");
		await Assert.That(attrs1).Contains("ROOTOTHER");
		await Assert.That(attrs1).DoesNotContain("ROOT`CHILD1");

		// grep with ** should traverse entire tree
		var result2 = (await Parser.FunctionParse(MModule.single("[grep(%!,ROOT**,child)]")))?.Message!;
		var attrs2 = result2.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();
		await Assert.That(attrs2).Contains("ROOT`CHILD1");
		await Assert.That(attrs2).Contains("ROOT`CHILD2");
		await Assert.That(attrs2).DoesNotContain("ROOT"); // "root_value" doesn't contain "child"
	}

	/// <summary>
	/// Test Case 9: Test with reglattr() using regex patterns
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Reglattr_WithRegex()
	{
		await SetupAttributeTree();

		// Match all attributes starting with ROOT (including tree)
		var result = (await Parser.FunctionParse(MModule.single("[reglattr(%!/^ROOT)]")))?.Message!;
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("ROOT");
		await Assert.That(attrs).Contains("ROOT`CHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD1`GRANDCHILD1");
		await Assert.That(attrs).Contains("ROOTOTHER");

		// Match only ROOT`CHILDx (not grandchildren)
		// Pattern uses [12] as a regex character class to match '1' or '2'
		// Brackets are escaped with \\[ \\] for the MUSH parser
		var result2 = (await Parser.FunctionParse(MModule.single("[reglattr(%!/ROOT`CHILD\\[12\\])]")))?.Message!;
		var attrs2 = result2.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		// The regex should find attributes matching ROOT`CHILD1 or ROOT`CHILD2
		await Assert.That(attrs2.Length).IsGreaterThanOrEqualTo(2);
		await Assert.That(attrs2.Any(a => a.Contains("CHILD1"))).IsTrue();
		await Assert.That(attrs2.Any(a => a.Contains("CHILD2"))).IsTrue();
	}

	/// <summary>
	/// Test Case 10: Test wildgrep() with wildcards in attribute tree
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Wildgrep_InAttributeTree()
	{
		await SetupAttributeTree();

		// wildgrep searches attribute VALUES (not names) for the wildcard pattern
		// The pattern ** in the attribute name matches the entire tree
		// The pattern *child* in the value matches values containing "child"
		var result = (await Parser.FunctionParse(MModule.single("[wildgrep(%!,ROOT**,*child*)]")))?.Message!;
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		// Should find ROOT`CHILD1 and ROOT`CHILD2 because their values 
		// ("child1_value" and "child2_value") contain "child"
		await Assert.That(attrs).Contains("ROOT`CHILD1");
		await Assert.That(attrs).Contains("ROOT`CHILD2");
	}

	/// <summary>
	/// Test Case 11: Test with xattr() to get ranged attribute list
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_Xattr_RangeInTree()
	{
		await SetupAttributeTree();

		// Get first 2 attributes matching ROOT** pattern
		var result = (await Parser.FunctionParse(MModule.single("[xattr(%!/ROOT**,1,2)]")))?.Message!;
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		// Should return first 2 attributes in sorted order
		await Assert.That(attrs.Length).IsEqualTo(2);
		// The exact attributes depend on sort order, but should be from the ROOT** set
	}

	/// <summary>
	/// Test Case 12: Verify special characters are properly escaped
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task Test_SpecialCharacters_Escaped()
	{
		// Create attributes with special regex characters in names
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/TEST.DOT,value1)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/TEST_UNDERSCORE,value2)]"));
		await Parser.FunctionParse(MModule.single("[attrib_set(%!/TEST-DASH,value3)]"));

		// The * wildcard should match these literally, not as regex
		var result = (await Parser.FunctionParse(MModule.single("[lattr(%!/TEST*)]")))?.Message!;
		var attrs = result.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToArray();

		await Assert.That(attrs).Contains("TEST.DOT");
		await Assert.That(attrs).Contains("TEST_UNDERSCORE");
		await Assert.That(attrs).Contains("TEST-DASH");
	}
}
