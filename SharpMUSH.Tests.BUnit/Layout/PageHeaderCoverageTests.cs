using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Layout;

/// <summary>
/// Every routed page leads with the kit's page header: <c>PageHeader</c>, or <c>GlassBanner</c> when its
/// subject has an image (<c>GameHero</c> is the home page's banner). A page may reach it through a component
/// it renders. The pages listed below are not pages with a header, and say why.
/// </summary>
public partial class PageHeaderCoverageTests
{
	private static readonly HashSet<string> Headers = ["PageHeader", "GlassBanner", "GameHero"];

	private static readonly Dictionary<string, string> Exempt = new()
	{
		["Pages/Register.razor"] = "redirects to /login",
		["Pages/SettingsCharactersRedirect.razor"] = "redirects to /account",
		["Pages/Login.razor"] = "a centred sign-in card under the game's brand",
		["Pages/ExternalLoginComplete.razor"] = "a centred sign-in card under the game's brand",
		["Pages/Setup.razor"] = "a centred setup card under the game's brand",
		["Pages/OAuthAuthorize.razor"] = "a centred consent card",
		["Pages/NotFound.razor"] = "the centred panel shared with Not Authorized",
		["Pages/SceneLive.razor"] = "a full-height workspace with its own scene bar",
		["Pages/SoftcodeEditor.razor"] = "a full-height workspace with its own panes",
		["Pages/AppBuilder.razor"] = "a full-height workspace with its own step bar",
		["Pages/WikiIndex.razor"] = "its header is the wiki index widget's banner, placed by the page's layout",
	};

	[GeneratedRegex(@"^@page\s", RegexOptions.Multiline)]
	private static partial Regex PageDirective();

	[GeneratedRegex(@"<([A-Z][A-Za-z0-9]*)\b")]
	private static partial Regex ComponentTag();

	private static Dictionary<string, string> Components() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories)
			.GroupBy(Path.GetFileNameWithoutExtension)
			.ToDictionary(g => g.Key!, g => string.Join('\n', g.Select(File.ReadAllText)));

	private static List<string> RoutedPages() =>
		Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories)
			.Where(f => PageDirective().IsMatch(File.ReadAllText(f)))
			.Select(f => Path.GetRelativePath(ClientSource.RazorRoot, f).Replace('\\', '/'))
			.ToList();

	private static bool LeadsWithHeader(string source, Dictionary<string, string> components)
	{
		var seen = new HashSet<string>();
		var pending = new Queue<string>([source]);
		while (pending.TryDequeue(out var text))
		{
			foreach (var name in ComponentTag().Matches(text).Select(m => m.Groups[1].Value))
			{
				if (Headers.Contains(name))
				{
					return true;
				}

				if (seen.Add(name) && components.TryGetValue(name, out var component))
				{
					pending.Enqueue(component);
				}
			}
		}

		return false;
	}

	[Test]
	public async Task EveryRoutedPage_LeadsWithThePageHeader()
	{
		var components = Components();
		var pages = RoutedPages();
		var missing = pages
			.Where(p => !Exempt.ContainsKey(p))
			.Where(p => !LeadsWithHeader(File.ReadAllText(Path.Join(ClientSource.RazorRoot, p)), components))
			.ToList();

		await Assert.That(pages.Count).IsGreaterThan(80).Because("the client's pages are copied beside the tests");
		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task EveryExemption_IsARoutedPageWithoutAHeader()
	{
		var components = Components();
		var pages = RoutedPages();
		var stale = Exempt.Keys
			.Where(p => !pages.Contains(p) || LeadsWithHeader(File.ReadAllText(Path.Join(ClientSource.RazorRoot, p)), components))
			.ToList();

		await Assert.That(stale).IsEmpty().Because("a page that gained a header, or is gone, leaves the list");
	}
}
