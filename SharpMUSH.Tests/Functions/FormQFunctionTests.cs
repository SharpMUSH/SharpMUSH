using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// Tests for <c>formq()</c> — the SharpMUSH extension that decodes a form-encoded string into
/// named q-registers (<c>%q&lt;form.*&gt;</c> by default), used by the default HTTP verb handlers
/// (help sharphttp) — and for <c>formdecode()</c>, its pure-value PennMUSH-compatible sibling.
/// </summary>
public class FormQFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> Eval(string code) =>
		(await Parser.FunctionParse(MModule.single(code)))?.Message?.ToPlainText() ?? string.Empty;

	[Test]
	public async Task FormQ_SetsRegistersAndReturnsNames()
	{
		var result = await Eval("[setq(n,formq(a=1&b=2))]%q<form.a>/%q<form.b>/%q<n>");

		await Assert.That(result).IsEqualTo("1/2/A B");
	}

	[Test]
	public async Task FormQ_DuplicateParams_JoinWithNewline()
	{
		var result = await Eval("[null(formq(like=potato&like=cheese))]%q<form.like>");

		await Assert.That(result).IsEqualTo("potato\ncheese");
	}

	[Test]
	public async Task FormQ_BracketArrays_CollapseLikeDuplicates()
	{
		// PHP/Rails-style array spelling — like[]=a&like[]=b — lands in the same register as
		// repeated names, with the [] marker stripped.
		var result = await Eval(@"[setq(n,formq(like\[\]=a&like\[\]=b))]%q<form.like>/%q<n>");

		await Assert.That(result).IsEqualTo("a\nb/LIKE");
	}

	[Test]
	public async Task FormQ_DecodesPercentAndPlus()
	{
		// \% keeps the percent literal past the MUSH substitution layer.
		var result = await Eval(@"[null(formq(name=Joe+Smith&note=o\%2F\%60))]%q<form.name>/%q<form.note>");

		await Assert.That(result).IsEqualTo("Joe Smith/o/`");
	}

	[Test]
	public async Task FormQ_NormalizesNamesLikeHeaders()
	{
		// "my name" → MY_NAME (space is not register-safe), mirroring %q<hdr.*> normalization.
		var result = await Eval("[setq(n,formq(my+name=x))]%q<form.my_name>/%q<n>");

		await Assert.That(result).IsEqualTo("x/MY_NAME");
	}

	[Test]
	public async Task FormQ_CustomPrefix()
	{
		var result = await Eval("[null(formq(a=1,arg.))]%q<arg.a>");

		await Assert.That(result).IsEqualTo("1");
	}

	[Test]
	public async Task FormQ_BareToken_BecomesEmptyFlagRegister()
	{
		var result = await Eval("[setq(n,formq(debug&a=1))]%q<n>:[t(%q<form.a>)]");

		await Assert.That(result).IsEqualTo("DEBUG A:1");
	}

	[Test]
	public async Task FormQ_EmptyString_SetsNothingReturnsEmpty()
	{
		var result = await Eval("[formq()]done");

		await Assert.That(result).IsEqualTo("done");
	}

	// formdecode() is PennMUSH-spec (help sharphttp FORMDECODE) and previously had no tests.
	[Test]
	[Arguments("formdecode(name=Joe&hobby=fishing)", "name hobby")] // no paramname → names
	[Arguments("formdecode(name=Joe&hobby=fishing,name)", "Joe")]
	[Arguments("formdecode(like=potato&like=cheese,like,^)", "potato^cheese")] // multi-value + osep
	[Arguments("formdecode(like=a&like=b)", "like like")] // duplicate names listed per occurrence
	[Arguments("formdecode(name=Joe+Smith,name)", "Joe Smith")] // + as space
	[Arguments(@"formdecode(hobby=o\%2F\%60,hobby)", "o/`")] // percent-decoding (\% = literal %)
	[Arguments("formdecode(name=Joe,missing)", "")] // absent param → empty
	public async Task FormDecode(string code, string expected)
	{
		var result = await Eval(code);

		await Assert.That(result).IsEqualTo(expected);
	}
}
