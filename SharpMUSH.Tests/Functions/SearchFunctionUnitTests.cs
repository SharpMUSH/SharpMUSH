using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SharpMUSH.Tests.Functions;

public class SearchFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task Lsearch_TypeFilter_ReturnsMatchingObjects()
	{
		// Test lsearch with type filter
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type=PLAYER)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least player #1 (God)
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#1");
	}

	[Test]
	public async Task Lsearch_NameFilter_ReturnsMatchingObjects()
	{
		// Create a test object with unique name
		var uniqueName = $"LSearchTest_{Guid.NewGuid():N}";
		await WebAppFactoryArg.CommandParser.CommandParse(1, ConnectionService, MModule.single($"@create {uniqueName}"));
		
		// Test lsearch with name filter
		var result = (await Parser.FunctionParse(MModule.single($"lsearch(all,name={uniqueName})")))?.Message!;
		var resultText = result.ToPlainText();
		
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).IsNotEmpty();
	}

	[Test]
	public async Task Lsearch_CombinedFilters_ReturnsMatchingObjects()
	{
		// Test lsearch with multiple filters
		var result = (await Parser.FunctionParse(MModule.single("lsearch(all,type=ROOM,mindbref=0)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return at least room #0
		await Assert.That(resultText).IsNotNull();
		await Assert.That(resultText).Contains("#0");
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lsearchr(#0,name,test*)", "")]
	public async Task Lsearchr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	public async Task Nlsearch_ReturnsCount()
	{
		// Test nlsearch returns a count
		var result = (await Parser.FunctionParse(MModule.single("nlsearch(all,type=PLAYER)")))?.Message!;
		var resultText = result.ToPlainText();
		
		// Should return a number >= 1 (at least God exists)
		await Assert.That(resultText).IsNotNull();
		await Assert.That(int.TryParse(resultText, out var count)).IsTrue();
		await Assert.That(count).IsGreaterThanOrEqualTo(1);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("scan(%#)", "")]
	public async Task Scan(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nearby(%#,%#)", "1")]
	public async Task Nearby(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}
}
