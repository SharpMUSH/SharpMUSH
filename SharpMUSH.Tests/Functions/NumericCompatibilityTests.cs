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
