using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class ListFunctionUnitTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test, NotInParallel]
	[Arguments("iter(1 2 3,%i0)", "1 2 3")]
	[Arguments("iter(1,%i1)", "#-1 REGISTER OUT OF RANGE")]
	[Arguments("iter(1 2 3,add(%i0,1))", "2 3 4")]
	[Arguments("iter(1|2|3,%i0,|)", "1|2|3")]
	[Arguments("iter(1|2|3,%i0,|,-)", "1-2-3")]
	[Arguments("iter(1|2|3,add(%i0,1),|,-)", "2-3-4")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%i1)),|,-)", "2 3 4-3 4 5-4 5 6")]
	// TODO: %iL does not evaluate to the correct value.
	// [Arguments("iter(1|2|3,iter(1 2 3,add(%i0,%iL)),|,-)", "2 3 4-3 4 5-4 5 6")]
	public async Task IterationValue(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	// TODO: Fix: %$0 is for switches.
	// TODO: This should be #@, which is not yet implemented.
	[Test, NotInParallel]
	[Arguments("iter(5 6 7,%$0)", "1 2 3")]
	[Arguments("iter(1|2|3,iter(1 2 3,add(%$0,%i1)),|,-)", "2 2 2-4 4 4-6 6 6")]
	public async Task IterationNumber(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
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
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("rest(1|2|3)", "")]
	[Arguments("rest(%b)", "")]
	[Arguments("rest(null())", "")]
	[Arguments("rest(1|2|3 5 6)", "5 6")]
	[Arguments("rest(1 2 3)", "2 3")]
	[Arguments("rest(1|2|3,|)", "2|3")]
	public async Task Rest(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("last(1|2|3)", "1|2|3")]
	[Arguments("last(null())", "")]
	[Arguments("last(%b)", "")]
	[Arguments("last(1|2|3 5 6)", "6")]
	[Arguments("last(1 2 3)", "3")]
	[Arguments("last(1|2|3,|)", "3")]
	public async Task Last(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("first(1 2 3)", "1")]
	[Arguments("first(a|b|c,|)", "a")]
	public async Task First(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("words(1 2 3)", "3")]
	[Arguments("words(single)", "1")]
	public async Task Words(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("extract(a b c,2)", "b")]
	[Arguments("extract(a b c,1,2)", "a b")]
	[Arguments("extract(a|b|c,2,3,|)", "b|c")]
	public async Task Extract(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("grab(This is a test,tes*)", "test")]
	[Arguments("grab(a|b|c|d,c,|)", "c")]
	public async Task Grab(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("graball(This is a test of a test,test)", "test test")]
	[Arguments("graball(This|is|testing|a|test,tes*,|)", "testing|test")]
	public async Task Graball(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("sort(3 1 2)", "1 2 3")]
	[Arguments("sort(foo bar baz)", "bar baz foo")]
	public async Task Sort(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("filter(test/is_odd,1 2 3 4 5 6)", "1 3 5")]
	public async Task Filter(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("fold(test/add_func,1 2 3)", "6")]
	public async Task Fold(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("ldelete(a b c d,2)", "a c d")]
	[Arguments("ldelete(a|b|c|d,2,1,|)", "a|c|d")]
	public async Task Ldelete(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("lreplace(a b c,2,foo)", "a foo c")]
	[Arguments("lreplace(a|b|c,2,foo,|)", "a|foo|c")]
	public async Task Lreplace(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("member(a b c,b)", "2")]
	[Arguments("member(a|b|c,b,|)", "2")]
	public async Task Member(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("remove(a b c b,b)", "a c")]
	[Arguments("remove(a|b|c|b,b,|)", "a|c")]
	public async Task Remove(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("setunion(a b c,c d e)", "a b c d e")]
	[Arguments("setunion(1 2 3,2 3 4)", "1 2 3 4")]
	public async Task Setunion(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("setinter(a b c,c d e)", "c")]
	[Arguments("setinter(1 2 3,2 3 4)", "2 3")]
	public async Task Setinter(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("setdiff(a b c,c d e)", "a b")]
	[Arguments("setdiff(1 2 3,2 3 4)", "1")]
	public async Task Setdiff(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("matchall(foo bar baz,ba*)", "2 3")]
	public async Task Matchall(string function, string expected)
	{
		var result = (await Parser!.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}
}