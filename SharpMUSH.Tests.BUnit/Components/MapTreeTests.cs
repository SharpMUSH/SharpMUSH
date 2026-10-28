using SharpMUSH.Client.Components.Map;
using SharpMUSH.Library.API;

namespace SharpMUSH.Tests.BUnit.Components;

public class MapTreeTests
{
	private static MapSummaryDto Map(string id, string? parent) => new(id, id, MapKinds.Graph, parent);

	[Test]
	public async Task ARegionSitsInsideTheOneItIsZonedTo()
	{
		var parents = MapTree.Parents([Map("#1", null), Map("#2", "#1"), Map("#3", "#2"), Map("#4", "#9")]);

		await Assert.That(parents["#1"]).IsNull();
		await Assert.That(parents["#2"]).IsEqualTo("#1");
		await Assert.That(parents["#3"]).IsEqualTo("#2");
		await Assert.That(parents["#4"]).IsNull().Because("#9 is not in the list");
	}

	[Test]
	public async Task ALoopOfParentsLeavesItsMapsAtTheTop()
	{
		var parents = MapTree.Parents([Map("#1", "#2"), Map("#2", "#1"), Map("#3", "#1"), Map("#4", "#4")]);

		await Assert.That(parents["#1"]).IsNull();
		await Assert.That(parents["#2"]).IsNull();
		await Assert.That(parents["#3"]).IsEqualTo("#1").Because("#3 is not in the loop, only inside it");
		await Assert.That(parents["#4"]).IsNull();
	}
}
