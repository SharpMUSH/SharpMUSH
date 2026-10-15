using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mediator;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Server.Services;

/// <summary>
/// Imports a <see cref="RoomDocumentDto"/>: works out what it would change against the rooms as the actor sees them
/// now, and makes those changes as the actor. Every change goes through the game's own building commands
/// (<c>@dig</c>, <c>@name</c>, <c>@open</c>, <c>@link</c>, <c>@unlink</c>) or <see cref="IAttributeService"/>, so
/// quota, locks, link permissions and events are the game's. Nothing is destroyed: a room or exit the document
/// leaves out is left alone.
/// </summary>
public sealed partial class RoomDocumentImporter(
	IMediator mediator,
	IPermissionService permissions,
	IRealityPolicy reality,
	IAttributeService attributes,
	IEngineCommandInvoker commands,
	RoomTopologyReader reader)
{
	/// <summary>The longest name or description an import writes, in characters.</summary>
	public const int MaxText = 16_384;

	[GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]{0,63}$")]
	private static partial Regex NewKey();

	/// <summary>A dbref, nine digits at most so that it always fits an <see cref="int"/>.</summary>
	[GeneratedRegex("^#[0-9]{1,9}$")]
	private static partial Regex Dbref();

	/// <summary>What <c>@dig</c> and <c>@open</c> answer with: a dbref, with the object's creation time after a colon.</summary>
	[GeneratedRegex("^(#[0-9]{1,10})(:[0-9]+)?$")]
	private static partial Regex MadeDbref();

	private static string? Made(string? result)
		=> result is not null && MadeDbref().Match(result) is { Success: true } match ? match.Groups[1].Value : null;

	/// <summary>
	/// One change, with what it does once confirmed. A step reports why the game refused it, or null. <c>Was</c> is what
	/// the change replaces, so a confirmed plan stops matching once someone else changes that.
	/// </summary>
	private sealed record Step(string Action, string Target, string Summary, string Value, Func<Context, Task<string?>> Run, string Was = "");

	/// <summary>What the steps of one apply share: the actor, and the dbrefs of what earlier steps made.</summary>
	private sealed class Context(AnySharpObject actor)
	{
		public AnySharpObject Actor { get; } = actor;
		public Dictionary<string, string> Created { get; } = [];
		public string Resolve(string target) => Created.TryGetValue(target, out var made) ? made : target;
	}

	private sealed record Planned(List<Step> Steps, List<string> Problems, List<string> Notes)
	{
		public RoomPlanDto ToDto() => new(
			[.. Steps.Select(step => new RoomPlanStepDto(step.Action, step.Target, step.Summary))],
			Problems, Notes, Fingerprint(Steps));
	}

	public async Task<RoomPlanDto> PlanAsync(AnySharpObject actor, RoomDocumentDto document, CancellationToken ct)
		=> (await BuildAsync(actor, document, ct)).ToDto();

	/// <summary>
	/// Works the plan out again and, when it is still the one the builder confirmed and has no problems, makes each
	/// change in order, stopping at the first the game refuses.
	/// </summary>
	public async Task<Result<RoomApplyDto>> ApplyAsync(AnySharpObject actor, RoomDocumentDto document, string fingerprint,
		CancellationToken ct)
	{
		var planned = await BuildAsync(actor, document, ct);
		if (planned.Problems.Count > 0) return new Error<string>(planned.Problems[0]);
		if (Fingerprint(planned.Steps) != fingerprint)
			return new Error<string>("The rooms changed after these changes were worked out. Review them again.");

		var context = new Context(actor);
		var done = new List<string>();
		foreach (var step in planned.Steps)
		{
			ct.ThrowIfCancellationRequested();
			if (await step.Run(context) is { } refused)
				return new RoomApplyDto(done, $"{step.Summary}: {refused}", context.Created);
			done.Add(step.Summary);
		}
		return new RoomApplyDto(done, null, context.Created);
	}

	private static string Fingerprint(IEnumerable<Step> steps)
		=> Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
			string.Join('\n', steps.Select(step => $"{step.Action}\t{step.Target}\t{step.Value}\t{step.Was}")))));

	private async Task<Planned> BuildAsync(AnySharpObject actor, RoomDocumentDto document, CancellationToken ct)
	{
		var planned = new Planned([], [], []);
		var problems = planned.Problems;
		if (document.Format != RoomDocumentDto.FormatName || document.Version != RoomDocumentDto.CurrentVersion)
		{
			problems.Add($"This is not a version {RoomDocumentDto.CurrentVersion} room document (\"format\": \"{RoomDocumentDto.FormatName}\").");
			return planned;
		}
		var axes = document.Coordinates ?? RoomTopologyReader.DefaultCoordinates;
		if (!RoomTopologyReader.ValidCoordinates(axes)) problems.Add("\"coordinates\" must name three attributes.");
		var rooms = document.Rooms ?? [];
		if (rooms.Count is < 1 or > RoomTopologyReader.MaxRooms) problems.Add($"A document lists 1 to {RoomTopologyReader.MaxRooms} rooms.");
		if (rooms.Sum(room => room.Exits?.Count ?? 0) > RoomTopologyReader.MaxExits) problems.Add($"A document lists at most {RoomTopologyReader.MaxExits} exits.");
		if (problems.Count > 0) return planned;

		var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var id in rooms.Select(room => room.Id).Concat(rooms.SelectMany(room => room.Exits ?? []).Select(exit => exit.Id)))
		{
			if (string.IsNullOrWhiteSpace(id) || !(Dbref().IsMatch(id) || NewKey().IsMatch(id)))
				problems.Add($"\"{id}\" is neither a dbref such as #10 nor a name of your own such as shed.");
			else if (!ids.Add(id)) problems.Add($"{id} is listed more than once.");
		}
		if (problems.Count > 0) return planned;

		// The world as the actor sees it, read the same way an export is, so an unchanged export plans nothing.
		var existing = rooms.Where(room => Dbref().IsMatch(room.Id)).Select(room => Number(room.Id)).ToList();
		var current = new Dictionary<string, RoomDocumentRoomDto>();
		if (existing.Count > 0)
		{
			switch (await reader.ExportAsync(actor, existing, axes, ct))
			{
				case RoomDocumentDto now:
					foreach (var room in now.Rooms) current[room.Id] = room;
					break;
				case Error<string> error:
					problems.Add(error.Value);
					return planned;
			}
		}
		var newRooms = rooms.Where(room => NewKey().IsMatch(room.Id)).Select(room => room.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (var room in rooms.Where(room => newRooms.Contains(room.Id)))
			PlanNewRoom(room, axes, planned);
		foreach (var room in rooms.Where(room => !newRooms.Contains(room.Id)))
			await PlanRoomAsync(actor, room, current, axes, planned, ct);
		foreach (var room in rooms)
			await PlanExitsAsync(actor, room, current, newRooms, planned, ct);
		return planned;
	}

	private static int Number(string dbref) => int.Parse(dbref.AsSpan(1), CultureInfo.InvariantCulture);

	private static string Shown(string text) => text.Length <= 60 ? text : text[..57] + "...";

	private async Task<AnySharpObject?> ObjectAsync(string dbref, CancellationToken ct)
		=> await mediator.Send(new GetObjectNodeQuery(new DBRef(Number(dbref))), ct) is AnySharpObject found ? found : null;

	private void PlanNewRoom(RoomDocumentRoomDto room, IReadOnlyList<string> axes, Planned planned)
	{
		if (string.IsNullOrWhiteSpace(room.Name) || room.Name.Length > MaxText)
		{
			planned.Problems.Add($"The new room {room.Id} needs a name.");
			return;
		}
		var name = room.Name.Trim();
		planned.Steps.Add(new("dig", room.Id, $"Dig {name} ({room.Id})", name, async context =>
		{
			var result = await Command(context, "@DIG", name);
			if (Made(result) is not { } dbref) return result ?? "@dig made no room.";
			context.Created[room.Id] = dbref;
			return null;
		}));
		if (!string.IsNullOrEmpty(room.Description)) Describe(planned, room.Id, name, "", room.Description);
		if (room.Coordinates is { Length: > 0 } coordinates) Place(planned, room.Id, name, null, coordinates, axes);
	}

	private async Task PlanRoomAsync(AnySharpObject actor, RoomDocumentRoomDto room, Dictionary<string, RoomDocumentRoomDto> current,
		IReadOnlyList<string> axes, Planned planned, CancellationToken ct)
	{
		if (!current.TryGetValue(room.Id, out var now))
		{
			planned.Problems.Add($"{room.Id} is not a room you can examine.");
			return;
		}
		var changes = (room.Name is { } name && name.Trim() != now.Name)
			|| (room.Description is { } description && description != now.Description)
			|| (room.Coordinates is { } coordinates && !SameCoordinates(coordinates, now.Coordinates));
		if (!changes) return;
		if (await ObjectAsync(room.Id, ct) is not { } target || !await permissions.Controls(actor, target))
		{
			planned.Problems.Add($"You do not control {now.Name} ({room.Id}).");
			return;
		}
		if (room.Name is { } newName && newName.Trim() != now.Name)
		{
			if (string.IsNullOrWhiteSpace(newName) || newName.Length > MaxText) planned.Problems.Add($"{room.Id} needs a name.");
			else planned.Steps.Add(new("rename", room.Id, $"Rename {now.Name} ({room.Id}) to {newName.Trim()}", newName.Trim(),
				async context => Failure(await Command(context, "@NAME", room.Id, newName.Trim())), now.Name ?? ""));
		}
		if (room.Description is { } newDescription && newDescription != now.Description)
			Describe(planned, room.Id, now.Name!, now.Description ?? "", newDescription);
		if (room.Coordinates is { } newCoordinates && !SameCoordinates(newCoordinates, now.Coordinates))
			Place(planned, room.Id, now.Name!, now.Coordinates, newCoordinates, axes);
	}

	private async Task PlanExitsAsync(AnySharpObject actor, RoomDocumentRoomDto room, Dictionary<string, RoomDocumentRoomDto> current,
		HashSet<string> newRooms, Planned planned, CancellationToken ct)
	{
		if (room.Exits is not { } exits) return;
		var now = current.GetValueOrDefault(room.Id);
		var listed = exits.Select(exit => exit.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		foreach (var left in now?.Exits?.Where(exit => !listed.Contains(exit.Id)) ?? [])
			planned.Notes.Add($"{left.Name} ({left.Id}) in {now!.Name} is not in the document and is left as it is.");

		var sourceControlled = now is null || (await ObjectAsync(room.Id, ct) is { } source && await permissions.Controls(actor, source));
		foreach (var exit in exits)
		{
			if (NewKey().IsMatch(exit.Id))
			{
				await PlanNewExitAsync(actor, room, exit, sourceControlled, newRooms, planned, ct);
				continue;
			}
			if (now?.Exits?.FirstOrDefault(e => e.Id == exit.Id) is not { } was)
			{
				planned.Problems.Add(now is null
					? $"{exit.Id} cannot move to the new room {room.Id}; exits are not moved between rooms."
					: $"{exit.Id} is not an exit of {now.Name} ({room.Id}) that you can examine.");
				continue;
			}
			await PlanExitAsync(actor, room, exit, was, newRooms, planned, ct);
		}
	}

	private async Task PlanNewExitAsync(AnySharpObject actor, RoomDocumentRoomDto room, RoomDocumentExitDto exit, bool sourceControlled,
		HashSet<string> newRooms, Planned planned, CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(exit.Name) || exit.Name.Length > MaxText)
		{
			planned.Problems.Add($"The new exit {exit.Id} needs a name.");
			return;
		}
		if (!sourceControlled)
		{
			planned.Problems.Add($"You do not control {room.Id}, so you cannot open {exit.Id} there.");
			return;
		}
		var to = await DestinationAsync(actor, exit.To ?? "unlinked", newRooms, exit.Id, planned, ct);
		if (to is null) return;
		var name = exit.Name.Trim();
		planned.Steps.Add(new("open", exit.Id, $"Open {name} ({exit.Id}) in {room.Id}{(to == "unlinked" ? ", unlinked" : $" to {to}")}",
			$"{room.Id}\t{name}\t{to}", async context =>
			{
				// Opened unlinked, then linked on its own: @open keeps an exit it may not link and still answers its dbref.
				var result = await Command(context, "@OPEN", name, null, null, context.Resolve(room.Id));
				if (Made(result) is not { } dbref) return result ?? "@open made no exit.";
				context.Created[exit.Id] = dbref;
				return to == "unlinked" ? null : Failure(await Command(context, "@LINK", dbref, context.Resolve(to)));
			}));
		if (!string.IsNullOrEmpty(exit.Description)) Describe(planned, exit.Id, name, "", exit.Description);
	}

	private async Task PlanExitAsync(AnySharpObject actor, RoomDocumentRoomDto room, RoomDocumentExitDto exit, RoomDocumentExitDto was,
		HashSet<string> newRooms, Planned planned, CancellationToken ct)
	{
		var rename = exit.Name is { } name && name.Trim() != was.Name;
		var relink = exit.To is { } to && to is not ("variable" or "hidden") && !string.Equals(to, was.To, StringComparison.OrdinalIgnoreCase);
		var describe = exit.Description is { } description && description != was.Description;
		if (!rename && !relink && !describe) return;
		if (await ObjectAsync(exit.Id, ct) is not { } target || !await permissions.Controls(actor, target))
		{
			planned.Problems.Add($"You do not control {was.Name} ({exit.Id}).");
			return;
		}
		if (rename)
		{
			if (string.IsNullOrWhiteSpace(exit.Name) || exit.Name.Length > MaxText) planned.Problems.Add($"{exit.Id} needs a name.");
			else planned.Steps.Add(new("rename", exit.Id, $"Rename {was.Name} ({exit.Id}) to {exit.Name.Trim()}", exit.Name.Trim(),
				async context => Failure(await Command(context, "@NAME", exit.Id, exit.Name.Trim())), was.Name ?? ""));
		}
		if (relink && await DestinationAsync(actor, exit.To!, newRooms, exit.Id, planned, ct) is { } destination)
		{
			planned.Steps.Add(destination == "unlinked"
				? new("unlink", exit.Id, $"Unlink {was.Name} ({exit.Id})", "unlinked",
					async context => Failure(await Command(context, "@UNLINK", exit.Id)), was.To ?? "")
				: new("link", exit.Id, $"Link {was.Name} ({exit.Id}) to {destination}", destination,
					async context => Failure(await Command(context, "@LINK", exit.Id, context.Resolve(destination))), was.To ?? ""));
		}
		if (describe) Describe(planned, exit.Id, was.Name!, was.Description ?? "", exit.Description!);
	}

	/// <summary>
	/// <c>home</c>, <c>unlinked</c>, a new room's key, or the dbref of a room the actor can see. Null, after noting
	/// the problem, for anything else.
	/// </summary>
	private async Task<string?> DestinationAsync(AnySharpObject actor, string to, HashSet<string> newRooms, string exitId, Planned planned,
		CancellationToken ct)
	{
		if (to is "home" or "unlinked") return to;
		if (newRooms.Contains(to)) return newRooms.First(key => string.Equals(key, to, StringComparison.OrdinalIgnoreCase));
		if (Dbref().IsMatch(to) && await ObjectAsync(to, ct) is { IsRoom: true } room
			&& await reality.CanPerceiveAsync(actor.Object().DBRef, room.Object().DBRef, ct))
			return to;
		planned.Problems.Add($"{exitId} leads to \"{to}\", which is not home, unlinked, a room in the document or a room you can see.");
		return null;
	}

	private void Describe(Planned planned, string id, string name, string was, string description)
	{
		if (description.Length > MaxText)
		{
			planned.Problems.Add($"The description of {id} is longer than {MaxText} characters.");
			return;
		}
		var summary = description.Length == 0
			? $"Clear the description of {name} ({id})"
			: $"Describe {name} ({id}): \"{Shown(description)}\"{(was.Length == 0 ? "" : $", was \"{Shown(was)}\"")}";
		planned.Steps.Add(new("describe", id, summary, description, async context =>
		{
			if (await Target(context, id) is not { } target) return "It is not there any more.";
			var result = description.Length == 0
				? await attributes.ClearAttributeAsync(context.Actor, target, "DESCRIBE", IAttributeService.AttributePatternMode.Exact)
				: await attributes.SetAttributeAsync(context.Actor, target, "DESCRIBE", MarkupText.Plain(description));
			return result is Error<string> error ? error.Value : null;
		}, was));
	}

	private void Place(Planned planned, string id, string name, double[]? was, double[] coordinates, IReadOnlyList<string> axes)
	{
		if (coordinates.Length is not (0 or 3) || coordinates.Any(axis => !double.IsFinite(axis)))
		{
			planned.Problems.Add($"The coordinates of {id} must be three numbers, or [] to remove them.");
			return;
		}
		var values = coordinates.Select(axis => axis.ToString(CultureInfo.InvariantCulture)).ToArray();
		var summary = coordinates.Length == 0
			? $"Remove the coordinates of {name} ({id})"
			: $"Place {name} ({id}) at {string.Join(", ", values)}{(was is null ? "" : $", was {string.Join(", ", was.Select(axis => axis.ToString(CultureInfo.InvariantCulture)))}")}";
		planned.Steps.Add(new("place", id, summary, string.Join(' ', values), async context =>
		{
			if (await Target(context, id) is not { } target) return "It is not there any more.";
			for (var i = 0; i < axes.Count; i++)
			{
				var result = values.Length == 0
					? await attributes.ClearAttributeAsync(context.Actor, target, axes[i], IAttributeService.AttributePatternMode.Exact)
					: await attributes.SetAttributeAsync(context.Actor, target, axes[i], MarkupText.Plain(values[i]));
				if (result is Error<string> error) return error.Value;
			}
			return null;
		}, was is null ? "" : string.Join(' ', was.Select(axis => axis.ToString(CultureInfo.InvariantCulture)))));
	}

	private static bool SameCoordinates(double[] wanted, double[]? now)
		=> wanted.Length == 0 ? now is null : now is not null && wanted.SequenceEqual(now);

	private async Task<AnySharpObject?> Target(Context context, string id)
		=> await ObjectAsync(context.Resolve(id), CancellationToken.None);

	/// <summary>Runs a building command as the actor with pre-split arguments; a null argument is left out.</summary>
	private async Task<string?> Command(Context context, string command, params string?[] arguments)
	{
		var split = new Dictionary<string, CallState>();
		for (var i = 0; i < arguments.Length; i++)
			if (arguments[i] is { } argument) split[i.ToString(CultureInfo.InvariantCulture)] = new CallState(argument);
		var result = await commands.InvokeAsync(command, context.Actor.Object().DBRef, split);
		return result?.Message?.ToPlainText().Trim();
	}

	/// <summary>The building commands answer a refusal as <c>#-1 …</c> and success as a dbref or nothing.</summary>
	private static string? Failure(string? result)
		=> result is not null && result.StartsWith("#-1", StringComparison.Ordinal) ? result : null;
}
