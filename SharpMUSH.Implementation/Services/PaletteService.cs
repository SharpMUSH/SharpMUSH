using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.InputSessions;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Services;

/// <inheritdoc cref="IPaletteService"/>
/// <remarks>
/// An entry is offered when its object is in one of the character's command scopes and perceived, as
/// dispatch finds <c>$</c>-commands; the object answers commands at all and its command and use locks
/// pass; it is not another player's personal entry; its <c>attr</c> is a <c>$</c>-command on the object,
/// or, without one, a built-in first word may be run; and its own lock passes. Nothing is evaluated but
/// locks. Running the line checks everything again, since it runs as typed.
/// </remarks>
public sealed class PaletteService(
	IMediator mediator,
	ILockService locks,
	IRealityPolicy reality,
	ICommandPermission commands,
	IContextStack contexts,
	ISystemNames systems,
	IInputSessionService inputs,
	IAttributeService attributes,
	IOptionsWrapper<SharpMUSHOptions> configuration) : IPaletteService
{
	public async ValueTask<PaletteResponse> ResolveAsync(AnySharpObject character, Guid? inputSession = null,
		CancellationToken cancellationToken = default)
	{
		var (entries, input, dropped, invalid) = await CollectAsync(character, inputSession, cancellationToken);
		return new PaletteResponse(
			entries,
			input,
			await commands.RunnableAsync(character),
			await PinsAsync(character),
			Stack(character),
			dropped,
			invalid);
	}

	public async ValueTask<IReadOnlyList<PaletteSuggestion>> SuggestAsync(AnySharpObject character,
		CancellationToken cancellationToken = default)
	{
		// With nothing on the stack nothing rises, and the scopes need not be walked.
		var stack = Stack(character);
		return stack.Count == 0
			? []
			: PaletteSuggestions.Suggest((await CollectAsync(character, null, cancellationToken)).Entries, stack);
	}

	private IReadOnlyList<PaletteContextView> Stack(AnySharpObject character)
		=> [.. contexts.Read(character.Object().DBRef).Select(context => new PaletteContextView(
			context.Name, context.Context.Values, Math.Round(context.Weight, 4), context.Context.Info))];

	private async ValueTask<(List<PaletteEntryView> Entries, PaletteInputView? Input, int Dropped, List<string> Invalid)> CollectAsync(
		AnySharpObject character, Guid? inputSession, CancellationToken cancellationToken)
	{
		var self = character.Object().DBRef;
		var entries = new List<PaletteEntryView>();
		var invalid = new List<string>();
		var dropped = 0;

		PaletteInputView? input = null;
		if (inputSession is { } id && inputs.FindCapturing(id) is { } session && session.Character.SameObjectAs(self))
		{
			input = new PaletteInputView(id.ToString(), PlainExit(session.Exit));
			foreach (var target in session.Routes.Select(route => route.Target).DistinctBy(target => target.Number))
			{
				if (await mediator.Send(new GetObjectNodeQuery(target), cancellationToken) is not AnySharpObject holder) continue;
				var found = await mediator.Send(new GetPaletteEntriesQuery(holder), cancellationToken);
				var system = await systems.SystemOfAsync(holder.Object().DBRef, cancellationToken);
				foreach (var entry in found.Entries.Where(IsInput))
				{
					if (entry.Lock is { } entryLock && !await locks.Evaluate(entryLock, holder, character)) continue;
					entries.Add(View(entry, holder, PaletteScope.Input, system));
				}
			}
		}

		var perceive = reality is IRealityObservationProvider observations
			? await observations.ObserveAsync(self, cancellationToken)
			: (target, ct) => reality.CanPerceiveAsync(self, target, ct);
		var inventory = await InventoryAsync(character);
		var visited = new HashSet<int>();

		for (var scope = CommandScope.Nearby; scope <= CommandScope.MasterRoom; scope++)
		{
			if (await CommandScopes.CandidatesIn(mediator, scope, character, configuration)
					is not IAsyncEnumerable<AnySharpObject> candidates)
			{
				continue;
			}

			await foreach (var holder in candidates.WithCancellation(cancellationToken))
			{
				var reference = holder.Object().DBRef;
				if (!visited.Add(reference.Number)) continue;

				var found = await mediator.Send(new GetPaletteEntriesQuery(holder), cancellationToken);
				if (found.Entries.Count == 0 && found.Invalid.Count == 0) continue;
				invalid.AddRange(found.Invalid.Select(reason => $"{reference}/{reason}"));
				dropped += found.Dropped;

				// Someone else's personal entries are theirs. The player's own need not answer $-commands
				// (players are NO_COMMAND by default) unless an entry runs one of them.
				var isSelf = reference.Number == self.Number;
				if (holder.IsPlayer && !isSelf) continue;
				if (!isSelf && !await perceive(reference, cancellationToken)) continue;
				var answers = await CommandDiscoveryService.ListensForCommandsAsync(holder, cancellationToken)
					&& await locks.Evaluate(LockType.Command, holder, character)
					&& await locks.Evaluate(LockType.Use, holder, character);
				if (!answers && !isSelf) continue;

				var where = scope switch
				{
					CommandScope.Nearby when reference.Number == self.Number => PaletteScope.Self,
					CommandScope.Nearby when inventory.Contains(reference.Number) => PaletteScope.Inventory,
					CommandScope.Nearby or CommandScope.Location => PaletteScope.Room,
					CommandScope.LocationZone or CommandScope.PersonalZone => PaletteScope.Zone,
					_ => PaletteScope.Master
				};
				var system = await systems.SystemOfAsync(reference, cancellationToken);
				HashSet<string>? commandNames = null;

				foreach (var entry in found.Entries.Where(entry => !IsInput(entry)))
				{
					if (entries.Count >= PaletteEntry.MaxPerOpen)
					{
						dropped++;
						continue;
					}

					if (entry.Attribute is { } attribute)
					{
						if (!answers) continue;
						commandNames ??= [.. (await mediator.Send(new GetCommandAttributesQuery(holder), cancellationToken))
							.Select(command => command.Attribute.LongName.ToUpperInvariant())];
						if (!commandNames.Contains(attribute))
						{
							invalid.Add($"{reference}/{entry.Name}: {attribute} is not a $-command");
							continue;
						}
					}
					else if (FirstWord(entry.Line) is { } word && commands.IsBuiltin(word)
						&& !await commands.CanRunAsync(character, word))
					{
						continue;
					}

					if (entry.Lock is { } entryLock && !await locks.Evaluate(entryLock, holder, character)) continue;
					entries.Add(View(entry, holder, where, system));
				}
			}
		}

		return (entries, input, dropped, invalid);
	}

	private static bool IsInput(PaletteEntry entry)
		=> entry.Context == PaletteSuggestions.InputContext;

	private static PaletteEntryView View(PaletteEntry entry, AnySharpObject holder, PaletteScope scope, string system)
		=> new(
			$"{holder.Object().DBRef}/{entry.Name}",
			entry.Label,
			entry.Line,
			entry.Action,
			scope,
			system,
			holder.Object().Name,
			entry.Context,
			entry.Group,
			entry.Info,
			entry.Icon,
			entry.Keywords,
			entry.Help,
			entry.Order,
			entry.Always,
			entry.When,
			entry.Urgent);

	/// <summary>The line's command word, up to a space, switch or <c>=</c>; null when it starts with a slot.</summary>
	private static string? FirstWord(string line)
	{
		var word = line.TrimStart().Split([' ', '/', '='], 2)[0];
		return word.Length == 0 || word.StartsWith('<') ? null : word;
	}

	/// <summary>The exit line, when the exit pattern is one plain word a player can type as it stands.</summary>
	private static string? PlainExit(InputRoute exit)
		=> !exit.IsRegex && !exit.IsAny && exit.Pattern.Length > 0
			&& exit.Pattern.All(c => !char.IsWhiteSpace(c) && c is not ('*' or '?' or '\\'))
			? exit.Pattern
			: null;

	private async ValueTask<HashSet<int>> InventoryAsync(AnySharpObject character)
	{
		var carried = new HashSet<int>();
		if (character.AsOptionalContainer is not AnySharpContainer container) return carried;
		await foreach (var item in container.Content(mediator))
			carried.Add(item.Object().DBRef.Number);
		return carried;
	}

	private async ValueTask<IReadOnlyList<string>> PinsAsync(AnySharpObject character)
		=> await attributes.GetAttributeAsync(character, character, PaletteEntry.PinsAttribute,
				IAttributeService.AttributeMode.Read, parent: false) is SharpAttribute[] { Length: > 0 } chain
			? chain.Last().Value.ToPlainText().Split(' ', StringSplitOptions.RemoveEmptyEntries)
			: [];
}
