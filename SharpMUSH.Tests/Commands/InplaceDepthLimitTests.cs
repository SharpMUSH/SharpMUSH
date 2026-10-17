using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
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
		=> (await Parser.EvaluateAsync(MarkupText.Plain($"[get({obj}/{attribute})]"))).ToPlainText();

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
	public async ValueTask AnInplaceCommandBodyCountsItsDepthFromZero()
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, "InplaceBody");
		var command = TestIsolationHelpers.GenerateUniqueName("inplacebody").ToLowerInvariant();
		await Run($"&CNT {obj}=0");
		await Run($"&INC {obj}=&CNT {obj}=[inc(get({obj}/CNT))];@include {obj}/INC");
		await Run($"&CMD {obj}=${command}:@include {obj}/INC");

		// A typed line runs the matched body in place, as a queue entry of its own (src/game.c:1224-1225).
		var parser = Parser;
		var initialDepth = parser.CurrentState.InplaceDepth;
		var initialCommandText = parser.CurrentState.CommandText is not null;
		var budgetMs = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>()
			.CurrentValue.Limit.QueueEntryCpuTime;
		var elapsed = Stopwatch.StartNew();
		var result = await parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		elapsed.Stop();
		var count = await Get(obj, "CNT");
		var reason = "the body is depth 0, like a typed line, so its includes run at depths 1 through 50";
		if (count != "50")
		{
			// Evidence only after failure: bypass Mediator/FusionCache to distinguish a stale final get()
			// from a stored undercount. Never replace the original assertion with a diagnostic read error.
			string storedCount;
			try
			{
				// Cooperative cancellation only: the provider may perform its synchronous read before checking it.
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				var stored = await WebAppFactoryArg.Services.GetRequiredService<IAttributeStore>()
					.GetAttributeAsync(obj, ["CNT"], timeout.Token).LastOrDefaultAsync(timeout.Token);
				storedCount = stored is null ? "<missing>"
					: stored.IsUnreadable || stored.IsMetadataUnreadable ? "<unreadable>" : stored.Value.ToPlainText();
			}
			catch (Exception exception)
			{
				storedCount = $"<read failed: {exception.GetType().Name}: {exception.Message}>";
			}
			reason += "; failure evidence: " + JsonSerializer.Serialize(new
			{
				command,
				Object = obj.ToString(),
				initialDepth,
				initialCommandText,
				budgetMs,
				ElapsedMs = elapsed.Elapsed.TotalMilliseconds,
				ObservedCount = count,
				StoredCount = storedCount,
				result.HadErrors,
				result.IsParseFailure,
				result.Depth,
				Message = result.Message.ToPlainText()
			});
		}

		await Assert.That(count).IsEqualTo("50").Because(reason);
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

		// @wait 0 queues the list as a new entry, whose own list is depth 0 like a typed line. DONE is
		// set by the same entry once every in-place level has returned.
		await Run($"@wait 0={{@include {obj}/INC;&DONE {obj}=1}}");

		for (var deadline = DateTime.UtcNow.AddSeconds(60); DateTime.UtcNow < deadline && await Get(obj, "DONE") != "1";)
			await Task.Delay(100);

		var count = await Get(obj, "CNT");
		await Assert.That(count).IsEqualTo("50");
	}
}
