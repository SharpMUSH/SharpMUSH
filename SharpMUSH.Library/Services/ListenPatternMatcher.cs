using Mediator;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

/// <summary>Matches visible listen patterns while retaining the original listener's self/other identity.</summary>
/// <remarks>
/// Callers opt into parent traversal; notification routing uses the original listener's LISTEN_PARENT.
/// Each phase visits at most Limit.MaxParents parents. A separately configured type-ancestor phase is
/// a SharpMUSH extension and shares shadow masks and visited identities with the ordinary parent phase.
/// </remarks>
public class ListenPatternMatcher(
	IMediator mediator,
	IOptionsWrapper<SharpMUSH.Configuration.Options.SharpMUSHOptions> configuration) : IListenPatternMatcher
{
	public ValueTask<ListenMatch[]> MatchListenPatternsAsync(
		AnySharpObject listener,
		string message,
		AnySharpObject speaker,
		bool checkParents = false)
		=> MatchListenPatternsAsync(listener, MString.Plain(message), speaker, checkParents);

	public async ValueTask<ListenMatch[]> MatchListenPatternsAsync(AnySharpObject listener, MString message,
		AnySharpObject speaker, bool checkParents = false)
	{
		var matches = new List<ListenMatch>();
		var search = new ListenAttributeSearch();
		var token = ExecutionBudget.CurrentToken;
		var maxParents = checkParents ? configuration.CurrentValue.Limit.MaxParents : 0;
		CollectMatches(await search.ReadPhaseAsync(mediator, listener.Object().DBRef, maxParents, false, token),
			listener, message, speaker, matches);
		if (checkParents && await listener.Ancestor(configuration) is { } ancestor)
			CollectMatches(await search.ReadPhaseAsync(mediator, ancestor, maxParents, true, token),
				listener, message, speaker, matches);

		return [.. matches];
	}

	/// <summary>
	/// Evaluate a set of listen attributes against the message and append any matches. The self/other
	/// trigger gate is computed relative to the original <paramref name="listener"/>.
	/// </summary>
	private static void CollectMatches(
		IEnumerable<ListenAttributeCache> listenAttributes,
		AnySharpObject listener,
		MString message,
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

			// A pattern that cannot finish is not a match, and must not stop the patterns after it.
			var regexMatch = SoftcodeRegex.Match(listenAttr.CompiledRegex, message.ToPlainText());
			if (regexMatch is not { Success: true })
				continue;

			var arguments = PatternArguments.Capture(listenAttr.CompiledRegex, regexMatch, listenAttr.IsRegexFlag, message);
			matches.Add(new ListenMatch(
				listenAttr.Attribute,
				PatternArguments.Groups(listenAttr.CompiledRegex, regexMatch, listenAttr.IsRegexFlag)
					.Skip(listenAttr.IsRegexFlag ? 0 : 1).Select(group => group.Value).ToArray(),
				listenAttr.Behavior
			)
			{ Arguments = arguments });
		}
	}
}
