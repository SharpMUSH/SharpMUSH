using System.Text.RegularExpressions;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// The server hands out its files as site-relative <c>/api/…</c> paths (wiki assets, gallery images).
/// A browser resolves those against the page, so they only load when the portal and the API share an
/// origin and sit at the root. The dev split (portal on :5284, API on :8081) and a portal served
/// under a base path both broke every uploaded image; resolving against the API base fixes both.
/// </summary>
public class ApiUrlTests
{
	private static readonly Uri DevApi = new("https://localhost:8081/");

	[Test]
	[Arguments("/api/wiki-assets/a1/docks.jpg", "https://localhost:8081/", "https://localhost:8081/api/wiki-assets/a1/docks.jpg")]
	[Arguments("  /api/wiki-assets/a1/docks.jpg ", "https://localhost:8081/", "https://localhost:8081/api/wiki-assets/a1/docks.jpg")]
	[Arguments("/api/wiki-assets/a1/docks.jpg", "https://mush.example/portal/", "https://mush.example/portal/api/wiki-assets/a1/docks.jpg")]
	[Arguments("/assets/docs/mock.svg", "https://localhost:8081/", "/assets/docs/mock.svg")]
	[Arguments("https://cdn.example/x.jpg", "https://localhost:8081/", "https://cdn.example/x.jpg")]
	[Arguments("/apish/x.jpg", "https://localhost:8081/", "/apish/x.jpg")]
	public async Task Resolve_PointsServerPathsAtTheApi_AndLeavesTheRestAlone(string url, string apiBase, string expected)
		=> await Assert.That(ApiUrl.Resolve(url, new Uri(apiBase))).IsEqualTo(expected);

	[Test]
	public async Task Resolve_WithoutAnApiBase_OnlyTrims()
		=> await Assert.That(ApiUrl.Resolve(" /api/wiki-assets/a1/docks.jpg ", null)).IsEqualTo("/api/wiki-assets/a1/docks.jpg");

	[Test]
	public async Task ResolveHtml_RewritesServerPathsInSrcAndHref_Only()
	{
		const string html = """<p><img src="/api/wiki-assets/a1/docks.jpg" alt=""> <a href="/api/wiki-assets/b2/map.pdf">map</a> """
			+ """<a href="/wiki/Main/Home">home</a> <img src="/assets/docs/mock.svg"></p>""";

		await Assert.That(ApiUrl.ResolveHtml(html, DevApi)).IsEqualTo(
			"""<p><img src="https://localhost:8081/api/wiki-assets/a1/docks.jpg" alt=""> <a href="https://localhost:8081/api/wiki-assets/b2/map.pdf">map</a> """
			+ """<a href="/wiki/Main/Home">home</a> <img src="/assets/docs/mock.svg"></p>""");
	}

	[Test]
	public async Task ResolveHtml_WithoutAnApiBase_IsUnchanged()
		=> await Assert.That(ApiUrl.ResolveHtml("""<img src="/api/x.jpg">""", null)).IsEqualTo("""<img src="/api/x.jpg">""");

	/// <summary>
	/// Every image the portal binds goes through <see cref="ApiUrl.Resolve(string)"/>; one that binds
	/// a raw URL is an uploaded image that breaks again the moment the API is on another origin.
	/// </summary>
	[Test]
	public async Task EveryBoundImageSource_IsResolvedAgainstTheApi()
	{
		var raw = new List<string>();
		foreach (var file in Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories))
		{
			foreach (Match m in Regex.Matches(File.ReadAllText(file), """<img\b[^>]*?\ssrc="(?<v>@[^"]*)"""))
			{
				if (!m.Groups["v"].Value.Contains("ApiUrl.Resolve("))
				{
					raw.Add($"{Path.GetRelativePath(ClientSource.RazorRoot, file)}: src=\"{m.Groups["v"].Value}\"");
				}
			}
		}

		await Assert.That(raw).IsEmpty().Because(string.Join("\n", raw));
	}
}
