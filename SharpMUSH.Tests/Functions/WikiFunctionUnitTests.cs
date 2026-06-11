using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Integration tests for the wiki() softcode functions, run against the seeded
/// wiki pages ("home" in main, "markdown_guide" in help) plus pages created here.
/// </summary>
public class WikiFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IWikiService WikiService => WebAppFactoryArg.Services.GetRequiredService<IWikiService>();

	private async Task<string> Eval(string expression) =>
		(await Parser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText();

	[Test]
	public async Task Wiki_Title_ReturnsSeededHomeTitle()
	{
		await Assert.That(await Eval("wiki(home, title)")).IsEqualTo("Home");
	}

	[Test]
	public async Task Wiki_DefaultField_ReturnsPlainText()
	{
		var result = await Eval("wiki(home)");

		await Assert.That(result).Contains("Getting started");
		await Assert.That(result).DoesNotContain("##"); // markdown stripped
	}

	[Test]
	public async Task Wiki_NamespacePrefix_ResolvesHelpPage()
	{
		await Assert.That(await Eval("wiki(help:markdown_guide, title)")).IsEqualTo("Markdown Guide");
		await Assert.That(await Eval("wiki(help:Markdown Guide, namespace)")).IsEqualTo("help");
	}

	[Test]
	public async Task Wiki_UnknownPage_ReturnsError()
	{
		await Assert.That(await Eval("wiki(definitely_not_a_page)"))
			.IsEqualTo("#-1 NO SUCH WIKI PAGE");
	}

	[Test]
	public async Task Wiki_UnknownField_ReturnsError()
	{
		await Assert.That(await Eval("wiki(home, nonsense)"))
			.IsEqualTo("#-1 UNKNOWN WIKI FIELD");
	}

	[Test]
	public async Task Wiki_RevisionField_IsNumeric()
	{
		var revision = await Eval("wiki(home, revision)");

		await Assert.That(int.TryParse(revision, out var value)).IsTrue();
		await Assert.That(value).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	public async Task WikiList_HelpNamespace_ContainsGuideReference()
	{
		var result = await Eval("wikilist(help)");

		await Assert.That(result).Contains("help:general:markdown_guide");
	}

	[Test]
	public async Task WikiList_UnknownNamespace_ReturnsError()
	{
		await Assert.That(await Eval("wikilist(bogus)"))
			.IsEqualTo("#-1 NO SUCH WIKI NAMESPACE");
	}

	[Test]
	public async Task WikiSearch_FindsPageByBody()
	{
		var created = await WikiService.CreateAsync(
			"Function Search Target", "Contains the plugh-marker token.", "#1");
		await Assert.That(created.IsT0).IsTrue();

		var result = await Eval("wikisearch(plugh-marker)");

		await Assert.That(result).Contains("function_search_target");
	}

	[Test]
	public async Task WikiRecent_ReturnsReferences_AndValidatesCount()
	{
		var recent = await Eval("wikirecent()");
		await Assert.That(recent.Length).IsGreaterThan(0);

		var error = await Eval("wikirecent(9999)");
		await Assert.That(error).StartsWith("#-1");
	}
}
