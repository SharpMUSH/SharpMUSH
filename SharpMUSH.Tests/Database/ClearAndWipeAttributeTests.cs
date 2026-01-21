using Bogus.Extensions;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using A = MarkupString.MarkupStringModule;

namespace SharpMUSH.Tests.Database;

public class ClearAndWipeAttributeTests : TestsBase
{
	private ISharpDatabase Database => Services.GetRequiredService<ISharpDatabase>();

	[Test]
	public async Task ClearAttributeAsync_LeafAttribute_RemovesAttribute()
	{
		// Arrange: Create a unique leaf attribute
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"CLEAR_LEAF_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [attributeName], A.single("TestValue"), playerOne);

		// Verify it exists
		var beforeClear = await Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		await Assert.That(beforeClear).IsNotNull();

		// Act: Clear the attribute (should remove it since it has no children)
		var result = await Database.ClearAttributeAsync(playerOneDBRef, [attributeName]);

		// Assert: Attribute should be removed
		await Assert.That(result).IsTrue();
		var afterClear = await Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		var afterClearList = afterClear == null ? null : await afterClear.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterClearList).IsNull();
	}

	[Test]
	public async Task ClearAttributeAsync_AttributeWithChildren_ClearsValueKeepsStructure()
	{
		// Arrange: Create a unique attribute tree
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"CLEAR_PARENT_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [baseName], A.single("ParentValue"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"], A.single("ChildValue1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"], A.single("ChildValue2"), playerOne);

		// Verify parent and children exist
		var beforeClear = await Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var beforeList = await beforeClear!.ToListAsync()!;
		await Assert.That(beforeList).Count().IsEqualTo(1);
		await Assert.That(beforeList.Last().Value.ToString()).IsEqualTo("ParentValue");

		// Act: Clear the parent attribute (should clear value but keep structure)
		var result = await Database.ClearAttributeAsync(playerOneDBRef, [baseName]);

		// Assert: Parent should exist but have empty value, children should still exist
		await Assert.That(result).IsTrue();
		var afterClear = await Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var afterList = await afterClear!.ToListAsync()!;
		await Assert.That(afterList).Count().IsEqualTo(1);
		await Assert.That(afterList.Last().Value.ToString()).IsEqualTo(string.Empty);

		// Verify children still exist
		var child1 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"]);
		var child1List = await child1!.ToListAsync()!;
		await Assert.That(child1List).Count().IsEqualTo(2);
		await Assert.That(child1List.Last().Value.ToString()).IsEqualTo("ChildValue1");

		var child2 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"]);
		var child2List = await child2!.ToListAsync()!;
		await Assert.That(child2List).Count().IsEqualTo(2);
		await Assert.That(child2List.Last().Value.ToString()).IsEqualTo("ChildValue2");
	}

	[Test]
	public async Task ClearAttributeAsync_NonExistentAttribute_ReturnsFalse()
	{
		// Arrange
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"NONEXISTENT_CLEAR_{Guid.NewGuid():N}";

		// Act
		var result = await Database.ClearAttributeAsync(playerOneDBRef, [attributeName]);

		// Assert
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task WipeAttributeAsync_LeafAttribute_RemovesAttribute()
	{
		// Arrange: Create a unique leaf attribute
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"WIPE_LEAF_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [attributeName], A.single("TestValue"), playerOne);

		// Verify it exists
		var beforeWipe = await Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		await Assert.That(beforeWipe).IsNotNull();

		// Act: Wipe the attribute
		var result = await Database.WipeAttributeAsync(playerOneDBRef, [attributeName]);

		// Assert: Attribute should be removed
		await Assert.That(result).IsTrue();
		var afterWipe = await Database.GetAttributeAsync(playerOneDBRef, [attributeName]);
		var afterWipeList = afterWipe == null ? null : await afterWipe.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterWipeList).IsNull();
	}

	[Test]
	public async Task WipeAttributeAsync_AttributeTree_RemovesAllDescendants()
	{
		// Arrange: Create a unique attribute tree with multiple levels
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"WIPE_TREE_TEST_{Guid.NewGuid():N}";

		// Build a tree structure:
		// baseName -> "Root"
		//   |- CHILD1 -> "Child1"
		//   |    |- GRANDCHILD1 -> "GrandChild1"
		//   |    |- GRANDCHILD2 -> "GrandChild2"
		//   |- CHILD2 -> "Child2"
		await Database.SetAttributeAsync(playerOneDBRef, [baseName], A.single("Root"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"], A.single("Child1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD1"], A.single("GrandChild1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD2"], A.single("GrandChild2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"], A.single("Child2"), playerOne);

		// Verify tree exists
		var root = await Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		await Assert.That(root).IsNotNull();
		var child1 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"]);
		await Assert.That(child1).IsNotNull();
		var grandchild1 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD1"]);
		await Assert.That(grandchild1).IsNotNull();

		// Act: Wipe the root attribute (should remove everything)
		var result = await Database.WipeAttributeAsync(playerOneDBRef, [baseName]);

		// Assert: Everything should be removed
		await Assert.That(result).IsTrue();
		var afterRoot = await Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var afterRootList = afterRoot == null ? null : await afterRoot.ToListAsync();
		await Assert.That(afterRootList!).IsNull();
		
		var afterChild1 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1"]);
		var afterChild1List = afterChild1 == null ? null : await afterChild1.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterChild1List).IsNull();
		
		var afterGrandchild1 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD1", "GRANDCHILD1"]);
		var afterGrandchild1List = afterGrandchild1 == null ? null : await afterGrandchild1.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterGrandchild1List).IsNull();
		
		var afterChild2 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "CHILD2"]);
		var afterChild2List = afterChild2 == null ? null : await afterChild2.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterChild2List).IsNull();
	}

	[Test]
	public async Task WipeAttributeAsync_MiddleOfTree_RemovesOnlySubtree()
	{
		// Arrange: Create a unique attribute tree
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"WIPE_SUBTREE_TEST_{Guid.NewGuid():N}";

		// Build a tree: ROOT -> BRANCH1 -> LEAF1, ROOT -> BRANCH2 -> LEAF2
		await Database.SetAttributeAsync(playerOneDBRef, [baseName], A.single("Root"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1"], A.single("Branch1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1", "LEAF1"], A.single("Leaf1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2"], A.single("Branch2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2", "LEAF2"], A.single("Leaf2"), playerOne);

		// Act: Wipe only BRANCH1 (not the root)
		var result = await Database.WipeAttributeAsync(playerOneDBRef, [baseName, "BRANCH1"]);

		// Assert: BRANCH1 and its children should be gone, but root and BRANCH2 should remain
		await Assert.That(result).IsTrue();
		
		var rootAfter = await Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		await Assert.That(rootAfter).IsNotNull();
		
		var branch1After = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1"]);
		var branch1AfterList = branch1After == null ? null : await branch1After.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(branch1AfterList).IsNull();
		
		var leaf1After = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH1", "LEAF1"]);
		var leaf1AfterList = leaf1After == null ? null : await leaf1After.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(leaf1AfterList).IsNull();
		
		var branch2After = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2"]);
		await Assert.That(branch2After).IsNotNull();
		var branch2List = await branch2After!.ToListAsync();
		await Assert.That(branch2List.Last().Value.ToString()).IsEqualTo("Branch2");
		
		var leaf2After = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "BRANCH2", "LEAF2"]);
		await Assert.That(leaf2After).IsNotNull();
		var leaf2List = await leaf2After!.ToListAsync();
		await Assert.That(leaf2List.Last().Value.ToString()).IsEqualTo("Leaf2");
	}

	[Test]
	public async Task WipeAttributeAsync_NonExistentAttribute_ReturnsFalse()
	{
		// Arrange
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var attributeName = $"NONEXISTENT_WIPE_{Guid.NewGuid():N}";

		// Act
		var result = await Database.WipeAttributeAsync(playerOneDBRef, [attributeName]);

		// Assert
		await Assert.That(result).IsFalse();
	}

	[Test]
	public async Task WipeAttributeAsync_DeepTree_RemovesAllLevels()
	{
		// Arrange: Create a deep attribute tree (5 levels)
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var baseName = $"WIPE_DEEP_TEST_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [baseName], A.single("L1"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2"], A.single("L2"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3"], A.single("L3"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4"], A.single("L4"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4", "L5"], A.single("L5"), playerOne);

		// Verify deepest level exists
		var deepest = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4", "L5"]);
		await Assert.That(deepest).IsNotNull();

		// Act: Wipe from root
		var result = await Database.WipeAttributeAsync(playerOneDBRef, [baseName]);

		// Assert: All levels should be removed
		await Assert.That(result).IsTrue();
		var afterL1 = await Database.GetAttributeAsync(playerOneDBRef, [baseName]);
		var afterL1List = afterL1 == null ? null : await afterL1.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterL1List).IsNull();
		
		var afterL5 = await Database.GetAttributeAsync(playerOneDBRef, [baseName, "L2", "L3", "L4", "L5"]);
		var afterL5List = afterL5 == null ? null : await afterL5.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(afterL5List).IsNull();
	}

	[Test]
	public async Task ClearAndWipe_DifferentAttributes_NoConflict()
	{
		// Arrange: Create two separate attribute trees to ensure they don't interfere
		var playerOne = (await Database.GetObjectNodeAsync(new DBRef(1))).AsPlayer;
		var playerOneDBRef = playerOne.Object.DBRef;
		var clearAttr = $"CONFLICT_CLEAR_{Guid.NewGuid():N}";
		var wipeAttr = $"CONFLICT_WIPE_{Guid.NewGuid():N}";

		await Database.SetAttributeAsync(playerOneDBRef, [clearAttr], A.single("ClearValue"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [clearAttr, "CHILD"], A.single("ClearChild"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [wipeAttr], A.single("WipeValue"), playerOne);
		await Database.SetAttributeAsync(playerOneDBRef, [wipeAttr, "CHILD"], A.single("WipeChild"), playerOne);

		// Act: Clear one, wipe the other
		var clearResult = await Database.ClearAttributeAsync(playerOneDBRef, [clearAttr]);
		var wipeResult = await Database.WipeAttributeAsync(playerOneDBRef, [wipeAttr]);

		// Assert: Clear attribute should have empty value but exist, wipe attribute should be gone
		await Assert.That(clearResult).IsTrue();
		await Assert.That(wipeResult).IsTrue();

		var clearedAttr = await Database.GetAttributeAsync(playerOneDBRef, [clearAttr]);
		await Assert.That(clearedAttr).IsNotNull();
		var clearedList = await clearedAttr!.ToListAsync();
		await Assert.That(clearedList.Last().Value.ToString()).IsEqualTo(string.Empty);

		var wipedAttr = await Database.GetAttributeAsync(playerOneDBRef, [wipeAttr]);
		var wipedAttrList = wipedAttr == null ? null : await wipedAttr.ToListAsync();
		await Assert.That<List<SharpAttribute>?>(wipedAttrList).IsNull();
	}
}
