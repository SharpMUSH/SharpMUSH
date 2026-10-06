using System.Collections.Concurrent;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Dfa;

/// <summary>
/// Adaptive prediction for <see cref="SharpMUSHParser"/> that settles the grammar's semantic
/// predicates where a decision starts, instead of after the lookahead runs into a conflict.
/// </summary>
/// <remarks>
/// <para>
/// Whether a <c>,</c>, <c>)</c>, <c>;</c>, <c>=</c> or <c>&gt;</c> is text or ends an argument is
/// decided only by the predicates in <c>beginGenericText</c> (and the two <c>inBraceDepth == 0</c>
/// guards). ANTLR keeps such a predicate on the alternatives it gates and evaluates it only once
/// the lookahead has reached a conflict between alternatives. Without the predicates, the comma
/// after <c>A</c> in <c>if(c,A,[...])</c> is as much text as a separator, and both readings stay
/// viable to the end of the call, so the prediction walks the whole bracket with every inner
/// <c>,</c> and <c>)</c> forked the same way. The stacks it tracks multiply with each nested call
/// in the bracket, and ANTLR merges and compares them without sharing work: a few hundred
/// characters took minutes, in SLL and LL alike.
/// </para>
/// <para>
/// The predicates read only parser fields, never the rule context, and ANTLR evaluates them
/// against the parser as it stands at the decision either way (it seeks back to the decision's
/// first token before evaluating). So each one has a single value for the whole prediction, known
/// before it starts. This simulator evaluates them all, picks a DFA cache kept for that
/// combination of values, and drops the start configurations whose predicate is false and the
/// predicate of the rest. Every alternative that survives is one ANTLR would have accepted; the
/// comma above stops being text at the first token, and prediction is over there.
/// </para>
/// <para>
/// Only the start state is resolved. Predicates met after consuming input were never part of
/// prediction and still are not, and the full-context (LL) retry computes its own start state
/// as before. The caches are process-wide, like the generated parser's own, one set per
/// combination of predicate values seen.
/// </para>
/// </remarks>
public sealed class PredicateResolvingSimulator : ParserATNSimulator
{
	/// <summary>Every predicate in the grammar, as the (rule, predicate) pair <c>Sempred</c> takes.</summary>
	private static readonly (int Rule, int Predicate)[] Predicates = SharpMUSHParser._ATN.states
		.Where(state => state is not null)
		.SelectMany(state => state.TransitionsArray)
		.OfType<PredicateTransition>()
		.Select(transition => (transition.ruleIndex, transition.predIndex))
		.Distinct()
		.Order()
		.ToArray();

	private static readonly ConcurrentDictionary<int, DFA[]> CachesByOutcome = new();

	private static readonly PredictionContextCache ContextCache = new();

	private readonly SharpMUSHParser _parser;

	// One simulator per combination this parser has met, each over that combination's caches.
	private readonly Dictionary<int, ResolvedStartSimulator> _simulators = [];

	public PredicateResolvingSimulator(SharpMUSHParser parser)
		: base(parser, SharpMUSHParser._ATN, parser.Interpreter.decisionToDFA, ContextCache)
	{
		_parser = parser;
		if (Predicates.Length > 31)
		{
			throw new InvalidOperationException($"The grammar has {Predicates.Length} predicates; their outcomes no longer fit one int.");
		}
	}

	public override int AdaptivePredict(ITokenStream input, int decision, ParserRuleContext outerContext)
	{
		var outcome = 0;
		for (var i = 0; i < Predicates.Length; i++)
		{
			if (_parser.Sempred(null, Predicates[i].Rule, Predicates[i].Predicate))
			{
				outcome |= 1 << i;
			}
		}

		if (!_simulators.TryGetValue(outcome, out var simulator))
		{
			simulator = new ResolvedStartSimulator(_parser, CachesByOutcome.GetOrAdd(outcome, _ => NewCaches()));
			_simulators[outcome] = simulator;
		}

		simulator.PredictionMode = PredictionMode;
		return simulator.AdaptivePredict(input, decision, outerContext);
	}

	private static DFA[] NewCaches()
	{
		var atn = SharpMUSHParser._ATN;
		var caches = new DFA[atn.NumberOfDecisions];
		for (var decision = 0; decision < caches.Length; decision++)
		{
			caches[decision] = new DFA(atn.GetDecisionState(decision), decision);
		}

		return caches;
	}

	/// <summary>
	/// The plain ANTLR simulator over one combination's caches, whose start state for a decision
	/// is computed once with the predicates already applied.
	/// </summary>
	private sealed class ResolvedStartSimulator(SharpMUSHParser owner, DFA[] caches)
		: ParserATNSimulator(owner, SharpMUSHParser._ATN, caches, ContextCache)
	{
		public override int AdaptivePredict(ITokenStream input, int decision, ParserRuleContext outerContext)
		{
			var dfa = decisionToDFA[decision];
			if (dfa.s0 is null)
			{
				var start = ComputeStartState(dfa.atnStartState, ParserRuleContext.EmptyContext, false);
				var resolved = new ATNConfigSet(false);
				var merges = new MergeCache();
				foreach (var config in start.configs)
				{
					if (config.semanticContext == SemanticContext.Empty.Instance)
					{
						resolved.Add(config, merges);
					}
					else if (config.semanticContext.Eval(owner, outerContext))
					{
						resolved.Add(new ATNConfig(config, SemanticContext.Empty.Instance), merges);
					}
				}

				dfa.s0 = AddDFAState(dfa, new DFAState(resolved));
			}

			return base.AdaptivePredict(input, decision, outerContext);
		}
	}
}

public partial class SharpMUSHParser
{
	/// <summary>
	/// Predicts with <see cref="PredicateResolvingSimulator"/>. Call it before setting
	/// <see cref="ParserATNSimulator.PredictionMode"/>: the new simulator starts at LL.
	/// </summary>
	public void ResolvePredicatesAtDecisionStart() => Interpreter = new PredicateResolvingSimulator(this);
}
