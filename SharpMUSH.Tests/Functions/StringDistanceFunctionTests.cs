using MarkupString;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.ExpandedObjectData;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class StringDistanceFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private IMUSHCodeParser Parser => Factory.FunctionParser;

	[Test]
	[Arguments("strdistance(kitten,sitting)", "3")]
	[Arguments("strdistance(,)", "0")]
	[Arguments("strdistance(,😀)", "1")]
	[Arguments("strdistance(ansi(r,界😀),ansi(b,界😀))", "0")]
	[Arguments("strdistance(A,a)", "1")]
	[Arguments("strdistance(é,é)", "1")]
	public async Task PublicContract(string expression, string expected)
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain(expression)))!.Message!;
		await Assert.That(result.Text).IsEqualTo(expected);
	}

	[Test]
	public async Task ExcessiveWorkReturnsTheDocumentedError()
	{
		var result = (await Parser.FunctionParse(MarkupText.Plain("strdistance(repeat(a,2001),repeat(b,2000))")))!.Message!;
		await Assert.That(result.Text).IsEqualTo(StringDistance.WorkLimitExceeded);
	}

	[Test, NotInParallel]
	public async Task SuggestRetainsItsCaseFoldingAndCodeUnitRanking()
	{
		var data = Factory.Services.GetRequiredService<IExpandedObjectDataService>();
		var original = await data.GetExpandedServerDataAsync<SuggestionData>();
		try
		{
			await data.SetExpandedServerDataAsync(new SuggestionData(new Dictionary<string, HashSet<string>>
			{
				["distancecase"] = ["A", "b"],
				["distanceunits"] = ["ab", "é"]
			}));
			var folded = (await Parser.FunctionParse(MarkupText.Plain("suggest(distancecase,a,|)")))!.Message!;
			await Assert.That(folded.Text).IsEqualTo("A|b");
			var units = (await Parser.FunctionParse(MarkupText.Plain("suggest(distanceunits,é,|)")))!.Message!;
			await Assert.That(units.Text).IsEqualTo("ab|é");
		}
		finally { await data.SetExpandedServerDataAsync(original ?? new SuggestionData()); }
	}

	[Test]
	public async Task HelpTopicIsIndexed()
	{
		var help = await Factory.Services.GetRequiredService<ITextFileService>().GetEntryAsync("help/sharpfunc", "STRDISTANCE()");
		await Assert.That(help).IsNotNull();
		await Assert.That(help!).Contains("strdistance(");
	}
}
