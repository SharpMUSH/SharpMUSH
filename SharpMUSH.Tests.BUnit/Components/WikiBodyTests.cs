using SharpMUSH.Client.Models.Wiki;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// <see cref="WikiBody.Split"/>: a live listing inside a container block renders inside it, because every
/// <c>div</c> around it becomes an element rather than being cut in half.
/// </summary>
public class WikiBodyTests
{
	private const string Recent = "<div class=\"wiki-directive\" data-directive=\"recent\" data-arg=\"5\"></div>";

	[Test]
	public async Task NoListing_IsOnePieceOfHtml()
	{
		var nodes = WikiBody.Split("<p>a</p><div class=\"center\"><p>b</p></div>");

		await Assert.That(nodes.Count).IsEqualTo(1);
		await Assert.That(nodes[0] is WikiBodyHtml { Html: "<p>a</p><div class=\"center\"><p>b</p></div>" }).IsTrue();
	}

	[Test]
	public async Task ListingAtTheTop_SitsBetweenHtml()
	{
		var nodes = WikiBody.Split($"<p>a</p>{Recent}<p>b</p>");

		await Assert.That(nodes.Count).IsEqualTo(3);
		await Assert.That(nodes[1] is WikiBodyDirective { Directive: "recent", Arg: "5" }).IsTrue();
	}

	[Test]
	public async Task ListingInsideNestedBlocks_IsInsideTheirElements()
	{
		var nodes = WikiBody.Split(
			$"<div class=\"md-flex md-row\">\n<div class=\"md-item\" style=\"--md-grow:1\">\n{Recent}\n</div>\n<div class=\"md-item\"><p>b</p></div>\n</div>\n<p>after</p>");

		await Assert.That(nodes.Count).IsEqualTo(2);
		var flex = nodes[0] is WikiBodyElement e ? e : throw new InvalidOperationException("expected the flex element");
		await Assert.That(flex.Attributes["class"]).IsEqualTo("md-flex md-row");

		var item = flex.Children.Select(child => child is WikiBodyElement element ? element : null).OfType<WikiBodyElement>().Single();
		await Assert.That(item.Attributes["style"]).IsEqualTo("--md-grow:1");
		await Assert.That(item.Children.Any(child => child is WikiBodyDirective)).IsTrue();

		// The item with no listing stays HTML inside the flex, after the element holding the listing.
		await Assert.That(flex.Children.Any(child => child is WikiBodyHtml { Html: var h } && h.Contains("<p>b</p>"))).IsTrue();
		await Assert.That(nodes[1] is WikiBodyHtml { Html: var tail } && tail.Contains("<p>after</p>")).IsTrue();
	}
}
