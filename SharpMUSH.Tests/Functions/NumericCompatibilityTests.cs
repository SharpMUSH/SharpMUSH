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
	// beep: is_integer, then 1..5 (src/funstr.c:1415-1427).
	[Arguments(false, false, "beep(x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "beep(0)", "#-1 OUT OF RANGE")]
	[Arguments(false, false, "beep(6)", "#-1 OUT OF RANGE")]
	// xwho family: strict integers or e_int, start and count of at least 1 (src/bsd.c:6455-6466).
	[Arguments(false, false, "xwho(x,1)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "xmwhoid(1,x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "xwhoid(1,0)", "#-1 ARGUMENT OUT OF RANGE")]
	[Arguments(false, false, "xmwho(1,0)", "#-1 ARGUMENT OUT OF RANGE")]
	// entrances: each bound a strict integer or a dbref, else e_ints (src/wiz.c:1808-1827).
	[Arguments(false, false, "entrances(here,a,x)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments(false, false, "entrances(here,a,0,1x)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	// pidinfo: a strict unsigned integer, else e_uint (src/cque.c:1747-1749).
	[Arguments(false, false, "pidinfo(-1)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	// A PID past int.MaxValue is still a PID to look up (unsigned int range).
	[Arguments(false, false, "pidinfo(3000000000)", "#-1 NO SUCH PID")]
	// benchmark: is_number, truncated, at least 1, else e_uint (src/funmisc.c:1492-1501).
	[Arguments(false, false, "benchmark(1,0)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(false, false, "benchmark(1,0.5)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	[Arguments(false, false, "benchmark(1,x)", "#-1 ARGUMENT MUST BE POSITIVE INTEGER")]
	// randextract: an empty count is 0 and answers nothing; a given one must be strict
	// (src/funlist.c:1160-1167).
	[Arguments(false, false, "randextract(a b c,)", "")]
	[Arguments(false, false, "randextract(a b c,0)", "")]
	[Arguments(false, false, "randextract(a b c,1x)", "#-1 ARGUMENT MUST BE INTEGER")]
	// A sortby() comparison and a numeric sort key are parse_integer of their text (src/sort.c:146,
	// :346-349), so their leading digits count.
	[Arguments(false, false, "sortby(lit(#lambda/[sub(%0,%1)]x),3 1 2)", "1 2 3")]
	[Arguments(false, false, "sortkey(lit(#lambda/%0),10x 9x 1x,n)", "1x 9x 10x")]
	[Arguments(false, false, "unique(1 1x 2,n)", "1 2")]
	// r(): a level is any strict number, truncated, and anything else a bad register name
	// (src/funmisc.c:752-756).
	[Arguments(false, false, "iter(a b,r(0.9,iter))", "a b")]
	[Arguments(false, false, "iter(a,r(x,iter))", "#-1 REGISTER NAME INVALID")]
	[Arguments(false, false, "switch(a,a,r(x,switch))", "#-1 REGISTER NAME INVALID")]
	// suggest: is_integer, else e_int (src/help.c:1843-1847).
	[Arguments(false, false, "suggest(nosuchcategory,word,,x)", "#-1 ARGUMENT MUST BE INTEGER")]
	// comp: N and F take strict numbers only (src/funstr.c:475-490).
	[Arguments(false, false, "comp(1,x,N)", "#-1 ARGUMENTS MUST BE INTEGERS")]
	[Arguments(false, false, "comp(1,x,F)", "#-1 ARGUMENTS MUST BE NUMBERS")]
	// F compares as strtod doubles, beyond decimal's range.
	[Arguments(false, false, "comp(1e100,2e100,F)", "-1")]
	[Arguments(false, false, "comp(2,10,N)", "-1")]
	// wrap: empty text first, then int_check widths, then at least 2 (src/funstr.c:1644-1669).
	[Arguments(false, false, "wrap(,x)", "")]
	[Arguments(false, false, "wrap(abc,1)", "#-1 WIDTH TOO SMALL")]
	[Arguments(false, false, "wrap(abc,x)", "#-1 ARGUMENT MUST BE INTEGER")]
	[Arguments(false, false, "wrap(abc def,)", "abc def")]
	[Arguments(false, true, "wrap(abc def,)", "#-1 WIDTH TOO SMALL")]
	[Arguments(false, false, "wrap(abc def,3,0)", "abc\ndef")]
	// timestring: a pad is_uinteger refuses is e_uints (src/funtime.c:494-497).
	[Arguments(false, false, "timestring(5,x)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments(false, false, "timestring(5,-1)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	// etime: is_integer width, a negative one out of range (src/funtime.c:375-383).
	[Arguments(false, false, "etime(5,-1)", "#-1 OUT OF RANGE")]
	[Arguments(true, false, "etime(61,5x)", "1m")]
	// die: is_uinteger counts, 1..700 dice, and a boolean for every roll (src/funmisc.c:834-866).
	[Arguments(false, false, "die(x,6)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments(false, false, "die(-1,6)", "#-1 ARGUMENTS MUST BE POSITIVE INTEGERS")]
	[Arguments(false, false, "die(0,6)", "#-1 NUMBER OUT OF RANGE")]
	[Arguments(false, false, "die(701,6)", "#-1 NUMBER OUT OF RANGE")]
	[Arguments(false, false, "die(3,1)", "3")]
	[Arguments(false, false, "die(3,1,1)", "1 1 1")]
	[Arguments(false, false, "die(2,0,1)", "0 0")]
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
		var result = (await parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
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
