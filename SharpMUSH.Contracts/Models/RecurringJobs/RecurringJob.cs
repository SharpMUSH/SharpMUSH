namespace SharpMUSH.Library.Models.RecurringJobs;

public sealed record RecurringJob(
	string Id, string OwnerAccount, string Character, string Target, string Attribute,
	string Schedule, string TimeZone, string Description, bool Enabled, long Revision,
	long? NextRun, long? LastRun, string? LastError, string Status, string? RunToken);

public sealed record RecurringJobRequest(string Target, string Attribute, string Schedule, string TimeZone, string Description = "");
public sealed record RecurringJobDocument(RecurringJob[] Jobs);
