using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class ListFunctionUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser(
			ns: Substitute.For<INotifyService>()
		);
	}

	[Test, NotInParallel]
	[Arguments("iter(1 2 3,%i0)", "1 2 3")]
	[Arguments("iter(1,%i1)", "#-1 OUT OF RANGE")]
	[Arguments("iter(1 2 3,add(%i0,1))", "2 3 4")]
	[Arguments("iter(1|2|3,%i0,|)", "1|2|3")]
	[Arguments("iter(1|2|3,%i0,|,-)", "1-2-3")]
	[Arguments("iter(1|2|3,add(%i0,1),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%i1)),|,-)", "2 3 4-3 4 5-4 5 6")]
	public async Task IterationValue(string function, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	// TODO: Fix: %$0 is for switches.
	// TODO: This should be #@, which is not yet implemented.
	[Test, NotInParallel]
	[Arguments("iter(5 6 7,%$0)", "1 2 3")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%$0,%i1)),|,-)", "2 2 2-4 4 4-6 6 6")]
	public async Task IterationNumber(string function, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments("iter(1|2|3,add(%i0,1)[ibreak()],|,-)", "2")]
	[Arguments("iter(1|2|3,add(%i0,1)[ibreak(0)],|,-)", "2")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(%i0,%i1)][ibreak()]),|,-)", "2 3 4")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(%i0,%i1)][ibreak(0)]),|,-)", "2 3 4")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(%i0,%i1)][ibreak(1)]),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,[add(1,1)][add(%i0,%i1)][ibreak(1)]),|,-)", "22-23-24")]
	[Arguments("iter(1|2|3,iter(1 2 3,[ibreak(1)][add(%i0,%i1)]),|,-)", "2-3-4")]
	// TODO: Why does putting [ibreak()] at the start of the contents cause a different evaluation?
	public async Task IterationBreak(string function, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments("rest(1|2|3)", "")]
	[Arguments("rest(%b)", "")]
	[Arguments("rest(null())", "")]
	[Arguments("rest(1|2|3 5 6)", "5 6")]
	[Arguments("rest(1 2 3)", "2 3")]
	[Arguments("rest(1|2|3,|)", "2|3")]
	public async Task Rest(string function, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	[Arguments("last(1|2|3)", "1|2|3")]
	[Arguments("last(null())", "")]
	[Arguments("last(%b)", "")]
	[Arguments("last(1|2|3 5 6)", "6")]
	[Arguments("last(1 2 3)", "3")]
	[Arguments("last(1|2|3,|)", "3")]
	public async Task Last(string function, string expected)
	{
		var result = (await _parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}
}