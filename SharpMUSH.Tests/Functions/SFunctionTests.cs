using SharpMUSH.Library;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class SFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	[Arguments("s(hello)", "hello")]
	[Arguments("s([add(1,2)])", "3")]
	[Arguments("s(  hello  )", "hello")]
	[Arguments("s([ljust(a,5)])", "a")]
	public async Task SFunction(string input, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}

	/// <summary>
	/// The object argument is evaluated before it is located, as PennMUSH's fun_objeval does with
	/// process_expression over args[0]: objeval is NoParse for the sake of the expression only. The
	/// last two cases are what a helper attribute writes — an object that arrives as %0 or is
	/// computed — and answered #-1 NO MATCH while the raw text was looked up as a name.
	/// </summary>
	[Test]
	[Arguments("objeval(#1,add(1,2))", "3")]
	[Arguments("objeval(#1,num(me))", "#1")]
	[Arguments("objeval([num(#1)],num(me))", "#1")]
	[Arguments("objeval(#[add(0,1)],add(1,2))", "3")]
	public async Task ObjevalFunction(string input, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message!.ToString()).IsEqualTo(expected);
	}
}
