using SharpMUSH.Documentation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Documentation;

/// <summary>The compatibility profile follows declared identities across its source articles.</summary>
internal static class CompatibilityProfileSources
{
	internal sealed record Source(string FileName, string Markdown, HelpArticle Article);

	private static readonly Lazy<IReadOnlyList<Source>> Sources = new(() =>
		Directory.GetFiles(TestPaths.Helpfiles.FullName, "*.md").OrderBy(path => path, StringComparer.Ordinal)
			.SelectMany(path =>
			{
				var markdown = File.ReadAllText(path);
				return HelpArticleParser.Parse(markdown, "help")
					.Where(parsed => parsed.Declared && parsed.Article.Id.StartsWith("compatibility-", StringComparison.Ordinal))
					.Select(parsed => new Source(Path.GetFileName(path), markdown, parsed.Article));
			}).ToList());

	internal static IReadOnlyList<Source> All => Sources.Value;
}
