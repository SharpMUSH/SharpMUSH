namespace SharpMUSH.Library.Models.RecurringJobs;

/// <summary>
/// A recurring job. One an account creates runs as its <paramref name="Character"/>; one a package
/// declares (<paramref name="Package"/> set) has no owning account and runs as its target object,
/// as <c>@trigger</c> would, until the package drops it or is uninstalled.
/// </summary>
/// <param name="Package">The package that declared it, or null for an account's own job.</param>
/// <param name="PackageRef">Its name within that package.</param>
public sealed record RecurringJob(
	string Id, string OwnerAccount, string Character, string Target, string Attribute,
	string Schedule, string TimeZone, string Description, bool Enabled, long Revision,
	long? NextRun, long? LastRun, string? LastError, string Status, string? RunToken,
	string? Package = null, string? PackageRef = null);

/// <summary>A job as a package declares it, with its target resolved to an object identity (<c>#n:created</c>).</summary>
public sealed record PackageJobDefinition(string Ref, string Target, string Attribute, string Schedule, string TimeZone, string Description);

public sealed record RecurringJobRequest(string Target, string Attribute, string Schedule, string TimeZone, string Description = "");
public sealed record RecurringJobDocument(RecurringJob[] Jobs);
