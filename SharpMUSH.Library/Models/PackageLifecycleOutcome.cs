namespace SharpMUSH.Library.Models.Packages;

/// <summary>Closed local execution metadata; Parsed is not lifecycle settlement or a success certificate.</summary>
public enum PackageLifecycleOutcome
{
	Unknown, Deferred, InvalidTarget, MissingExecutor, MissingTarget, Absent, Empty,
	AttributeUnavailable, NoVerdict, Parsed, ParseFailure, CommandFailure, Failed, Cancelled, ExecutionLimit
}
