using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

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

	private async Task Command(string command)
		=> await WebAppFactoryArg.CommandParser.CommandParse(1,
			WebAppFactoryArg.Services.GetRequiredService<IConnectionService>(), MarkupText.Plain(command));

	private async Task<string> EvalAsync(string expression)
		=> (await Parser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

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
	public async Task ClearingACachedBranchMakesTheNextCallEvaluate()
		=> await Assert.That(await EvalOnNewThingAsync(
				"[cache(%q0,TREE,old)][attrib_set(%q0/TREE`CHILD,x)][attrib_set(%q0/TREE)]-[cache(%q0,TREE,new)]"))
			.IsEqualTo("old-new");

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

	[Test]
	public async Task AHitDoesNotSeeANewRoomUntilTheWindowPasses()
	{
		var count = "[cache(%q0,ROOMS,words(lsearch(all,type,room)),600)]";
		var room = TestIsolationHelpers.GenerateUniqueName("CacheAnnex");
		var result = await EvalOnNewThingAsync(
			$"{count}-[setq(1,dig({room}))][words(lsearch(all,type,room))]-{count}");
		var parts = result.Split('-');

		await Assert.That(parts[2]).IsEqualTo(parts[0]);
		await Assert.That(int.Parse(parts[1])).IsGreaterThan(int.Parse(parts[0]));
	}

	[Test]
	public async Task AWatchOnTheSourceAttributeClearsTheCache()
	{
		var source = TestIsolationHelpers.GenerateUniqueName("CacheLevel").ToUpperInvariant();
		var cached = $"CACHE`{source}";
		var total = $"[cache(#9,{cached},ladd(iter(lsearch(all,type,player),get(##/{source}))),86400)]";
		await Command($"@attribute/access {source}=none");
		await Command($"@attribute/watch {source}");
		await Command($"&WATCH`{source}`CACHE #9=&{cached} #9");
		await Command($"&{source} me=5");
		await Assert.That(await EvalAsync(total)).IsEqualTo("5");

		await Command($"&{source} me=7");
		for (var tries = 0; tries < 100 && await EvalAsync($"[hasattr(#9,{cached})]") == "1"; tries++)
		{
			await Task.Delay(50);
		}

		await Assert.That(await EvalAsync(total)).IsEqualTo("7");
	}
}
