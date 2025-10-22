using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests for documented functions that were initially missed in the comprehensive test coverage
/// These functions all have NotImplementedException and documentation in pennfunc.md
/// </summary>
public class MissingDocumentedFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	// JSON Functions
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("json_map(json_array(1,2,3),add(##,1))", "[2,3,4]")]
	public async Task JsonMap(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsEqualTo(expected);
	}

	// Connection Functions - WHO/LWHO variations
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lwhoid()", "")]
	public async Task Lwhoid(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ncon()", "0")]
	public async Task Ncon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nexits(#0)", "0")]
	public async Task Nexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nplayers()", "0")]
	public async Task Nplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nthings()", "0")]
	public async Task Nthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvcon()", "0")]
	public async Task Nvcon(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvexits()", "0")]
	public async Task Nvexits(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvplayers()", "0")]
	public async Task Nvplayers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nvthings()", "0")]
	public async Task Nvthings(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ports()", "4201")]
	public async Task Ports(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	// Communication Functions - NS variations (NoSpace)
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nsoemit(#1,test)", "")]
	public async Task Nsoemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nspemit(#1,test)", "")]
	public async Task Nspemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nsprompt(#1,test)", "")]
	public async Task Nsprompt(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nsremit(#0,test)", "")]
	public async Task Nsremit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nszemit(#0,test)", "")]
	public async Task Nszemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	// Utility Functions
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("r(0)", "0")]
	public async Task R(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("rand(10)", "")]
	public async Task Rand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("recv()", "")]
	public async Task Recv(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("sent()", "")]
	public async Task Sent(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("suggest(test)", "test")]
	public async Task Suggest(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	// Regex Functions - Extended variations
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regeditall(text,pattern,replacement)", "")]
	public async Task Regeditall(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regeditalli(text,pattern,replacement)", "")]
	public async Task Regeditalli(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regediti(text,pattern,replacement)", "")]
	public async Task Regediti(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("registers()", "")]
	public async Task Registers(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("reglattrp(#0,pattern)", "")]
	public async Task Reglattrp(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("reglmatch(list,pattern)", "")]
	public async Task Reglmatch(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("reglmatchall(list,pattern)", "")]
	public async Task Reglmatchall(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("reglmatchalli(list,pattern)", "")]
	public async Task Reglmatchalli(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("reglmatchi(list,pattern)", "")]
	public async Task Reglmatchi(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regnattr(#0,pattern)", "")]
	public async Task Regnattr(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regnattrp(#0,pattern)", "")]
	public async Task Regnattrp(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regraballi(list,pattern)", "")]
	public async Task Regraballi(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regrabi(text,pattern)", "")]
	public async Task Regrabi(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regxattr(#0,pattern)", "")]
	public async Task Regxattr(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("regxattrp(#0,pattern)", "")]
	public async Task Regxattrp(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	// Dbref Functions
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

	// Text file functions
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("textentries(file)", "0")]
	public async Task Textentries(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("textfile(file)", "")]
	public async Task Textfile(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("textsearch(file,pattern)", "")]
	public async Task Textsearch(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	// Lambda functions
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ulambda(code)", "")]
	public async Task Ulambda(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	// X-prefixed attribute functions (extended versions)
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

	// Special functions
	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("zfun(#0,func,arg)", "")]
	public async Task Zfun(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("zmwho()", "")]
	public async Task Zmwho(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("zone(#0)", "#-1")]
	public async Task Zone(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
