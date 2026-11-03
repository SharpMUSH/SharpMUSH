using System.Globalization;
using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Services;

/// <summary>
/// RhostMUSH's live-wired function invocation limit (<c>@livewire/funceval</c>, <c>src/wiz.c</c>
/// <c>do_livewire</c>): a number of function calls one object's code may make in a queue entry, in place of
/// <c>function_invocation_limit</c>. A lower number always applies; a higher one only with the override on.
/// </summary>
/// <param name="Limit">The object's limit; 0 is none.</param>
/// <param name="Override">Whether <paramref name="Limit"/> may exceed the game's limit.</param>
public readonly record struct LiveWire(uint Limit, bool Override)
{
	/// <summary>The attribute that holds it, RhostMUSH's <c>*WireFuncEval</c>, as <c>&lt;limit&gt; &lt;override&gt;</c>.</summary>
	public const string AttributeName = "WIREFUNCEVAL";

	/// <summary>
	/// Its flags, as the world's attribute entry seeds them: <c>internal</c>, so only the engine writes it and
	/// softcode never sees it, as RhostMUSH's <c>AF_INTERNAL | AF_GOD</c>.
	/// </summary>
	public static readonly IReadOnlyList<string> AttributeFlags = ["no_command", "no_clone", "internal"];

	/// <summary>The highest limit RhostMUSH accepts.</summary>
	public const uint MaximumLimit = 1_000_000_000;

	/// <summary>No limit of the object's own.</summary>
	public static LiveWire None => default;

	/// <summary>
	/// Reads the stored text as RhostMUSH does (<c>sscanf("%d %d")</c> in <c>src/game.c</c>): two numbers, or
	/// nothing at all.
	/// </summary>
	public static LiveWire Parse(string? text)
	{
		var parts = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		return parts.Length == 2
			&& int.TryParse(parts[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var limit)
			&& int.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var overriding)
				? new LiveWire(Math.Min((uint)Math.Max(limit, 0), MaximumLimit), overriding != 0)
				: None;
	}

	/// <summary>The stored text: <c>&lt;limit&gt; &lt;override&gt;</c>.</summary>
	public override string ToString()
		=> string.Create(CultureInfo.InvariantCulture, $"{Limit} {(Override ? 1 : 0)}");

	/// <summary>Whether this limit replaces <paramref name="gameLimit"/>: a lower one always, a higher one with the override.</summary>
	public bool Replaces(uint gameLimit)
		=> Limit != 0 && (Limit < gameLimit || (Limit > gameLimit && Override));

	/// <summary>The object's own setting, or <see cref="None"/>.</summary>
	public static async ValueTask<LiveWire> ReadAsync(IMediator mediator, DBRef target, CancellationToken cancellationToken)
	{
		var stored = await mediator.CreateStream(new GetAttributeQuery(target, [AttributeName]), cancellationToken)
			.LastOrDefaultAsync(cancellationToken);
		// Only the engine's own copy counts: one written as an ordinary attribute, before the world's entry
		// marked it internal, is anyone's to set.
		return stored is not null && stored.IsInternal() ? Parse(stored.Value.ToPlainText()) : None;
	}

	/// <summary>
	/// The function invocation limit for code <paramref name="executor"/> runs, as RhostMUSH's <c>exec()</c>
	/// chooses it (<c>src/eval.c</c>): the object's own setting when it has one, else its owner's, and the game's
	/// <paramref name="gameLimit"/> when the one found does not replace it.
	/// </summary>
	public static async ValueTask<uint> LimitForAsync(IMediator mediator, DBRef executor, uint gameLimit,
		CancellationToken cancellationToken)
	{
		var own = await ReadAsync(mediator, executor, cancellationToken);
		if (own.Limit != 0)
		{
			return own.Replaces(gameLimit) ? own.Limit : gameLimit;
		}

		if (await mediator.Send(new GetObjectNodeQuery(executor), cancellationToken) is not AnySharpObject found)
		{
			return gameLimit;
		}

		var owner = (await found.Object().Owner.WithCancellation(cancellationToken)).Object.DBRef;
		if (owner.Number == executor.Number)
		{
			return gameLimit;
		}

		var owners = await ReadAsync(mediator, owner, cancellationToken);
		return owners.Replaces(gameLimit) ? owners.Limit : gameLimit;
	}
}
