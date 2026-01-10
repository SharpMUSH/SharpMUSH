using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for matching listen patterns on objects.
/// </summary>
/// <remarks>
/// TODO: Complete implementation with proper API usage
/// - Fix DBRef comparison (need to use .Object() extension)
/// - Use proper async enumeration for cached attributes
/// - Handle parent checking with LISTEN_PARENT flag
/// </remarks>
public class ListenPatternMatcher(IMediator mediator) : IListenPatternMatcher
{
	public async ValueTask<ListenMatch[]> MatchListenPatternsAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker,
		bool checkParents = false)
	{
		var matches = new List<ListenMatch>();
		
		// Get cached listen attributes for this object
		var listenAttributes = await mediator.Send(new GetListenAttributesQuery(listener));
		
		foreach (var listenAttr in listenAttributes)
		{
			// Check if this pattern should trigger based on speaker
			var isSelf = listener.Object().DBRef == speaker.Object().DBRef;
			var shouldTrigger = listenAttr.Behavior switch
			{
				ListenBehavior.AHear => !isSelf,   // Only others
				ListenBehavior.AAHear => true,      // Anyone
				ListenBehavior.AMHear => isSelf,    // Only self
				_ => false
			};
			
			if (!shouldTrigger)
				continue;
			
			// Try to match the pattern
			var regexMatch = listenAttr.CompiledRegex.Match(message);
			if (!regexMatch.Success)
				continue;
			
			// Extract captured groups
			var capturedGroups = new string[regexMatch.Groups.Count];
			for (int i = 0; i < regexMatch.Groups.Count; i++)
			{
				capturedGroups[i] = regexMatch.Groups[i].Value;
			}
			
			matches.Add(new ListenMatch(
				listenAttr.Attribute,
				capturedGroups,
				listenAttr.Behavior
			));
		}
		
		// TODO: Add parent checking when checkParents is true and LISTEN_PARENT flag is set
		
		return [.. matches];
	}
}
