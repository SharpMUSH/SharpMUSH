namespace SharpMUSH.Tests.Database;

internal static class FixtureDirectoryCleanup
{
	/// <summary>Retries transient LMDB release races and surfaces persistent cleanup failures.</summary>
	internal static async Task DeleteAsync(string path)
	{
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
				return;
			}
			catch (IOException) when (attempt < 4)
			{
				await Task.Delay(25);
			}
		}
	}
}
