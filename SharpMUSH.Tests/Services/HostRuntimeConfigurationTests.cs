using System.Text.Json;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// What the two hosts actually ask the runtime for, read from the <c>runtimeconfig.json</c> the build
/// produces rather than from the project file that is supposed to produce it.
/// </summary>
/// <remarks>
/// <c>ServerGarbageCollection</c> is set by <c>Microsoft.NET.Sdk.Web</c>, not by a
/// <c>FrameworkReference</c> to <c>Microsoft.AspNetCore.App</c>. SharpMUSH.Server uses the plain SDK
/// plus a framework reference, so it silently ran on the workstation collector while the telnet relay
/// — a thin byte pump on the web SDK — got the server one. The allocating process is the engine: it
/// builds an ANTLR parse tree, a markup graph and a set of cache entries per command.
/// <para>Both hosts' runtimeconfig.json land in this project's output because it references them.</para>
/// </remarks>
public class HostRuntimeConfigurationTests
{
	private static bool? ServerGarbageCollection(string host)
	{
		var path = Path.Join(AppContext.BaseDirectory, $"{host}.runtimeconfig.json");
		if (!File.Exists(path))
		{
			throw new FileNotFoundException(
				$"{host}.runtimeconfig.json is not in the test output; this test can no longer see what it checks.",
				path);
		}

		using var document = JsonDocument.Parse(File.ReadAllText(path));
		return document.RootElement
			.GetProperty("runtimeOptions")
			.TryGetProperty("configProperties", out var properties)
			&& properties.TryGetProperty("System.GC.Server", out var serverGc)
				? serverGc.GetBoolean()
				: null;
	}

	[Test]
	[Arguments("SharpMUSH.Server")]
	[Arguments("SharpMUSH.ConnectionServer")]
	public async Task TheHostAsksForTheServerGarbageCollector(string host)
		=> await Assert.That(ServerGarbageCollection(host)).IsTrue()
			.Because($"{host} is a long-lived server process, and the workstation collector is the default");
}
