using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>cache()</c> after the PennMUSH patch for issue 1326, stored as a plain attribute whose own modification time
/// decides freshness; and <c>mtime()</c>/<c>msecs()</c> given an attribute. Each test works on a thing of its own.
/// </summary>
public class CacheFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;

	private async Task<string> EvalOnNewThingAsync(string expression)
	{
		var name = TestIsolationHelpers.GenerateUniqueName("CacheFn");
		return (await Parser.EvaluateAsync(MarkupText.Plain($"[setq(0,create({name}))]{expression}"))).ToPlainText();
	}

	[Test]
	public async Task AMissStoresThePlainValueAndAHitReturnsIt()
		=> await Assert.That(await EvalOnNewThingAsync("[cache(%q0,ANSWER,first)]-[cache(%q0,ANSWER,second)]-[get(%q0/ANSWER)]"))
			.IsEqualTo("first-first-first");

	[Test]
	public async Task AHitDoesNotEvaluateTheValue()
		=> await Assert.That(await EvalOnNewThingAsync(
				"[cache(%q0,LAZY,[setq(z,ran-first)]a)][cache(%q0,LAZY,[setq(z,ran-second)]b)]-%qz"))
			.IsEqualTo("aa-ran-first");

	[Test]
	public async Task AWindowOfZeroAlwaysMisses()
		=> await Assert.That(await EvalOnNewThingAsync("[cache(%q0,NOW,one,0)]-[cache(%q0,NOW,two,0)]-[get(%q0/NOW)]"))
			.IsEqualTo("one-two-two");

	[Test]
	public async Task ASlidingHitKeepsTheValueAndRewritesTheTime()
		=> await Assert.That(await EvalOnNewThingAsync(
				"[cache(%q0,SLIDE,kept)][setq(t,msecs(%q0/SLIDE))][cache(%q0,SLIDE,lost,3600,1)]-[gte(msecs(%q0/SLIDE),%qt)]"))
			.IsEqualTo("keptkept-1");

	[Test]
	[Arguments("abc", ErrorMessages.Returns.Integer)]
	[Arguments("-5", ErrorMessages.Returns.UInteger)]
	public async Task ABadWindowIsRefusedBeforeTheValueIsEvaluated(string window, string expected)
		=> await Assert.That(await EvalOnNewThingAsync($"[cache(%q0,BAD,[setq(z,ran)]x,{window})]-%qz"))
			.IsEqualTo($"{expected}-");

	[Test]
	public async Task MsecsOfAnAttributeIsWhenItsValueWasWritten()
		=> await Assert.That(await EvalOnNewThingAsync(
				"[setq(b,secs())][attrib_set(%q0/WHEN,x)][setq(m,msecs(%q0/WHEN))][and(gte(%qm,%qb),lte(%qm,secs()))]"))
			.IsEqualTo("1");

	[Test]
	public async Task MtimeOfAnAttributeIsATimeString()
		=> await Assert.That(await EvalOnNewThingAsync(
				"[attrib_set(%q0/WHEN,x)][eq(convtime(mtime(%q0/WHEN)),msecs(%q0/WHEN))]"))
			.IsEqualTo("1");

	[Test]
	public async Task MsecsOfAMissingAttributeIsAnError()
		=> await Assert.That(await EvalOnNewThingAsync("[msecs(%q0/NOT_THERE)]"))
			.IsEqualTo(ErrorMessages.Returns.NoSuchAttribute);
}
