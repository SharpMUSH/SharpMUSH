namespace SharpMUSH.Tests;

/// <summary>A test world's directory and everything the server keeps beside it.</summary>
public static class TestWorldFiles
{
	public static void Delete(string world)
	{
		foreach (var directory in new[] { world, world + ".backups", world + ".dataprotection-keys", world + ".imports" }.Where(Directory.Exists))
			Directory.Delete(directory, recursive: true);
		File.Delete(world + ".engine.lock");
	}
}
