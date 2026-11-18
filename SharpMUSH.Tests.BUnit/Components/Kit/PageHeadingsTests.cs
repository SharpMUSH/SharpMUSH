using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

public class PageHeadingsTests
{
	[Test]
	[Arguments("<h1 id=\"getting-started\">Getting Started</h1>\n<p>Body</p>", "getting started", "<span id=\"getting-started\"></span><p>Body</p>")]
	[Arguments("<h1>Getting <em>Started</em></h1><p>Body</p>", "Getting Started", "<p>Body</p>")]
	[Arguments("<h1>Welcome</h1><p>Body</p>", "What's happening", "<h2>Welcome</h2><p>Body</p>")]
	[Arguments("<h1>Welcome</h1><p>Body</p>", null, "<h2>Welcome</h2><p>Body</p>")]
	[Arguments("<p>Intro</p><h1 id=\"a\">Getting Started</h1>", "Getting Started", "<p>Intro</p><h2 id=\"a\">Getting Started</h2>")]
	[Arguments("<h2>Kept</h2>", "Kept", "<h2>Kept</h2>")]
	[Arguments("<h1>A</h1><h2>B</h2><h3>C</h3><h6>F</h6>", null, "<h2>A</h2><h3>B</h3><h4>C</h4><h6>F</h6>")]
	[Arguments("<h1>Title</h1><h2>Section</h2><h3>Sub</h3>", "Title", "<h2>Section</h2><h3>Sub</h3>")]
	[Arguments("<p>Intro</p><!-- <h1> --><h2>Section</h2>", null, "<p>Intro</p><!-- <h1> --><h2>Section</h2>")]
	public async Task OnePageHeading(string html, string? title, string expected)
		=> await Assert.That(PageHeadings.UnderPageTitle(html, title)).IsEqualTo(expected);
}
