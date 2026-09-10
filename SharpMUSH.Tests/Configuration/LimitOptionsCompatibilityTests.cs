using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Generated;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

public class LimitOptionsCompatibilityTests
{
	// Exact positional contract published at 4dc394dec, before GlobalQueueLimit.
	private static readonly (string Name, Type Type)[] PublishedParameters =
	[
		("MaxAliases", typeof(uint)),
		("MaxDbReference", typeof(uint?)),
		("MaxAttributesPerObj", typeof(uint)),
		("MaxLogins", typeof(uint)),
		("MaxGuests", typeof(int)),
		("MaxNamedQRegisters", typeof(uint)),
		("ConnectFailLimit", typeof(uint)),
		("IdleTimeout", typeof(uint)),
		("UnconnectedIdleTimeout", typeof(uint)),
		("KeepaliveTimeout", typeof(uint)),
		("WhisperLoudness", typeof(uint)),
		("StartingQuota", typeof(uint)),
		("StartingMoney", typeof(uint)),
		("Paycheck", typeof(uint)),
		("GuestPaycheck", typeof(uint)),
		("MaxPennies", typeof(uint)),
		("MaxGuestPennies", typeof(uint)),
		("MaxParents", typeof(uint)),
		("MailLimit", typeof(uint)),
		("MaxDepth", typeof(uint)),
		("PlayerQueueLimit", typeof(uint)),
		("QueueLoss", typeof(uint)),
		("QueueChunk", typeof(uint)),
		("FunctionRecursionLimit", typeof(uint)),
		("FunctionInvocationLimit", typeof(uint)),
		("CallLimit", typeof(uint)),
		("PlayerNameLen", typeof(uint)),
		("QueueEntryCpuTime", typeof(uint)),
		("UseQuota", typeof(bool)),
		("ChunkMigrate", typeof(uint)),
		("MaxAttributeValueLength", typeof(uint))
	];

	private static SharpMUSHOptions Options() => ReadPennMushConfig.Create(
		Path.Combine(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));

	[Test]
	public async Task PublishedLimitConstructorRemainsCallable()
	{
		var constructor = typeof(LimitOptions).GetConstructor(PublishedParameters.Select(x => x.Type).ToArray());
		await Assert.That(constructor).IsNotNull();
		var original = Options().Limit;
		var arguments = PublishedParameters.Select(x => typeof(LimitOptions).GetProperty(x.Name)!.GetValue(original)).ToArray();
		var result = (LimitOptions)constructor!.Invoke(arguments);
		for (var i = 0; i < arguments.Length; i++)
			await Assert.That(typeof(LimitOptions).GetProperty(PublishedParameters[i].Name)!.GetValue(result)).IsEqualTo(arguments[i]);
		await Assert.That(result.GlobalQueueLimit).IsEqualTo(10000u);
	}

	[Test]
	public async Task PublishedLimitDeconstructionRemainsCallable()
	{
		var deconstruct = typeof(LimitOptions).GetMethod("Deconstruct", PublishedParameters.Select(x => x.Type.MakeByRefType()).ToArray());
		await Assert.That(deconstruct).IsNotNull();
		var original = Options().Limit;
		var values = new object?[PublishedParameters.Length];
		deconstruct!.Invoke(original, values);
		for (var i = 0; i < values.Length; i++)
			await Assert.That(values[i]).IsEqualTo(typeof(LimitOptions).GetProperty(PublishedParameters[i].Name)!.GetValue(original));
	}

	[Test]
	public async Task NonPositionalQueueLimitRetainsConfigurationBindingAndDeclaredDefault()
	{
		await Assert.That(Options().Limit.GlobalQueueLimit).IsEqualTo(10000u);
		await Assert.That(ConfigAccessor.GetDeclaredDefault(nameof(LimitOptions.GlobalQueueLimit))).IsEqualTo((object)10000u);
		await Assert.That(ConfigMetadata.PropertyToAttributeName[nameof(LimitOptions.GlobalQueueLimit)]).IsEqualTo("global_queue_limit");
		await Assert.That(ConfigMetadata.PropertyMetadata[nameof(LimitOptions.GlobalQueueLimit)].Min).IsEqualTo((object)1);
		var path = Path.GetTempFileName();
		try
		{
			await File.WriteAllTextAsync(path, "global_queue_limit 321\n");
			var parsed = ReadPennMushConfig.Create(path);
			await Assert.That(parsed.Limit.GlobalQueueLimit).IsEqualTo(321u);
			await Assert.That(ConfigAccessor.GetValue(parsed, nameof(LimitOptions.GlobalQueueLimit))).IsEqualTo((object)321u);
			var updated = ConfigAccessor.WithValue(parsed, nameof(LimitOptions.GlobalQueueLimit), 456u);
			await Assert.That(updated.Limit.GlobalQueueLimit).IsEqualTo(456u);
		}
		finally { File.Delete(path); }
	}
}
