using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for matching listen patterns on objects.
/// </summary>
public interface IListenPatternMatcher
{
	/// <summary>
	/// Match a message against listen patterns on an object.
	/// Returns matched patterns with captured groups.
	/// </summary>
	ValueTask<ListenMatch[]> MatchListenPatternsAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker,
		bool checkParents = false);
}

/// <summary>
/// Result of a listen pattern match.
/// </summary>
public record ListenMatch(
	SharpAttribute Attribute,
	string[] CapturedGroups,
	ListenBehavior Behavior
);

/// <summary>
/// How a listen pattern should trigger based on speaker.
/// </summary>
public enum ListenBehavior
{
	/// <summary>
	/// Default - triggers for others speaking (like @ahear)
	/// </summary>
	AHear,

	/// <summary>
	/// Triggers for anyone speaking (attribute has AAHEAR flag, like @aahear)
	/// </summary>
	AAHear,

	/// <summary>
	/// Triggers only when object speaks (attribute has AMHEAR flag, like @amhear)
	/// </summary>
	AMHear
}
