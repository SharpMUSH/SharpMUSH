using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

public class BooleanFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	[Test]
	public async Task RuntimeTinyBooleansAffectsParsedAndIndirectCalls()
	{
		var baseline = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with { Compatibility = baseline.Compatibility with { TinyBooleans = false } });
		var parser = (MUSHCodeParser)Parser;
		parser = parser with { ServiceProvider = new OptionsProvider(parser.ServiceProvider, options) };
		await Assert.That((await parser.FunctionParse(MarkupText.Plain("t(text)")))!.Message!.ToPlainText()).IsEqualTo("1");
		options.CurrentValue.Returns(baseline with { Compatibility = baseline.Compatibility with { TinyBooleans = true } });
		foreach (var expression in new[] { "t(text)", "and(text,1)", "if(text,1,0)", "map(#apply/t,text)" })
			await Assert.That((await parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText()).IsEqualTo("0");
	}

	private sealed class OptionsProvider(IServiceProvider inner, IOptionsWrapper<SharpMUSHOptions> options) : IServiceProvider
	{
		public object? GetService(Type serviceType) => serviceType == typeof(IOptionsWrapper<SharpMUSHOptions>)
			? options : inner.GetService(serviceType);
	}

	[Test]
	[Arguments("t(1)", "1")]
	[Arguments("t(0)", "0")]
	[Arguments("t(true)", "1")]
	[Arguments("t(false)", "1")]
	[Arguments("t(#-1 Words)", "0")]
	[Arguments("t()", "0")]
	[Arguments("t( )", "0")]
	[Arguments("t(%b)", "0")]
	[Arguments("t(-0)", "0")]
	[Arguments("t(0.0)", "0")]
	[Arguments("t(0e10)", "0")]
	[Arguments("t(ansi(r,-0.0))", "0")]
	[Arguments("t(%t)", "1")]
	[Arguments("t(0text)", "1")]
	[Arguments("t(-0.1)", "1")]
	[Arguments("if(-0,yes,no)", "no")]
	[Arguments("cand(0.0,setq(probe,changed))%q<probe>", "0")]
	[Arguments("neq(1,2)", "1")]
	[Arguments("neq(1,1.0)", "0")]
	[Arguments("neq(1,2,1)", "1")]
	[Arguments("neq(1,2,3)", "1")]
	public async Task T(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("and(1,1)", "1")]
	[Arguments("and(0,1)", "0")]
	[Arguments("and(0,0,1)", "0")]
	[Arguments("and(1,1,1)", "1")]
	public async Task And(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nand(1,1)", "0")]
	[Arguments("nand(0,1)", "1")]
	[Arguments("nand(0,0,1)", "1")]
	[Arguments("nand(1,1,1)", "0")]
	public async Task Nand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("or(1,1)", "1")]
	[Arguments("or(0,1)", "1")]
	[Arguments("or(0,0)", "0")]
	[Arguments("or(0,0,1)", "1")]
	[Arguments("or(1,1,1)", "1")]
	public async Task Or(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nor(1,1)", "0")]
	[Arguments("nor(0,1)", "0")]
	[Arguments("nor(0,0)", "1")]
	[Arguments("nor(0,0,1)", "0")]
	[Arguments("nor(1,1,1)", "0")]
	public async Task Nor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("xor(1,1)", "0")]
	[Arguments("xor(0,1)", "1")]
	[Arguments("xor(1,0)", "1")]
	[Arguments("xor(0,0)", "0")]
	public async Task Xor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("not(1)", "0")]
	[Arguments("not(0)", "1")]
	[Arguments("not(true)", "0")]
	[Arguments("not(false)", "0")]
	public async Task Not(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cand(1,1)", "1")]
	[Arguments("cand(0,1)", "0")]
	[Arguments("cand(0,0,1)", "0")]
	[Arguments("cand(1,1,1)", "1")]
	public async Task Cand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("cor(1,1)", "1")]
	[Arguments("cor(0,1)", "1")]
	[Arguments("cor(0,0)", "0")]
	[Arguments("cor(0,0,1)", "1")]
	public async Task Cor(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await Parser.FunctionParse(MarkupText.Plain(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
}
