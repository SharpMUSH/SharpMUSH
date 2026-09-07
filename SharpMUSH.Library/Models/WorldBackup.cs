namespace SharpMUSH.Library.Models;

/// <summary>
/// One completed hot copy of the world, sitting in the backup root as a self-contained directory a
/// snapshot tool can read while the game keeps running.
/// </summary>
/// <param name="Name">The directory's name, a UTC timestamp that sorts chronologically.</param>
/// <param name="Path">Full path to the copy.</param>
/// <param name="CreatedAt">When the copy was taken, read back from <paramref name="Name"/>.</param>
/// <param name="SizeBytes">Total size of the copy on disk.</param>
public sealed record WorldBackup(string Name, string Path, DateTimeOffset CreatedAt, long SizeBytes);
