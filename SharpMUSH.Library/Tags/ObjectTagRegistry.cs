using System.Text.Json;
using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Tags;

/// <summary>
/// The world's tags: names for objects, so softcode can say <c>#weather</c> or <c>tagref(weather)</c> in place of
/// a dbref that differs from game to game. A global tag is the world's; a personal tag belongs to an owner and
/// resolves only for that owner's objects, after the global ones.
/// </summary>
/// <param name="Global">Each global tag, by lower-case name, to the objid it names.</param>
/// <param name="Personal">Each owner's objid to that owner's tags, by lower-case name, to the objid each names.</param>
public sealed record ObjectTags(int Version, Dictionary<string, string> Global, Dictionary<string, Dictionary<string, string>> Personal)
{
	public static ObjectTags Empty => new(1, [], []);
}

/// <summary>One tag, whose it is (null for a global tag), and the object it names.</summary>
public sealed record ObjectTag(string Name, DBRef? Owner, DBRef Target);

/// <summary>
/// Keeps <see cref="ObjectTags"/> in the world's server data. A tag names an object for the object's lifetime: its
/// objid is kept, so once the object is destroyed the tag names nothing, even when the dbref is used again, and
/// it is dropped at the next change. A personal tag may share a global tag's name; the global one hides it until
/// it is removed.
/// </summary>
public sealed class ObjectTagRegistry(IMediator mediator)
{
	public const string Key = "sharpmush.tags.v1";

	/// <summary>Tag names: 1-32 letters, digits, <c>_</c> or <c>-</c>, and not only digits, which would be a dbref.</summary>
	public const int MaxNameLength = 32;

	/// <summary>The most tags, global and personal together, that may name one object.</summary>
	public const int MaxPerObject = 16;

	/// <summary>A personal tag may be added only while fewer than this many tags, anyone's, name the object.</summary>
	public const int MaxPersonalPerObject = 5;

	private readonly SemaphoreSlim gate = new(1, 1);
	private readonly SemaphoreSlim loadGate = new(1, 1);
	private ObjectTags? tags;

