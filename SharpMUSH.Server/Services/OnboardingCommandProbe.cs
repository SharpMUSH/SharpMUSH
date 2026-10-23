using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>Read-only discovery in dispatch order. Never evaluates or queues the suggested command.</summary>
public sealed class OnboardingCommandProbe(IMediator mediator, ICommandDiscoveryService discovery,
	IMUSHCodeParser parser, IRealityPolicy reality, IOptionsWrapper<SharpMUSHOptions> options, ICommandPermission commands,
	IHookService hooks, IAttributeService attributes, ILockService locks) : IOnboardingCommandProbe
{
	public async Task<IReadOnlyList<OnboardingCommandMatch>> MatchAsync(DBRef character, string command,
		CancellationToken cancellationToken)
	{
		// A built-in or registered alias wins before the softcode scopes. A command a package added with
		// @command/add runs the attribute its override hook names, so that attribute is the handler.
		var name = command.Split([' ', '/'], 2)[0];
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(3), cancellationToken);
		using var entered = budget.Enter();
		if (await mediator.Send(new GetObjectNodeQuery(character), cancellationToken) is not AnySharpObject player) return [];
		if (commands.IsBuiltin(name)) return await HookedAsync(name, command, player, cancellationToken);
		var perceive = reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(character, cancellationToken)
			: (target, ct) => reality.CanPerceiveAsync(character, target, ct);
		for (var scope = CommandScope.Nearby; scope <= CommandScope.MasterRoom; scope++)
		{
			budget.Token.ThrowIfCancellationRequested();
			if (await CommandScopes.CandidatesIn(mediator, scope, player, options)
				is not IAsyncEnumerable<AnySharpObject> candidates) continue;
			var matches = await discovery.MatchUserDefinedCommand(parser,
				candidates.Where((candidate, _) => perceive(candidate.Object().DBRef, budget.Token)),
				MString.Plain(command), player);
			if (!matches.TryGetValue(out var found)) continue;
			return found.Select(match => new OnboardingCommandMatch(match.SObject.Object().DBRef.ToString(),
				match.Attribute.LongName, match.Attribute.Value.ToPlainText())).ToArray();
		}
		return [];
	}

	/// <summary>The hook's attribute, judged the way dispatch judges it: the line must reach and match it.</summary>
	private async Task<IReadOnlyList<OnboardingCommandMatch>> HookedAsync(string name, string command, AnySharpObject player,
		CancellationToken cancellationToken)
	{
		if (await hooks.GetHookAsync(name.ToUpperInvariant(), "OVERRIDE") is not CommandHook hook
			|| await mediator.Send(new GetObjectNodeQuery(hook.TargetObject), cancellationToken) is not AnySharpObject target
			|| !(string.IsNullOrEmpty(hook.AttributeName)
				? await discovery.MatchUserDefinedCommand(parser, new[] { target }.ToAsyncEnumerable(), MString.Plain(command), player)
				: await HookedCommandMatch.MatchAsync(attributes, locks, target, player, hook.AttributeName, MString.Plain(command)))
				.TryGetValue(out var found)) return [];
		return found.Select(match => new OnboardingCommandMatch(match.SObject.Object().DBRef.ToString(),
			match.Attribute.LongName, match.Attribute.Value.ToPlainText())).ToArray();
	}
}
