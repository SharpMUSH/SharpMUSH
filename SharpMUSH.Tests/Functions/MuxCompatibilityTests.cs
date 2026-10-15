using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// TinyMUX's forms of functions PennMUSH shares, and the four <c>mux_</c> options for the ones that
/// cannot be told apart. Every TinyMUX expectation is the answer of TinyMUX 2.14.0.11 (5f3a3f1) to the
/// same line; the PennMUSH ones are PennMUSH 1.8.8's.
/// </summary>
public class MuxCompatibilityTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Flags]
	public enum Mux
	{
		None = 0,
		Logic = 1,
		Justify = 2,
		Collation = 4,
		List = 8
	}

	[Test]
	// Calls PennMUSH refuses, answered as TinyMUX does whatever the options.
	[Arguments(Mux.None, "[switch(x,default)]|[add(5)]|[mul(3)]|[itext()]|[lnum()]|[iter(a b,itext())]", "default|5|3|||a b")]
	[Arguments(Mux.None, "[and(1)]|[or(0)]|[cand(1)]|[cor(0)]|[xor(1)]", "1|0|1|0|1")]
	[Arguments(Mux.None, "switch(x,a,1)", "")]
	[Arguments(Mux.None, "wrap(aaa bbb ccc,5,right)", "  aaa\n  bbb\n  ccc")]
	[Arguments(Mux.None, "wrap(aaa bbb ccc,7,center,<,>,2)", "<aaa bbb>\n  <  ccc  >")]
	[Arguments(Mux.None, "wrap(aaa bbb ccc,5,left,,,,|)", "aaa  |bbb  |ccc  ")]
	[Arguments(Mux.None, "wrap(abc,5,x)", "#-1 INVALID JUSTIFICATION SPECIFIED")]
	// PennMUSH's wrap() is unchanged when its third argument is empty or a number.
	[Arguments(Mux.None, "wrap(abc def,4,4)", "abc\ndef")]
	[Arguments(Mux.None, "wrap(abc def,4,,|)", "abc|def")]
	// The set functions take TinyMUX's order, the separator and then the sort type, when the fourth
	// argument names no sort type and the fifth names one.
	[Arguments(Mux.None, "setunion(B a c,b, ,|,a)", "B|a|b|c")]
	[Arguments(Mux.None, "setunion(B a c,b, ,a,|)", "B|a|b|c")]
	// Otherwise they stay PennMUSH's: any non-empty fourth argument is a sort type, so TinyMUX's
	// four-argument form (separator fourth) is read as PennMUSH reads it, and an empty fourth is
	// PennMUSH's sort type before a fifth.
	[Arguments(Mux.None, "setunion(c a b,b d, ,|)", "a b c d")]
	[Arguments(Mux.None, "setinter(a b c,c b, ,-)", "b c")]
	[Arguments(Mux.None, "setunion(c a b,b d, ,,n)", "anbncnd")]
	// The options off: PennMUSH's answers.
	[Arguments(Mux.None, "[and(1,abc)]|[or(0,abc)]|[xor(1,1,1)]|[xor(1,1)]", "1|1|0|0")]
	[Arguments(Mux.None, "[ljust(abcdef,3)]|[rjust(abcdef,3)]|[center(abcdefg,3)]", "abcdef|abcdef|abcdefg")]
	[Arguments(Mux.None, "[sort(b A a B)]|[comp(a,B)]|[comp(B,a)]", "A a b B|1|-1")]
	// The options on: TinyMUX's.
	[Arguments(Mux.Logic, "[and(1,abc)]|[or(0,abc)]|[xor(1,1,1)]|[xor(1,1)]|[and(2,-1)]", "0|0|1|0|1")]
	[Arguments(Mux.Logic, "[t(abc)]|[not(abc)]", "1|0")]
	[Arguments(Mux.Justify, "[ljust(abcdef,3)]|[rjust(abcdef,3)]|[center(abcdefg,3)]|[ljust(ab,4,xy)]", "abc|abc|abc|abxy")]
	[Arguments(Mux.Justify, "ljust(abcdef,3,,0)", "abcdef")]
	[Arguments(Mux.Collation, "[sort(b A a B)]|[comp(a,B)]|[comp(B,a)]|[sort(b A a B,a)]|[comp(a,a)]", "a A b B|-1|1|A B a b|0")]
	[Arguments(Mux.List, "list(a b)", "#-1 FUNCTION (LIST) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1")]
	public async Task FunctionsAnswerAsTinyMux(Mux options, string expression, string expected)
		=> await Assert.That(await Evaluate(options, expression)).IsEqualTo(expected);

	[Test]
	public async Task MuxListTellsTheEnactorEachResult()
	{
		var marker = TestIsolationHelpers.GenerateUniqueName("MuxList");
		var god = Factory.ExecutorDBRef;
		var before = Factory.Notifications.CountFor(god);

		var result = await Evaluate(Mux.List, $"list(a b|c,{marker}%i0,|)");

		await Assert.That(result).IsEqualTo(string.Empty);
		await Factory.Notifications.WaitForAsync(god, $"{marker}c", startIndex: before);
		await Assert.That(Factory.Notifications.For(god).Skip(before).Where(line => line.StartsWith(marker)))
			.IsEquivalentTo([$"{marker}a b", $"{marker}c"]);
	}

	[Test]
	public async Task PennListStillNamesFunctions()
		=> await Assert.That(await Evaluate(Mux.None, "member(list(functions),LIST)")).IsNotEqualTo("0");

	[Test]
	public async Task AttributeSubstitutionReadsTheExecutorsAttribute()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("MUXSUB").ToUpperInvariant();
		await Evaluate(Mux.None, $"set(me,{name}:hello)");

		await Assert.That(await Evaluate(Mux.None, $"%=<{name}>|%=<{name.ToLowerInvariant()}>|%=<NO_SUCH_{name}>|%=x"))
			.IsEqualTo("hello|hello||x");
	}

	[Test]
	public async Task PercentEqualsIsTheAttributeBeingEvaluated()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("PennEquals");
		await Assert.That(await Evaluate(Mux.None,
				$"[setq(0,create({thing}))][attrib_set(%q0/FN`SUB,lit(%=))][strmatch(u(%q0/FN`SUB),#*/FN`SUB)]|[eq(after(before(u(%q0/FN`SUB),/),#),after(num(%q0),#))]|%="))
			.IsEqualTo("1|1|");
	}

	[Test]
	public async Task AttributeSubstitutionReadsANumberedArgument()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("MuxArgs");
		await Assert.That(await Evaluate(Mux.None,
				$"[setq(0,create({thing}))][attrib_set(%q0/FN,lit(%=<0>+%=<1>))][u(%q0/FN,first,second)]"))
			.IsEqualTo("first+second");
	}

	private async Task<string> Evaluate(Mux mux, string expression)
	{
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Compatibility = baseline.Compatibility with
			{
				MuxLogic = mux.HasFlag(Mux.Logic),
				MuxJustify = mux.HasFlag(Mux.Justify),
				MuxCollation = mux.HasFlag(Mux.Collation),
				MuxList = mux.HasFlag(Mux.List)
			}
		});
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var parser = original with { ServiceProvider = new OptionsProvider(original.ServiceProvider, options) };
		return (await parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();
	}

	private sealed class OptionsProvider(IServiceProvider inner, IOptionsWrapper<SharpMUSHOptions> options) : IServiceProvider
	{
		public object? GetService(Type serviceType) => serviceType == typeof(IOptionsWrapper<SharpMUSHOptions>)
			? options : inner.GetService(serviceType);
	}
}
