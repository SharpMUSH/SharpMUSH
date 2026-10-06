using SharpMUSH.Library.Markup;

namespace SharpMUSH.Tests.Markup;

public class ImageHostPolicyTests
{
	[Test]
	[Arguments("https://example.com/a.png", "any", "", true)]
	[Arguments("http://example.com/a.png", "", "", true)]
	[Arguments("/images/a.png", "any", "", true)]
	[Arguments("images/a.png", "allow", "", true)]
	[Arguments("/images/a.png", "off", "", false)]
	[Arguments("https://example.com/a.png", "off", "", false)]
	[Arguments("//example.com/a.png", "any", "", false)]
	[Arguments("file:///etc/passwd", "any", "", false)]
	[Arguments("javascript:alert(1)", "any", "", false)]
	[Arguments("", "any", "", false)]
	[Arguments("https://example.com/a.png", "allow", "example.com", true)]
	[Arguments("https://evil.test/a.png", "allow", "example.com", false)]
	[Arguments("https://cdn.example.com/a.png", "allow", "*.example.com", true)]
	[Arguments("https://example.com/a.png", "allow", "*.example.com", false)]
	[Arguments("https://notexample.com/a.png", "allow", "*.example.com", false)]
	[Arguments("https://EXAMPLE.com/a.png", "allow", "imgur.com, example.com", true)]
	[Arguments("https://evil.test/a.png", "block", "evil.test", false)]
	[Arguments("https://example.com/a.png", "block", "evil.test", true)]
	public async Task Allows(string source, string mode, string hosts, bool allowed)
		=> await Assert.That(ImageHostPolicy.Allows(source, mode, hosts)).IsEqualTo(allowed);
}
