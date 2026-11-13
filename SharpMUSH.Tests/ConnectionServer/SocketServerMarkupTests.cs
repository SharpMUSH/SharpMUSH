using System.Reflection;
using System.Runtime.Loader;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// The socket server installs its own markup layers. This test assembly configures
/// <c>MarkupRegistry.Default</c> before any test runs, so the socket server is loaded again, with its own copy of
/// MarkupString, in a context where nothing has: there, building the host has to be what configures it.
/// </summary>
public class SocketServerMarkupTests
{
	// The session's broker: building the host connects to it, and nothing here consumes a stream.
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer Broker { get; init; }

	[Test]
	public async Task HostConfiguresMarkupForTheLifecycleNotices()
	{
		var context = new IsolatedContext(AppContext.BaseDirectory);
		try
		{
			var registry = context.LoadFromAssemblyName(new AssemblyName("MarkupString")).GetType("MarkupString.MarkupRegistry", true)!;
			bool IsConfigured() => (bool)registry.GetProperty("IsConfigured")!.GetValue(null)!;
			await Assert.That(IsConfigured()).IsFalse();

			var socketServer = context.LoadFromAssemblyName(new AssemblyName("SharpMUSH.SocketServer"));
			var create = socketServer.GetType("SharpMUSH.SocketServer.Program", true)!.GetMethod("CreateHostBuilderAsync")!;
			var building = (Task)create.Invoke(null, [
				TestDiagnostics.HostArguments, $"nats://localhost:{Broker.Instance.GetMappedPublicPort(4222)}", null])!;
			await building;
			await using var app = (IAsyncDisposable)building.GetType().GetProperty("Result")!.GetValue(building)!;

			await Assert.That(IsConfigured()).IsTrue();

			// What the service sends every socket on a restart: serialising it threw while nothing was configured.
			var notices = socketServer.GetType("SharpMUSH.SocketServer.Services.EngineLifecycleNoticeService", true)!;
			var notice = notices.GetMethod("Notice")!.Invoke(null, [false])!;
			var serializer = registry.Assembly.GetType("MarkupString.MarkupTextSerializer", true)!
				.GetMethod("Serialize", [notice.GetType()])!;
			var serialized = (string)serializer.Invoke(null, [notice])!;
			await Assert.That(serialized).Contains("The game is restarting.");
		}
		finally
		{
			context.Unload();
		}
	}

	/// <summary>Loads MarkupString and SharpMUSH assemblies afresh; everything else is shared with the test run.</summary>
	private sealed class IsolatedContext(string directory) : AssemblyLoadContext(isCollectible: true)
	{
		protected override Assembly? Load(AssemblyName name)
		{
			if (name.Name is not { } simple || !(simple.StartsWith("MarkupString") || simple.StartsWith("SharpMUSH."))) return null;
			var path = Path.Join(directory, simple + ".dll");
			return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
		}
	}
}
