using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A portal package operation run as a queue entry: it ran and answered, or it never ran.
/// </summary>
public union PackageOperationOutcome<T>(PackageOperationRan<T>, PackageOperationRefused);

/// <summary>
/// The operation ran and <paramref name="Result"/> is its answer, success or failure.
/// <paramref name="Backup"/> is the copy of the world taken just before it, when one was.
/// </summary>
public readonly record struct PackageOperationRan<T>(T Result, WorldBackup? Backup);

/// <summary>
/// The operation never ran and nothing was written: the queue refused or dropped it, or the backup
/// taken before it failed. <paramref name="Reason"/> says which, for whoever asked.
/// </summary>
public readonly record struct PackageOperationRefused(string Reason);