	/// <summary>Letters, digits, <c>_</c> and <c>-</c>, and not a dbref's number (<c>12</c>, <c>-1</c>), which <c>#</c> would read first.</summary>
	public static bool ValidName(string name)
		=> name.Length is > 0 and <= MaxNameLength
			&& name.All(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-')
			&& !(name[0] == '-' ? name[1..] : name).All(char.IsAsciiDigit);

	/// <summary>
	/// The live object <paramref name="name"/> names: the global tag of that name, else <paramref name="owner"/>'s
	/// personal one; <see cref="NotFound"/> when neither names an object that still exists.
	/// </summary>
	public async ValueTask<Found<DBRef>> ResolveAsync(string name, DBRef? owner, CancellationToken ct = default)
	{
		var current = await ReadAsync(ct);
		var key = name.ToLowerInvariant();
		if (current.Global.TryGetValue(key, out var global) && await LiveAsync(global, ct) is { } globalTarget)
		{
			return globalTarget;
		}

		return owner is { } holder && PersonalOf(current, holder) is { } personal && personal.Tags.TryGetValue(key, out var mine)
			&& await LiveAsync(mine, ct) is { } personalTarget
				? personalTarget
				: new NotFound();
	}

	/// <summary>Every tag that names a live object: the global ones, then <paramref name="owner"/>'s, by name.</summary>
	public async ValueTask<IReadOnlyList<ObjectTag>> ListAsync(DBRef? owner, CancellationToken ct = default)
	{
		var current = await ReadAsync(ct);
		var listed = new List<ObjectTag>();
		foreach (var (name, target) in current.Global.OrderBy(tag => tag.Key, StringComparer.Ordinal))
		{
			if (await LiveAsync(target, ct) is { } live) listed.Add(new ObjectTag(name, null, live));
		}

		if (owner is { } holder && PersonalOf(current, holder) is { } personal)
		{
			foreach (var (name, target) in personal.Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
			{
				if (await LiveAsync(target, ct) is { } live) listed.Add(new ObjectTag(name, holder, live));
			}
		}

		return listed;
	}

	/// <summary>Every tag, global or anyone's, that names <paramref name="target"/>.</summary>
	public async ValueTask<IReadOnlyList<ObjectTag>> OfAsync(DBRef target, CancellationToken ct = default)
	{
		var current = await ReadAsync(ct);
		return [.. Naming(current, target)];
	}

	/// <summary>
	/// Adds a global tag (<paramref name="owner"/> null) or <paramref name="owner"/>'s personal tag naming
	/// <paramref name="target"/>, an object that exists, by its objid.
	/// </summary>
	public async ValueTask<Result<string>> AddAsync(string name, DBRef target, DBRef? owner, CancellationToken ct = default)
	{
		if (!ValidName(name))
		{
			return new Error<string>($"A tag name is 1-{MaxNameLength} letters, digits, _ or -, and not only digits; {name} is not.");
		}

		if (await LiveAsync(target.ToString(), ct) is not { } live)
		{
			return new Error<string>("No such object.");
		}

		var key = name.ToLowerInvariant();
		await gate.WaitAsync(ct);
		try
		{
			var current = await PrunedAsync(ct);
			var personal = owner is { } holder ? PersonalOf(current, holder) : null;
			if (owner is null ? current.Global.ContainsKey(key) : personal?.Tags.ContainsKey(key) == true)
			{
				return new Error<string>($"The tag {key} already exists.");
			}

			var naming = Naming(current, live).ToList();
			if (naming.Count >= MaxPerObject)
			{
				return new Error<string>($"{live} already has {MaxPerObject} tags.");
			}

			if (owner is { } personalOwner)
			{
				if (naming.Count >= MaxPersonalPerObject)
				{
					return new Error<string>($"{live} already has {MaxPersonalPerObject} tags, too many for a personal one.");
				}

				current.Personal[personal?.Key ?? personalOwner.ToString()] =
					new Dictionary<string, string>(personal?.Tags ?? []) { [key] = live.ToString() };
			}
			else
			{
				current.Global[key] = live.ToString();
			}

			await SaveAsync(current, ct);
			return $"Tag {key} added for {live}.";
		}
		finally
		{
			gate.Release();
		}
	}

	/// <summary>Removes a global tag (<paramref name="owner"/> null) or <paramref name="owner"/>'s personal one.</summary>
	public async ValueTask<Result<string>> RemoveAsync(string name, DBRef? owner, CancellationToken ct = default)
	{
		var key = name.ToLowerInvariant();
		await gate.WaitAsync(ct);
		try
		{
			var current = await PrunedAsync(ct);
			var removed = owner is { } holder
				? PersonalOf(current, holder) is { } personal && personal.Tags.Remove(key)
				: current.Global.Remove(key);
			if (!removed)
			{
				return new Error<string>($"No tag {key}.");
			}

			await SaveAsync(current, ct);
			return $"Tag {key} removed.";
		}
		finally
		{
			gate.Release();
		}
	}

	private static IEnumerable<ObjectTag> Naming(ObjectTags current, DBRef target)
		=> current.Global.Where(tag => Names(tag.Value, target)).Select(tag => new ObjectTag(tag.Key, null, target))
			.Concat(current.Personal.SelectMany(owner => owner.Value.Where(tag => Names(tag.Value, target))
				.Select(tag => new ObjectTag(tag.Key, DBRef.TryParse(owner.Key, out var holder) ? holder : null, target))));

	private static bool Names(string stored, DBRef target)
		=> DBRef.TryParse(stored, out var reference) && reference is { } named && named.SameObjectAs(target);

	/// <summary><paramref name="owner"/>'s personal tags, under the key they are kept by, when it has any.</summary>
	private static (string Key, Dictionary<string, string> Tags)? PersonalOf(ObjectTags current, DBRef owner)
		=> current.Personal.Where(entry => Names(entry.Key, owner)).Select(entry => (entry.Key, entry.Value))
			.Cast<(string, Dictionary<string, string>)?>().FirstOrDefault();

	/// <summary>The object <paramref name="stored"/> names, when it still exists with the same creation time.</summary>
	private async ValueTask<DBRef?> LiveAsync(string stored, CancellationToken ct)
		=> DBRef.TryParse(stored, out var parsed) && parsed is { } reference
			&& await mediator.Send(new GetObjectNodeQuery(reference), ct) is AnySharpObject found
				? found.Object().DBRef
				: null;

	/// <summary>A copy of the tags without those whose object or owner is gone.</summary>
	private async ValueTask<ObjectTags> PrunedAsync(CancellationToken ct)
	{
		var current = await ReadAsync(ct);
		var global = new Dictionary<string, string>();
		foreach (var (name, target) in current.Global)
		{
			if (await LiveAsync(target, ct) is not null) global[name] = target;
		}

		var personal = new Dictionary<string, Dictionary<string, string>>();
		foreach (var (owner, owned) in current.Personal)
		{
			if (await LiveAsync(owner, ct) is null) continue;
			var kept = new Dictionary<string, string>();
			foreach (var (name, target) in owned)
			{
				if (await LiveAsync(target, ct) is not null) kept[name] = target;
			}

			if (kept.Count > 0) personal[owner] = kept;
		}

		return current with { Global = global, Personal = personal };
	}

	/// <summary>
	/// The tags, loaded once. The load holds a gate of its own, so a reader's load cannot finish after a write and
	/// put back what the write replaced.
	/// </summary>
	private async ValueTask<ObjectTags> ReadAsync(CancellationToken ct)
	{
		if (tags is { } loaded) return loaded;
		await loadGate.WaitAsync(ct);
		try
		{
			return tags ??= await mediator.Send(new ExpandedServerDataQuery(Key), ct) is { } stored
				? JsonSerializer.Deserialize<ObjectTags>(JsonSerializer.Serialize(stored)) ?? ObjectTags.Empty
				: ObjectTags.Empty;
		}
		finally
		{
			loadGate.Release();
		}
	}

	private async ValueTask SaveAsync(ObjectTags value, CancellationToken ct)
	{
		await mediator.Send(new SetExpandedServerDataCommand(Key, value), ct);
		tags = value;
	}
}
