using SharpMUSH.Configuration;
using SharpMUSH.Library.API;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Services;

namespace SharpMUSH.Tests.Configuration;

public class NegotiationConfigurationTests
{
	[Test]
	public async Task GameSchemaAndGatewayDefaultToFourHundredMilliseconds()
	{
		var property = SchemaBuilder.BuildSchema().Properties["Net.NegotiationWaitMilliseconds"];
		await Assert.That(property.DefaultValue?.ToString()).IsEqualTo("400");
		await Assert.That(SharpMUSHOptions.Default().Net.NegotiationWaitMilliseconds).IsEqualTo(400u);
		await Assert.That(new OutputSettingsHolder().NegotiationWaitMilliseconds).IsEqualTo(400u);
	}

	[Test]
	[Arguments(0u)]
	[Arguments(750u)]
	public async Task ImportedGameOptionReachesTheGateway(uint milliseconds)
	{
		var path = Path.Join(Path.GetTempPath(), $"negotiation-{Guid.NewGuid():N}.cnf");
		try
		{
			await File.WriteAllTextAsync(path, $"negotiation_wait_ms {milliseconds}\n");
			var imported = ReadPennMushConfig.Import(path, followIncludes: false);
			await Assert.That(imported.Skipped).IsEmpty();
			await Assert.That(imported.Options.Net.NegotiationWaitMilliseconds).IsEqualTo(milliseconds);
			var holder = new OutputSettingsHolder();
			holder.Replace(new OutputSettingsMessage(new Dictionary<string, string>(),
				NegotiationWaitMilliseconds: imported.Options.Net.NegotiationWaitMilliseconds));
			await Assert.That(holder.NegotiationWaitMilliseconds).IsEqualTo(milliseconds);
		}
		finally
		{
			File.Delete(path);
		}
	}
}
