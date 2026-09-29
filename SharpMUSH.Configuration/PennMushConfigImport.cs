using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Configuration;

/// <summary>
/// A PennMUSH configuration read into SharpMUSH's, and the lines of it that were not carried over,
/// each with the reason: an <c>include</c> that could not be read, a restriction with no value, one a
/// later line replaced, or a <c>restrict_*</c> directive SharpMUSH has nothing for.
/// </summary>
public record PennMushConfigImport(SharpMUSHOptions Options, IReadOnlyList<string> Skipped);
