namespace SharpMUSH.Tests.Integration;

/// <summary>
/// A test host's scratch directory, deleted when the host's services are disposed. Disposing a
/// <c>WebApplication</c> does not stop it, so <c>ApplicationStopped</c> never fires for a host a test
/// only disposes; the service container is disposed either way.
/// </summary>
internal sealed class FixtureDirectory(string path) : IDisposable
{
	public string Path { get; } = path;

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort: the directory is only scratch.
		}
	}
}
