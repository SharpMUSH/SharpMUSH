using SharpMUSH.Configuration;

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
}
