using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Service for matching listen patterns on objects.
/// </summary>
/// <remarks>
/// Implements listen pattern matching for ^-prefixed attributes that trigger on speech.
/// Supports AHEAR (others only), AAHEAR (anyone), and AMHEAR (self only) behaviors.
/// Can optionally check parent objects with LISTEN_PARENT flag.
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

		// Check parent objects if requested and LISTEN_PARENT flag is set
		if (checkParents)
		{
			var currentObject = listener;
			var visitedObjects = new HashSet<int> { currentObject.Object().DBRef.Number };
			const int maxParentDepth = 10; // Prevent infinite loops
			var depth = 0;

			while (depth < maxParentDepth)
			{
				var parentAsync = await currentObject.Object().Parent.WithCancellation(CancellationToken.None);
				if (parentAsync.IsNone)
					break;

				var parent = parentAsync.Known;
				var parentObject = parent.Object();

				// Prevent infinite loops from circular parent relationships
				if (visitedObjects.Contains(parentObject.DBRef.Number))
					break;
				visitedObjects.Add(parentObject.DBRef.Number);

				// Check if parent has LISTEN_PARENT flag
				var hasListenParent = await parentObject.Flags.Value.AnyAsync(f => f.Name == "LISTEN_PARENT");
				if (!hasListenParent)
					break;

				// Get listen attributes from parent
				var parentListenAttributes = await mediator.Send(new GetListenAttributesQuery(parent));

				foreach (var listenAttr in parentListenAttributes)
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

				// Move to next parent
				currentObject = parent;
				depth++;
			}
		}

		return [.. matches];
	}
}
