using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

/// <summary>Putting an unpinned category back in its place on the wiki home.</summary>
public class WikiPinOrderTests
{
	[Test]
	[Arguments("a,c", "a,b,c", "b", "a,b,c")]
	[Arguments("b,c", "a,b,c", "a", "a,b,c")]
	[Arguments("a,b", "a,b,c", "c", "a,b,c")]
	[Arguments("c,a", "a,b,c", "b", "c,a,b")]
	[Arguments("c", "a,b,c", "b", "b,c")]
	[Arguments("a,c,b", "a,c", "b", "a,c,b")]
	public async Task RestorePutsTheCategoryAfterItsNearestEarlierPin(string pinned, string before, string category, string expected)
		=> await Assert.That(string.Join(',', WikiPinOrder.Restore(pinned.Split(','), before.Split(','), category))).IsEqualTo(expected);
}
