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
/// The walk visits at most Limit.MaxParents parents and never the type ancestor: atr_comm_match passes
/// a NULL <c>use_ancestor</c> to <c>next_parent</c> (<c>src/attrib.c:1923</c>), and Penn's help says
/// ancestors are not checked for ^-commands (<c>penntop.hlp:279</c>).
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
		// Every pattern is matched against the same plain text; it is rendered once, not per attribute.
		var plainMessage = message.ToPlainText();
		CollectMatches(await search.ReadPhaseAsync(mediator, listener.Object().DBRef, maxParents, token),
			listener, message, plainMessage, speaker, matches);

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
		string plainMessage,
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
			var regexMatch = SoftcodeRegex.Match(listenAttr.CompiledRegex, plainMessage);
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
