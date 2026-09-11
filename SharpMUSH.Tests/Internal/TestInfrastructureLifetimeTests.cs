using System.Reflection;

namespace SharpMUSH.Tests.Internal;

public class TestInfrastructureLifetimeTests
{
	[Test]
	public async Task ConnectionFixtureDisposesOwnedFactoryThroughAsyncDisposable()
	{
		var host = new RecordingConnectionFactory();
		var fixture = new ConnectionServerWebAppFactory { DockerNetwork = null!, NatsTestServer = null! };
		typeof(ConnectionServerWebAppFactory).GetField("_server", BindingFlags.Instance | BindingFlags.NonPublic)!
			.SetValue(fixture, host);

		await ((IAsyncDisposable)fixture).DisposeAsync();

		await Assert.That(host.DisposalCount).IsEqualTo(1);
	}

	// No server or container is started: this tests ownership at the disposal boundary only.
	private sealed class RecordingConnectionFactory() :
		ConnectionServerTestWebApplicationBuilderFactory<SharpMUSH.ConnectionServer.Program>("unused")
	{
		public int DisposalCount { get; private set; }
		public override ValueTask DisposeAsync()
		{
			DisposalCount++;
			return ValueTask.CompletedTask;
		}
	}

	[Test]
	public async Task GeneratedNamesRemainDistinctUnderConcurrentCreation()
	{
		var names = new string[40_000];
		Parallel.For(0, names.Length, i => names[i] = TestIsolationHelpers.GenerateUniqueName("concurrent"));
		await Assert.That(names.Distinct(StringComparer.OrdinalIgnoreCase).Count()).IsEqualTo(names.Length);
		// Existing command-level callers need compact names, not a full GUID plus sequence.
		await Assert.That(names.Max(name => name.Length)).IsLessThanOrEqualTo(32);
	}
}
