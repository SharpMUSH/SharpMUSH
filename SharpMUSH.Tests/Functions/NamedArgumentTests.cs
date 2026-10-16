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
	public async Task PresenceQueryDoesNotForceTheTargetValue()
	{
		var arguments = new Dictionary<string, CallState> { ["who"] = new CallState(MarkupText.Empty, 0, null, () => throw new InvalidOperationException("Target value was forced")) };
		var state = ParserState.RootFor(WebAppFactoryArg.ExecutorDBRef) with { EnvironmentRegisters = arguments, SuppliedArgumentKeys = AttributeArgumentEnvironment.SuppliedKeys(arguments) };
		var result = await Parser.FromState(state).FunctionParse(MarkupText.Plain("[hasarg(who)]|[hasarg(who,supplied)]"));
		await Assert.That(result!.Message.ToPlainText()).IsEqualTo("1|1");
		var queued = state.SnapshotForQueuedAction();
		arguments.Clear();
		var resumed = await Parser.FromState(queued).FunctionParse(MarkupText.Plain("[hasarg(who)]|[hasarg(who,supplied)]"));
		await Assert.That(resumed!.Message.ToPlainText()).IsEqualTo("1|1");
	}

	[Test]
	[Arguments("00,A,who,B", "A|B|2")]
	[Arguments("+10,A", "A||1")]
	[Arguments("who,,count,0", "|0|2")]
	[Arguments("who,A,WHO,B", "A|B|2")]
	public async Task OrderedAliasesKeepEmptyZeroAndNormalizedNames(string arguments, string expected)
		=> await Assert.That(await CallAsync($"uargs(%q0/FN,{arguments})", ("FN", "%<0>|[r(1,args)]|%+"))).IsEqualTo(expected);

	[Test]
	public async Task AliasesAboveNineUseNumericLookup()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,a,0,b,1,c,2,d,3,e,4,f,5,g,6,h,7,i,8,j,9,k,10)", ("FN", "%<10>|[r(10,args)]|%+"))).IsEqualTo("10|10|11");

	[Test]
	public async Task ValuesEvaluateOnceAndInvalidBindingsDoNotRunTheCallback()
		=> await Assert.That(await CallAsync("strcat(uargs(%q0/FN,1,setr(side,once)),|,%q<side>,|,%q<callback>)", ("FN", "setq(callback,ran)")))
			.IsEqualTo(ErrorMessages.Returns.BadArgumentName + "|once|");

	[Test]
	public async Task PresenceOutsideInvocationAndInvalidScope()
	{
		await Assert.That((await Parser.EvaluateAsync(MarkupText.Plain("[hasarg(0)]|[hasarg(0,supplied)]"))).ToPlainText()).IsEqualTo("0|0");
		await Assert.That(await CallAsync("uargs(%q0/FN,who,0)", ("FN", "hasarg(who,wrong)"))).IsEqualTo(ErrorMessages.Returns.InvalidArgument);
	}

	[Test]
	public async Task PresenceDistinguishesEmptyAndMissing()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,)", ("FN", "[hasarg(who)]|[hasarg(who,supplied)]|[hasarg(0,supplied)]|[hasarg(absent)]")))
			.IsEqualTo("1|1|1|0");

	[Test]
	public async Task PresenceBelongsToNestedInvocation()
		=> await Assert.That(await CallAsync("uargs(%q0/OUTER,who,Bob)", ("OUTER", "[hasarg(who,supplied)]|[u(INNER)]|[hasarg(who,supplied)]"), ("INNER", "hasarg(who)")))
			.IsEqualTo("1|0|1");

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
	public async Task UargsPassesPositionalAliases()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,Bob)", ("FN", "[%0]-%+")))
			.IsEqualTo("Bob-1");

	[Test]
	public async Task RepeatedNamesKeepOrderedPositions()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,who,A,WHO,B)", ("FN", "%<who>|%0|%1|%+")))
			.IsEqualTo("B|A|B|2");

	[Test]
	public async Task NumericCollisionIsRefused()
		=> await Assert.That(await CallAsync("uargs(%q0/FN,1,A,who,B)", ("FN", "%0")))
			.IsEqualTo(ErrorMessages.Returns.BadArgumentName);

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
