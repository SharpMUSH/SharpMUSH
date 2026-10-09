using Mediator;
using Microsoft.Extensions.DependencyInjection;
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
		await Assert.That(result!.Message.ToString()).IsEqualTo(expected);
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
		await Assert.That(result!.Message.ToString()).IsEqualTo(expected);
	}

	/// <summary>
	/// An object objeval() cannot use is not an error: fun_objeval (PennMUSH src/funufun.c) falls back
	/// to the executor when match_thing finds nothing, and evaluates the expression anyway. The
	/// executor here is God, so an unmatched object evaluates as #1 — side effects included.
	/// </summary>
	[Test]
	[Arguments("objeval(#99999999,num(me))", "#1")]
	[Arguments("objeval(#[add(99999998,1)],num(me))", "#1")]
	[Arguments("objeval(#99999999,setr(objevalprobe,ran))-[r(objevalprobe)]", "ran-ran")]
	public async Task Objeval_UnmatchedObject_EvaluatesAsTheExecutor(string input, string expected)
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(input));
		await Assert.That(result!.Message.ToString()).IsEqualTo(expected);
	}

	/// <summary>
	/// The same fallback for an object the executor neither controls nor may see_all: a mortal asking
	/// to evaluate as God evaluates as itself, as PennMUSH does, rather than being refused.
	/// </summary>
	[Test]
	public async Task Objeval_UncontrolledObject_EvaluatesAsTheExecutor()
	{
		var mortal = await TestIsolationHelpers.CreateTestPlayerAsync(
			WebAppFactoryArg.Services, WebAppFactoryArg.Services.GetRequiredService<IMediator>(), "ObjevalMortal");

		var result = await Parser.FunctionParse(MarkupText.Plain($"objeval(#{mortal.Number},objeval(#1,num(me)))"));

		await Assert.That(result!.Message.ToString()).IsEqualTo($"#{mortal.Number}");
	}

	/// <summary>
	/// Argument zero's failure metadata survives: a parse failure there is reported on objeval()'s
	/// result, as default() and the other NoParse functions that evaluate their own arguments do.
	/// </summary>
	[Test]
	public async Task Objeval_ObjectArgumentFailure_RetainsHadErrors()
	{
		var result = await Parser.FunctionParse(MarkupText.Plain(@"objeval(ulambda(#lambda/\[),add(1,2))"));

		await Assert.That(result!.HadErrors).IsTrue();
	}
}
