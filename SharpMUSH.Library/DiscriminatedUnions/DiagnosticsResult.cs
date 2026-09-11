using SharpMUSH.Library.Models.Diagnostics;

namespace SharpMUSH.Library.DiscriminatedUnions;

/// <summary>
/// A queue-diagnostics answer, or why the actor could not have it.
/// </summary>
public partial union DiagnosticsResult<T>(T, DiagnosticsError);
