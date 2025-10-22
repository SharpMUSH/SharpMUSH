using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class InformationFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("type(%#)", "PLAYER")]
	[Arguments("type(%l)", "ROOM")]
	public async Task Type(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	public async Task MudName()
	{
		var result = (await Parser.FunctionParse(MModule.single("mudname()")))?.Message!;
		// Should return a non-empty string
		await Assert.That(result.ToPlainText()).IsEqualTo("PennMUSH Emulation by SharpMUSH");
	}

	[Test, Skip("Not Yet Implemented")]
	public async Task Name()
	{
		var result = (await Parser.FunctionParse(MModule.single("name(%#)")))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo("One");
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("alias(%#)", "")]
	public async Task Alias(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("fullname(%#)", "")]
	public async Task Fullname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotEmpty();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("accname(%#)", "")]
	public async Task Accname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("iname(%#)", "")]
	public async Task Iname(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("moniker(%#)", "")]
	public async Task Moniker(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("money(%#)", "0")]
	public async Task Money(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("quota(%#)", "")]
	public async Task Quota(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("powers(%#)", "")]
	public async Task Powers(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("findable(%#,%#)", "1")]
	public async Task Findable(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("hidden(%#)", "0")]
	public async Task Hidden(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("playermem()", "0")]
	public async Task Playermem(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}
}
