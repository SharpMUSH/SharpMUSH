using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary><c>uargs()</c> passes named arguments; <c>%&lt;name&gt;</c> and <c>r(&lt;name&gt;,args)</c> read them.</summary>
public class NamedArgumentTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	/// <summary>
	/// Creates a thing of this test's own in %q0, sets each attribute on it, then evaluates
	/// <paramref name="call"/>. Attribute text is stored unevaluated: write it as softcode would see it.
	/// </summary>
	private async Task<string> CallAsync(string call, params (string Name, string Text)[] attributes)
	{
		var thing = $"NamedArg_{Guid.NewGuid():N}"[..20];
		var sets = string.Concat(attributes.Select(a => $"[attrib_set(%q0/{a.Name},lit({a.Text}))]"));
		var result = await Parser.EvaluateAsync(MarkupText.Plain($"[setq(0,create({thing}))]{sets}[{call}]"));
		return result.ToPlainText();
	}

	[Test]
	public async Task UargsPassesEachValueUnderItsName()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,greeting,Hi,who,Bob)", ("FN", "%<greeting> %<who>")))
			.IsEqualTo("Hi Bob");

	[Test]
	public async Task NamesAreCaseInsensitive()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,Who,Bob)", ("FN", "%<WHO>/%<who>")))
			.IsEqualTo("Bob/Bob");

	[Test]
	public async Task RArgsReadsTheSameNames()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,Bob)", ("FN", "r(who,args)")))
			.IsEqualTo("Bob");

	[Test]
	public async Task TheNameIsEvaluated()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,Bob)", ("FN", "%<[lcstr(WHO)]>")))
			.IsEqualTo("Bob");

	[Test]
	public async Task UargsPassesNoPositionalArguments()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,Bob)", ("FN", "[%0]-%+")))
			.IsEqualTo("-0");

	[Test]
	public async Task AnUnsetNameIsEmpty()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,Bob)", ("FN", "[%<nobody>]x")))
			.IsEqualTo("x");

	[Test]
	public async Task ANumberNamesThePositionalArgument()
		=> await Assert.That(await CallAsync("u(%q0/FN,first,second)", ("FN", "%<0>+%<1>")))
			.IsEqualTo("first+second");

	[Test]
	public async Task NamedArgumentsEndWithTheirCall()
		=> await Assert.That(await CallAsync("uargs(%q0/OUTER,who,Bob)",
				("OUTER", "%<who>:[u(INNER)]"), ("INNER", "[%<who>]x")))
			.IsEqualTo("Bob:x");

	[Test]
	public async Task AnEmptyNameIsRefused()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,,Bob)", ("FN", "%<who>")))
			.IsEqualTo(ErrorMessages.Returns.BadArgumentName);

	[Test]
	public async Task ANameWithoutAValueIsRefused()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who)", ("FN", "%<who>")))
			.IsEqualTo(string.Format(ErrorMessages.Returns.GotEvenArgs, "UARGS"));

	/// <summary>An unclosed <c>%&lt;</c> is a parse error, as an unclosed <c>%q&lt;</c> is.</summary>
	[Test]
	[Arguments("a %< b")]
	[Arguments("a %q< b")]
	public async Task AnUnclosedNameIsAParseError(string text)
		=> await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain(text))).ToPlainText())
			.StartsWith("#-1 PARSER FAILURE");
}
