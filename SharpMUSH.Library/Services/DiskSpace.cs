namespace SharpMUSH.Library.Services;

/// <summary>
/// The two disk questions capacity reporting and the backup preflight ask: how much is free where a
/// path lives, and how much a directory holds. Both answer -1 rather than throw when the filesystem will
/// not say — a report with one unknown figure is still a report.
/// </summary>
public static class DiskSpace
{
	/// <summary>
	/// Bytes available to this process on the filesystem holding <paramref name="path"/>, which need not
	/// exist yet: the nearest existing ancestor is asked instead.
	/// </summary>
	public static long FreeBytes(string path)
	{
		try
		{
			var probe = Path.GetFullPath(path);
			while (!Directory.Exists(probe))
			{
				var parent = Path.GetDirectoryName(probe);
				if (string.IsNullOrEmpty(parent)) return -1;
				probe = parent;
			}

			return new DriveInfo(probe).AvailableFreeSpace;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
		{
			return -1;
		}
	}

	/// <summary>The total length of every file under <paramref name="directory"/>; 0 when it does not exist.</summary>
	public static long DirectoryBytes(string directory)
	{
		try
		{
			var info = new DirectoryInfo(directory);
			return info.Exists ? info.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return -1;
		}
	}
}
