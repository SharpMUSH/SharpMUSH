using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class DbrefFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task Loc()
	{
		// Test loc function - should return the location of the current player
		var result = (await Parser.FunctionParse(MModule.single("loc(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	public async Task Controls()
	{
		// Test controls function - a player should control themselves
		var result = (await Parser.FunctionParse(MModule.single("controls(%#,%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("1");
	}

	[Test]
	public async Task Home()
	{
		// Test home function - should return the home of the current player
		var result = (await Parser.FunctionParse(MModule.single("home(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).StartsWith("#0:");
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("entrances(#0)", "")]
	public async Task Entrances(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("followers(%#)", "")]
	public async Task Followers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("following(%#)", "")]
	public async Task Following(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("locate(%#,test,*)", "")]
	public async Task Locate(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lock(%#)", "")]
	public async Task Lock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("elock(%#)", "")]
	public async Task Elock(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("rloc(#1,0)", "#0")]
	public async Task Rloc(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("slev()", "")]
	public async Task Slev(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("stext()", "")]
	public async Task Stext(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
