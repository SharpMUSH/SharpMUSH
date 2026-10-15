using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.Database;

public class ClearAndWipeAttributeTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task ClearAttributeAsync_LeafAttribute_RemovesAttribute()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"CLEAR_LEAF_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [attributeName], MarkupText.Plain("TestValue"), playerOne);

		var beforeClear = Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		await Assert.That(beforeClear).IsNotNull();

		// Clear should remove it since it has no children
		var result = await Database.ClearAttributeAsync(playerOneDBRef, [attributeName]);

		await Assert.That(result).IsTrue();
		var afterClear = Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		var afterClearList = await afterClear.ToListAsync();
		await Assert.That(afterClearList).IsEmpty();
	}

	[Test]
	public async Task ClearAttributeAsync_AttributeWithChildren_ClearsValueKeepsStructure()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"CLEAR_PARENT_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [baseName], MarkupText.Plain("ParentValue"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"], MarkupText.Plain("ChildValue1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"], MarkupText.Plain("ChildValue2"), playerOne);

		var beforeClear = Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var beforeList = await beforeClear!.ToListAsync()!;
		await Assert.That(beforeList).Count().IsEqualTo(1);
		await Assert.That(beforeList.Last().Value.ToString()).IsEqualTo("ParentValue");

		// Clear the parent should clear value but keep structure
		var result = await Database.ClearAttributeAsync(playerOneDBRef, [baseName]);

		await Assert.That(result).IsTrue();
		var afterClear = Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var afterList = await afterClear!.ToListAsync()!;
		await Assert.That(afterList).Count().IsEqualTo(1);
		await Assert.That(afterList.Last().Value.ToString()).IsEqualTo(string.Empty);

		var child1 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"]);
		var child1List = await child1!.ToListAsync()!;
		await Assert.That(child1List).Count().IsEqualTo(2);
		await Assert.That(child1List.Last().Value.ToString()).IsEqualTo("ChildValue1");

		var child2 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"]);
		var child2List = await child2!.ToListAsync()!;
		await Assert.That(child2List).Count().IsEqualTo(2);
		await Assert.That(child2List.Last().Value.ToString()).IsEqualTo("ChildValue2");
	}

	[Test]
	public async Task ClearAttributeAsync_NonExistentAttribute_ReturnsFalse()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"NONEXISTENT_CLEAR_{Guid.NewGuid():N}";

		var result = await Database.ClearAttributeAsync(playerOneDBRef, [attributeName]);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task WipeAttributeAsync_LeafAttribute_RemovesAttribute()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"WIPE_LEAF_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [attributeName], MarkupText.Plain("TestValue"), playerOne);

		var beforeWipe = Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		await Assert.That(beforeWipe).IsNotNull();

		var result = await Database.WipeAttributeAsync(playerOneDBRef, [attributeName]);

		await Assert.That(result).IsTrue();
		var afterWipe = Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		var afterWipeList = await afterWipe.ToListAsync();
		await Assert.That(afterWipeList).IsEmpty();
	}

	[Test]
	public async Task WipeAttributeAsync_AttributeTree_RemovesAllDescendants()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"WIPE_TREE_TEST_{Guid.NewGuid():N}";

		// Build a tree structure:
		// baseName -> "Root"
		//   |- CHILD1 -> "Child1"
		//   |    |- GRANDCHILD1 -> "GrandChild1"
		//   |    |- GRANDCHILD2 -> "GrandChild2"
		//   |- CHILD2 -> "Child2"
		await Database.SetAttributeAsync(playerOneDBRef, [baseName], MarkupText.Plain("Root"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"], MarkupText.Plain("Child1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD1"], MarkupText.Plain("GrandChild1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD2"], MarkupText.Plain("GrandChild2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"], MarkupText.Plain("Child2"), playerOne);

		var root = Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		await Assert.That(root).IsNotNull();
		var child1 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"]);
		await Assert.That(child1).IsNotNull();
		var grandchild1 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD1"]);
		await Assert.That(grandchild1).IsNotNull();

		// Wipe the root should remove everything
		var result = await Database.WipeAttributeAsync(playerOneDBRef, [baseName]);

		await Assert.That(result).IsTrue();
		var afterRoot = Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var afterRootList = await afterRoot.ToListAsync();
		await Assert.That(afterRootList).IsEmpty();

		var afterChild1 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"]);
		var afterChild1List = await afterChild1.ToListAsync();
		await Assert.That(afterChild1List).IsEmpty();

		var afterGrandchild1 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD1"]);
		var afterGrandchild1List = await afterGrandchild1.ToListAsync();
		await Assert.That(afterGrandchild1List).IsEmpty();

		var afterChild2 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"]);
		var afterChild2List = await afterChild2.ToListAsync();
		await Assert.That(afterChild2List).IsEmpty();
	}

	[Test]
	public async Task WipeAttributeAsync_MiddleOfTree_RemovesOnlySubtree()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"WIPE_SUBTREE_TEST_{Guid.NewGuid():N}";

		// Build a tree: ROOT -> BRANCH1 -> LEAF1, ROOT -> BRANCH2 -> LEAF2
		await Database.SetAttributeAsync(playerOneDBRef, [baseName], MarkupText.Plain("Root"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1"], MarkupText.Plain("Branch1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1", "LEAF1"], MarkupText.Plain("Leaf1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2"], MarkupText.Plain("Branch2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2", "LEAF2"], MarkupText.Plain("Leaf2"), playerOne);

		var result = await Database.WipeAttributeAsync(playerOneDBRef, [baseName, "BRANCH1"]);

		await Assert.That(result).IsTrue();

		var rootAfter = Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		await Assert.That(rootAfter).IsNotNull();

		var branch1After = Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1"]);
		var branch1AfterList = await branch1After.ToListAsync();
		await Assert.That(branch1AfterList).IsEmpty();

		var leaf1After = Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1", "LEAF1"]);
		var leaf1AfterList = await leaf1After.ToListAsync();
		await Assert.That(leaf1AfterList).IsEmpty();

		var branch2After = Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2"]);
		await Assert.That(branch2After).IsNotNull();
		var branch2List = await branch2After!.ToListAsync();
		await Assert.That(branch2List.Last().Value.ToString()).IsEqualTo("Branch2");

		var leaf2After = Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2", "LEAF2"]);
		await Assert.That(leaf2After).IsNotNull();
		var leaf2List = await leaf2After!.ToListAsync();
		await Assert.That(leaf2List.Last().Value.ToString()).IsEqualTo("Leaf2");
	}

	[Test]
	public async Task WipeAttributeAsync_NonExistentAttribute_ReturnsFalse()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"NONEXISTENT_WIPE_{Guid.NewGuid():N}";

		var result = await Database.WipeAttributeAsync(playerOneDBRef, [attributeName]);

		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task WipeAttributeAsync_DeepTree_RemovesAllLevels()
	{
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"WIPE_DEEP_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [baseName], MarkupText.Plain("L1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2"], MarkupText.Plain("L2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3"], MarkupText.Plain("L3"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4"], MarkupText.Plain("L4"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4", "L5"], MarkupText.Plain("L5"), playerOne);

		var deepest = Database.GetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4", "L5"]);
		await Assert.That(deepest).IsNotNull();

		var result = await Database.WipeAttributeAsync(playerOneDBRef, [baseName]);

		await Assert.That(result).IsTrue();
		var afterL1 = Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var afterL1List = await afterL1.ToListAsync();
		await Assert.That(afterL1List).IsEmpty();

		var afterL5 = Database.GetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4", "L5"]);
		var afterL5List = await afterL5.ToListAsync();
		await Assert.That(afterL5List).IsEmpty();
	}

	[Test]
	public async Task ClearAndWipe_DifferentAttributes_NoConflict()
	{
		// Two separate attribute trees to ensure they don't interfere
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).Expect<SharpPlayer>();
		var playerOneDBRef = playerOne.Object.DBRef;
		var clearAttr = $"CONFLICT_CLEAR_{Guid.NewGuid():N}";
		var wipeAttr = $"CONFLICT_WIPE_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [clearAttr], MarkupText.Plain("ClearValue"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [clearAttr, "CHILD"], MarkupText.Plain("ClearChild"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [wipeAttr], MarkupText.Plain("WipeValue"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [wipeAttr, "CHILD"], MarkupText.Plain("WipeChild"), playerOne);

		var clearResult = await Database.ClearAttributeAsync(playerOneDBRef, [clearAttr]);
		var wipeResult = await Database.WipeAttributeAsync(playerOneDBRef, [wipeAttr]);

		await Assert.That(clearResult).IsTrue();
		await Assert.That(wipeResult).IsTrue();

		var clearedAttr = Database.GetAttributeAsync(playerOneDBRef, [clearAttr]);
		await Assert.That(clearedAttr).IsNotNull();
		var clearedList = await clearedAttr!.ToListAsync();
		await Assert.That(clearedList.Last().Value.ToString()).IsEqualTo(string.Empty);

		var wipedAttr = Database.GetAttributeAsync(playerOneDBRef, [wipeAttr]);
		var wipedAttrList = await wipedAttr.ToListAsync();
		await Assert.That(wipedAttrList).IsEmpty();
	}
}
