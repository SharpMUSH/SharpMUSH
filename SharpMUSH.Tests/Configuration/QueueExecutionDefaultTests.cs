using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

public class QueueExecutionDefaultTests
{
	[Test]
	[Arguments("", 2000u)]
	[Arguments("queue_entry_cpu_time 0", 0u)]
	[Arguments("queue_entry_cpu_time 1000", 1000u)]
	[Arguments("queue_entry_cpu_time 1500", 1500u)]
	[Arguments("queue_entry_cpu_time 2750", 2750u)]
	public async Task MissingValueUsesTwoSecondsAndExplicitOverridesArePreserved(string content, uint expected)
	{
		var path = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync(path, content);
			await Assert.That(ReadPennMushConfig.Create(path).Limit.QueueEntryCpuTime).IsEqualTo(expected);
		}
		finally { File.Delete(path); }
	}

	/// <summary>
	/// The queue and function limits a new game starts with are the ones PennMUSH ships in
	/// <c>game/mushcnf.dst</c>, except where SharpMUSH documents why not: <c>queue_entry_cpu_time</c>
	/// is an elapsed-time budget here (<c>help execution budget</c>), and <c>call_limit</c> counts
	/// function frames rather than parser recursions (<c>SharpMUSHOptions.Default()</c>).
	/// </summary>
	[Test]
	public async Task LimitDefaults_MatchPennMushShippedConfiguration()
	{
		var limit = SharpMUSHOptions.Default().Limit;

		await Assert.That(limit.PlayerQueueLimit).IsEqualTo(100u);
		await Assert.That(limit.QueueChunk).IsEqualTo(3u);
		await Assert.That(limit.QueueLoss).IsEqualTo(63u);
		await Assert.That(limit.MaxDepth).IsEqualTo(10u);
		await Assert.That(limit.FunctionRecursionLimit).IsEqualTo(50u);
		await Assert.That(limit.FunctionInvocationLimit).IsEqualTo(25000u);
		await Assert.That(limit.MaxNamedQRegisters).IsEqualTo(50u);
		await Assert.That(limit.QueueEntryCpuTime).IsEqualTo(LimitOptions.DefaultQueueEntryCpuTime);
		await Assert.That(limit.CallLimit).IsEqualTo(1000u);
	}
}
