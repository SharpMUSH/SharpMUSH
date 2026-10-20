namespace SharpMUSH.Library.Models;

/// <summary>One attribute read/parse verdict. Parsed is not proof of business success or joined descendants.</summary>
public enum ObjectAttributeRunStatus
{
	Unknown, Absent, Empty, Unavailable, NoVerdict, Parsed, ParseFailure, CommandFailure
}
