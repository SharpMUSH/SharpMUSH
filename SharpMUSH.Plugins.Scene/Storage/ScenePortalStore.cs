using System.Security.Cryptography;
using System.Text.Json;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Plugins.Storage;
using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Plugins.Scene.Storage;

/// <summary>
/// A character's unsent pose for one scene, kept by the server so it follows the character from one device to
/// another.
/// </summary>
/// <param name="Mode">The verb the pose goes out as: <c>emit</c> (in character) or <c>ooc</c>.</param>
/// <param name="Text">The plain text of the pose.</param>
/// <param name="Markup">The styled text, serialized, when the pose has colour; null otherwise.</param>
/// <param name="UpdatedAt">UTC Unix-millis of the write, so a device can tell whose copy is newer.</param>
public record SceneDraft(string Mode, string Text, string? Markup, long UpdatedAt);

/// <summary>When a character's private calendar link was made. The link itself is shown once and not kept.</summary>
public record SceneCalendarLink(long CreatedAt);

/// <summary>
/// What the portal keeps for a character about scenes, apart from the scenes themselves: unsent poses
/// (<see cref="SceneDraft"/>) and the private calendar link. Neither is game state, so neither goes through the
/// scene commands; the scene API reads and writes them for the character the request acts as.
/// </summary>
public interface IScenePortalStore
{
	/// <summary>The longest pose text a draft holds; the styled copy may be up to four times this.</summary>
	const int MaxDraftLength = 64 * 1024;

	/// <summary>The character's draft for the scene, or <c>NotFound</c>.</summary>
	Found<SceneDraft> GetDraft(string character, string sceneId);

	/// <summary>Replaces the character's draft for the scene.</summary>
	ValueTask SaveDraftAsync(string character, string sceneId, SceneDraft draft);

	/// <summary>Forgets the character's draft for the scene; nothing happens when there is none.</summary>
	ValueTask DeleteDraftAsync(string character, string sceneId);

	/// <summary>When the character's calendar link was made, or <c>NotFound</c> when it has none.</summary>
	Found<SceneCalendarLink> GetCalendarLink(string character);

	/// <summary>
	/// Makes the character a new calendar link and returns its secret. The link it had before stops working.
	/// </summary>
	ValueTask<string> CreateCalendarLinkAsync(string character, long now);

	/// <summary>Turns the character's calendar link off.</summary>
	ValueTask DeleteCalendarLinkAsync(string character);

	/// <summary>The character a calendar link's secret belongs to, or <c>NotFound</c>.</summary>
	Found<string> FindCalendarOwner(string secret);
}

/// <summary>
/// <see cref="IScenePortalStore"/> over the host-shared Lightning accessor, in two plugin-owned tables:
/// <list type="table">
///   <item><term><c>scene.draft</c></term><description><c>Keys.Composite(character, sceneId)</c> →
///   <see cref="SceneDraft"/> JSON. The character is its objid, so a recycled dbref never inherits a draft.</description></item>
///   <item><term><c>scene.cal</c></term><description><c>Keys.Composite("char", character)</c> → the link's
///   hash and creation time, and <c>Keys.Composite("hash", hash)</c> → the character. Only the SHA-256 of a
///   link's secret is stored, so a copy of the world does not hand out anyone's calendar.</description></item>
/// </list>
/// </summary>
public sealed class LightningScenePortalStore : IScenePortalStore
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = null };

	private const string CharacterKind = "char";
	private const string HashKind = "hash";

	private readonly ILightningStorageAccessor _accessor;
	private readonly TableDef _drafts;
	private readonly TableDef _calendar;

	public LightningScenePortalStore(ILightningStorageAccessor accessor)
	{
		_accessor = accessor;
		_drafts = accessor.OpenTable("scene.draft", duplicates: false);
		_calendar = accessor.OpenTable("scene.cal", duplicates: false);
	}

	private sealed record CalendarRecord(string Hash, long CreatedAt);

	public Found<SceneDraft> GetDraft(string character, string sceneId) =>
		_accessor.Read<Found<SceneDraft>>(tx =>
			tx.TryGet(_drafts, Keys.Composite(character, sceneId), out var bytes)
			&& JsonSerializer.Deserialize<SceneDraft>(bytes, JsonOptions) is { } draft
				? draft
				: new NotFound());

	public async ValueTask SaveDraftAsync(string character, string sceneId, SceneDraft draft)
	{
		var key = Keys.Composite(character, sceneId);
		var value = JsonSerializer.SerializeToUtf8Bytes(draft, JsonOptions);
		await _accessor.WriteAsync(tx =>
		{
			tx.Put(_drafts, key, value);
			return true;
		});
	}

	public async ValueTask DeleteDraftAsync(string character, string sceneId)
	{
		var key = Keys.Composite(character, sceneId);
		await _accessor.WriteAsync(tx => tx.Delete(_drafts, key));
	}

	public Found<SceneCalendarLink> GetCalendarLink(string character) =>
		_accessor.Read<Found<SceneCalendarLink>>(tx =>
			ReadLink(tx, character) is { } link ? new SceneCalendarLink(link.CreatedAt) : new NotFound());

	public async ValueTask<string> CreateCalendarLinkAsync(string character, long now)
	{
		var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
		var hash = Hash(secret);
		await _accessor.WriteAsync(tx =>
		{
			if (ReadLink(tx, character) is { } old) tx.Delete(_calendar, Keys.Composite(HashKind, old.Hash));
			tx.Put(_calendar, Keys.Composite(CharacterKind, character),
				JsonSerializer.SerializeToUtf8Bytes(new CalendarRecord(hash, now), JsonOptions));
			tx.Put(_calendar, Keys.Composite(HashKind, hash), Keys.Str(character));
			return true;
		});
		return secret;
	}

	public async ValueTask DeleteCalendarLinkAsync(string character) =>
		await _accessor.WriteAsync(tx =>
		{
			if (ReadLink(tx, character) is not { } link) return false;
			tx.Delete(_calendar, Keys.Composite(HashKind, link.Hash));
			return tx.Delete(_calendar, Keys.Composite(CharacterKind, character));
		});

	public Found<string> FindCalendarOwner(string secret) =>
		_accessor.Read<Found<string>>(tx =>
			tx.TryGet(_calendar, Keys.Composite(HashKind, Hash(secret)), out var bytes) ? Keys.ReadStr(bytes) : new NotFound());

	private CalendarRecord? ReadLink(ITx tx, string character) =>
		tx.TryGet(_calendar, Keys.Composite(CharacterKind, character), out var bytes)
			? JsonSerializer.Deserialize<CalendarRecord>(bytes, JsonOptions)
			: null;

	private static string Hash(string secret) => Convert.ToHexStringLower(SHA256.HashData(Keys.Str(secret)));
}
