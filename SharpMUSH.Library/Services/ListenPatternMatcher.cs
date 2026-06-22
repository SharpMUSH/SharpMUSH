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
/// Can optionally check parent objects with LISTEN_PARENT flag, then the type ancestor
/// (PennMUSH ANCESTOR_*) and the ancestor's own LISTEN_PARENT chain.
/// </remarks>
public class ListenPatternMatcher(
	IMediator mediator,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration) : IListenPatternMatcher
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
		CollectMatches(listenAttributes, listener, message, speaker, matches);

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
				CollectMatches(parentListenAttributes, listener, message, speaker, matches);

				// Move to next parent
				currentObject = parent;
				depth++;
			}

			// PennMUSH ancestor fall-through: after the object's own @parent chain, consult the
			// type ancestor and its own LISTEN_PARENT chain (but no ancestor-of-ancestor). Skip
			// when disabled, missing, or when the object IS its own type ancestor (no self-loop).
			var ancestorRef = await listener.Ancestor(configuration);
			if (ancestorRef is not null && ancestorRef.Value.Number != listener.Object().DBRef.Number
			    && !visitedObjects.Contains(ancestorRef.Value.Number))
			{
				var ancestorNode = await mediator.Send(new GetObjectNodeQuery(ancestorRef.Value));
				if (!ancestorNode.IsNone)
				{
					var ancestor = ancestorNode.Known;
					visitedObjects.Add(ancestor.Object().DBRef.Number);

					var ancestorListenAttributes = await mediator.Send(new GetListenAttributesQuery(ancestor));
					CollectMatches(ancestorListenAttributes, listener, message, speaker, matches);

					// Honor the ancestor's own LISTEN_PARENT chain, then stop.
					var ancestorCurrent = ancestor;
					var ancestorDepth = 0;
					while (ancestorDepth < maxParentDepth)
					{
						var apAsync = await ancestorCurrent.Object().Parent.WithCancellation(CancellationToken.None);
						if (apAsync.IsNone)
							break;

						var ap = apAsync.Known;
						var apObject = ap.Object();
						if (visitedObjects.Contains(apObject.DBRef.Number))
							break;
						visitedObjects.Add(apObject.DBRef.Number);

						var apHasListenParent = await apObject.Flags.Value.AnyAsync(f => f.Name == "LISTEN_PARENT");
						if (!apHasListenParent)
							break;

						var apListenAttributes = await mediator.Send(new GetListenAttributesQuery(ap));
						CollectMatches(apListenAttributes, listener, message, speaker, matches);

						ancestorCurrent = ap;
						ancestorDepth++;
					}
				}
			}
		}

		return [.. matches];
	}

	/// <summary>
	/// Evaluate a set of listen attributes against the message and append any matches. The self/other
	/// trigger gate is computed relative to the original <paramref name="listener"/>.
	/// </summary>
	private static void CollectMatches(
		IEnumerable<ListenAttributeCache> listenAttributes,
		AnySharpObject listener,
		string message,
		AnySharpObject speaker,
		List<ListenMatch> matches)
	{
		var isSelf = listener.Object().DBRef == speaker.Object().DBRef;

		foreach (var listenAttr in listenAttributes)
		{
			var shouldTrigger = listenAttr.Behavior switch
			{
				ListenBehavior.AHear => !isSelf,   // Only others
				ListenBehavior.AAHear => true,      // Anyone
				ListenBehavior.AMHear => isSelf,    // Only self
				_ => false
			};

			if (!shouldTrigger)
				continue;

			var regexMatch = listenAttr.CompiledRegex.Match(message);
			if (!regexMatch.Success)
				continue;

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
	}
}
