using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// What a first-time visitor is made to download before they have done anything.
///
/// <para>Every heavy editor asset was a plain <c>&lt;script&gt;</c> in <c>index.html</c>, so an
/// anonymous visitor reading the home page fetched Monaco (3.67 MB) and Mermaid (2.57 MB) whether or
/// not they would ever open the softcode editor or a wiki page with a diagram in it. Compression cut
/// what those cost on the wire; it cannot stop them being asked for. These are loaded on demand now,
/// by the one page that needs each.</para>
/// </summary>
public class EagerAssetTests
{
	private static string IndexHtml() =>
		File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "index.html"));

	/// <summary>Script/link tags in index.html — everything a visitor fetches before Blazor starts.</summary>
	private static IEnumerable<string> EagerReferences()
	{
		var html = IndexHtml();
		foreach (Match m in Regex.Matches(html, "(?:src|href)\\s*=\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
		{
			yield return m.Groups[1].Value;
		}
	}

	[Test]
	[Arguments("monaco", "the softcode editor is one page; every other visitor pays 3.67 MB for it")]
	[Arguments("mermaid", "diagrams appear on some wiki pages; the home page is not one of them")]
	[Arguments("easymde", "the markdown editor component is not used anywhere in the client")]
	public async Task IndexHtml_DoesNotEagerlyLoad(string asset, string because)
	{
		var offenders = EagerReferences()
			.Where(r => r.Contains(asset, StringComparison.OrdinalIgnoreCase))
			.ToList();

		await Assert.That(offenders).IsEmpty().Because(because);
	}

	/// <summary>
	/// The loader itself must stay eager — it is what everything else is fetched through, and it is a
	/// couple of hundred bytes.
	/// </summary>
	[Test]
	public async Task IndexHtml_StillLoadsTheAssetLoader()
	{
		await Assert.That(EagerReferences().Any(r => r.Contains("lazy-assets.js", StringComparison.Ordinal)))
			.IsTrue();
	}

	/// <summary>
	/// A stylesheet that imports from a package the client no longer references makes every visitor pay
	/// for a 404 on the critical path. Dropping the unused MarkdownEditor package left two such imports
	/// in custom.css behind, and nothing in the build complains: an <c>@import</c> naming a missing
	/// <c>_content</c> path is valid CSS.
	/// </summary>
	[Test]
	public async Task StylesheetImports_OnlyNameReferencedPackages()
	{
		var css = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "css", "custom.css"));
		var csproj = File.ReadAllText(Path.Join(AppContext.BaseDirectory, "client", "SharpMUSH.Client.csproj"));

		var dangling = Regex.Matches(css, @"_content/([^/""]+)/")
			.Select(m => m.Groups[1].Value)
			.Distinct()
			.Where(pkg => !csproj.Contains($"Include=\"{pkg}\"", StringComparison.OrdinalIgnoreCase))
			.ToList();

		await Assert.That(dangling).IsEmpty()
			.Because("every _content import must come from a package the client actually references");
	}
}
