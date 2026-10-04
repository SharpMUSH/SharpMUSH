using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// In-place action lists nest at most <see cref="ParserState.MaxInplaceDepth"/> deep inside one queue
/// entry. PennMUSH's <c>do_entry</c> runs a nested in-place entry only while <c>include_recurses &lt; 50</c>
/// (<c>src/cque.c:1182</c>) and drops a deeper one silently; every level above it carries on with the
/// rest of its list. The count is of in-place levels of any kind, not of one attribute re-entering.
/// </summary>
public class InplaceDepthLimitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private ValueTask<CallState> Run(string command)
		=> Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));

	private async ValueTask<string> Get(DBRef obj, string attribute)
		=> (await Parser.FunctionParse(MarkupText.Plain($"[get({obj}/{attribute})]")))!.Message!.ToPlainText();

	[Test]
	public async ValueTask ASelfIncludeRunsFiftyLevelsAndEveryLevelFinishesItsList()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InplaceSelf");
		await Run($"&CNT {obj}=0");
		await Run($"&AFTER {obj}=0");
		await Run($"&INC {obj}=&CNT {obj}=[inc(get({obj}/CNT))];@include {obj}/INC;&AFTER {obj}=[inc(get({obj}/AFTER))]");

		await Run($"@include {obj}/INC");

		await Assert.That(await Get(obj, "CNT")).IsEqualTo("50")
			.Because("a typed line is depth 0, and lists at depths 1 through 50 run");
		await Assert.That(await Get(obj, "AFTER")).IsEqualTo("50")
			.Because("the 51st level is dropped, and each of the 50 above it goes on past its @include");
	}

	[Test]
	public async ValueTask EveryKindOfInplaceListCountsTowardsTheSameDepth()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InplaceMixed");
		await Run($"&CNT {obj}=0");
		await Run($"&INC {obj}=&CNT {obj}=[inc(get({obj}/CNT))];@switch/inplace 1=1,{{@include {obj}/INC}}");

		await Run($"@include {obj}/INC");

		await Assert.That(await Get(obj, "CNT")).IsEqualTo("25")
			.Because("each round is two in-place levels, the @include and the @switch/inplace");
	}

	[Test]
	public async ValueTask AQueuedEntryCountsItsDepthFromZero()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InplaceQueued");
		await Run($"&CNT {obj}=0");
		await Run($"&INC {obj}=&CNT {obj}=[inc(get({obj}/CNT))];@include {obj}/INC");

		// @wait 0 queues the list as a new entry, whose own list is depth 0 like a typed line.
		await Run($"@wait 0=@include {obj}/INC");

		// Wait for the count to start and then to hold still for a second: the entry has finished.
		var count = "0";
		var stableSince = DateTime.UtcNow;
		for (var deadline = DateTime.UtcNow.AddSeconds(60); DateTime.UtcNow < deadline;)
		{
			await Task.Delay(100);
			var current = await Get(obj, "CNT");
			if (current != count) { count = current; stableSince = DateTime.UtcNow; }
			else if (count != "0" && DateTime.UtcNow - stableSince > TimeSpan.FromSeconds(1)) break;
		}

		await Assert.That(count).IsEqualTo("50");
	}
}
