using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class NumericCompatibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments(true, false, "iter(a b,[ibreak()]%iL)", "a")]
	[Arguments(true, false, "iter(a b,[ibreak(1foo)]%iL)", "#-1 ARGUMENT MUST BE INTEGERa #-1 ARGUMENT MUST BE INTEGERb")]
	[Arguments(false, true, "add(0x10,2)", "18")]
	[Arguments(true, true, "add(0x10,2)", "18")]
	[Arguments(true, true, "add(0x1zz,2)", "3")]
	[Arguments(false, true, "add(-0x1.8p2,0)", "-6")]
	[Arguments(false, true, "lmath(neq,1 1 2)", "1")]
	[Arguments(false, false, "round(e(),2)", "2.72")]
	[Arguments(false, false, "round(pi(),2)", "3.14")]
	[Arguments(false, false, "inc()", "#-1 ARGUMENT MUST END IN AN INTEGER")]
	[Arguments(false, false, "inc(foo)", "#-1 ARGUMENT MUST END IN AN INTEGER")]
	[Arguments(false, true, "inc(foo)", "foo1")]
	[Arguments(false, true, "dec(foo)", "foo-1")]
	[Arguments(false, true, "inc(9223372036854775807)", "#-1 OUT OF RANGE")]
	[Arguments(false, true, "dec(-9223372036854775808)", "#-1 OUT OF RANGE")]
	[Arguments(false, true, "inc(12%b)", "12 1")]
	[Arguments(false, false, "add(,2)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments(false, false, "eq(,0)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments(false, false, "abs()", "#-1 ARGUMENT MUST BE NUMBER")]
	[Arguments(false, true, "add(,2)", "2")]
	[Arguments(false, true, "add(foo,2)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	[Arguments(false, true, "add(1e2,2)", "102")]
	[Arguments(true, false, "add(foo,2)", "2")]
	[Arguments(true, false, "add(12foo,2)", "14")]
	[Arguments(true, false, "add(1.5foo,2)", "3.5")]
	[Arguments(true, false, "add(,2)", "2")]
	[Arguments(true, false, "abs(-1.5foo)", "1.5")]
	[Arguments(true, false, "div(12foo,2)", "6")]
	[Arguments(true, false, "map(#apply/abs,-2foo)", "2")]
	// Integer arguments (PennMUSH is_integer / is_uinteger / is_strict_*, src/parse.c:373-566); the
	// expected values are a reference PennMUSH's answers under the same two options.
	[Arguments(false, false, "[extract(a b c,2 ,1)]|[left(abcde, 2 )]|[ljust(a,3 )]|", "b|ab|a  |")]
	[Arguments(false, false, "extract(a b c,2x,1)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments(false, false, "left(abcde,2x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "right(abcde,2x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "left(abcde,-1)", "#-1 OUT OF RANGE")]
	[Arguments(false, false, "right(abcde,-1)", "#-1 OUT OF RANGE")]
	[Arguments(false, false, "left(abcde,+2)", "ab")]
	[Arguments(false, false, "repeat(ab,2x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "ljust(a,3x)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(false, false, "ljust(a,-1)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(false, false, "rjust(a,+3)", "  a")]
	[Arguments(false, false, "space(+2)", "  ")]
	[Arguments(false, false, "chr(+65)", "A")]
	[Arguments(false, false, "left(abcde,)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, true, "left(abcde,)", "")]
	[Arguments(false, true, "ljust(a,)", "a")]
	[Arguments(false, true, "repeat(ab,)", "")]
	[Arguments(false, true, "extract(a b c,2,)", "")]
	[Arguments(false, true, "mid(abcde,,2)", "ab")]
	[Arguments(false, true, "space()", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(false, true, "chr()", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(true, true, "left(abcde,2foo)", "ab")]
	[Arguments(true, true, "right(abcde,foo)", "")]
	[Arguments(true, true, "repeat(ab,2x)", "abab")]
	[Arguments(true, true, "ljust(a,3x)", "a  ")]
	[Arguments(true, true, "mid(abcde,1x,2)", "bc")]
	[Arguments(true, true, "extract(a b c,2x,1x)", "b")]
	[Arguments(true, true, "wordpos(a b c,3x)", "2")]
	[Arguments(true, true, "delete(abcde,1x,2)", "ade")]
	[Arguments(true, true, "strinsert(abc,1x,X)", "aXbc")]
	[Arguments(true, true, "space(2x)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(true, true, "chr(65x)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	// List positions go through find_list_position, which asks is_integer (src/funlist.c:197-202); one
	// it refuses names no element, and linsert() then answers the list as it was (:1819-1821).
	[Arguments(false, false, "linsert(a b c,2x,X)", "a b c")]
	[Arguments(false, true, "linsert(a b c,,X)", "a b c")]
	[Arguments(true, true, "linsert(a b c,2x,X)", "a X b c")]
	[Arguments(false, false, "elements(a b c,2x 3)", "c")]
	[Arguments(true, true, "elements(a b c,2x 3)", "b c")]
	[Arguments(false, false, "ldelete(a b c,2x)", "a b c")]
	[Arguments(true, true, "ldelete(a b c,2x)", "a c")]
	[Arguments(true, true, "lreplace(a b c,2x,X)", "a X c")]
	[Arguments(true, true, "iter(a b,[inum(0x)])", "#-1 ARGUMENT MUST BE INTEGER #-1 ARGUMENT MUST BE INTEGER")]
	public async Task LiveOptionsControlValidationAndEvaluation(bool tinyMath, bool nullEqualsZero, string expression, string expected)
	{
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Compatibility = baseline.Compatibility with { TinyMath = tinyMath, NullEqualsZero = nullEqualsZero }
		});
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var parser = original with { ServiceProvider = new OptionsProvider(original.ServiceProvider, options) };
		var result = (await parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();
		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("round(1.25,2,1)", "1.25")]
	[Arguments("lnum(0.1,0.3,|,0.1)", "0.1|0.2|0.3")]
	public async Task NumericOutputIsInvariant(string expression, string expected)
	{
		var prior = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
			await LiveOptionsControlValidationAndEvaluation(false, true, expression, expected);
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = prior; }
	}

	private sealed class OptionsProvider(IServiceProvider inner, IOptionsWrapper<SharpMUSHOptions> options) : IServiceProvider
	{
		public object? GetService(Type serviceType) => serviceType == typeof(IOptionsWrapper<SharpMUSHOptions>)
			? options : inner.GetService(serviceType);
	}
}
