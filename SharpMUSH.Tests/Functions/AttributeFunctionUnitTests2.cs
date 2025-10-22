using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class AttributeFunctionUnitTests2
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("grep(%#,test,*)", "")]
	public async Task Grep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("grepi(%#,test,*)", "")]
	public async Task Grepi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("pgrep(%#,test,*)", "")]
	public async Task Pgrep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regrep(%#,test,*)", "")]
	public async Task Regrep(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regrepi(%#,test,*)", "")]
	public async Task Regrepi(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regedit(obj/attr,pattern,replacement)", "")]
	public async Task Regedit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("reglattr(%#,test)", "")]
	public async Task Reglattr(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xattr(#0,attr)", "")]
	public async Task Xattr(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xattrp(#0,attr)", "0")]
	public async Task Xattrp(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xcon(#0)", "")]
	public async Task Xcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xexits(#0)", "")]
	public async Task Xexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xmwhoid()", "")]
	public async Task Xmwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xplayers(#0)", "")]
	public async Task Xplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xthings(#0)", "")]
	public async Task Xthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvcon(#0)", "")]
	public async Task Xvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvexits(#0)", "")]
	public async Task Xvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvplayers(#0)", "")]
	public async Task Xvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xvthings(#0)", "")]
	public async Task Xvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xwho()", "")]
	public async Task Xwho(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("xwhoid()", "")]
	public async Task Xwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
