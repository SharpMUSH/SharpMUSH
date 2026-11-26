using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace SharpMUSH.Library.Models.Portal.Play;

/// <summary>
/// The tab's play connection as a stream of ordered events: who is playing, the connection, the room, and the
/// named out-of-band packages the server sends that character. One tab plays one character, so a tab has one
/// session. A compiled portal component receives it as a cascading parameter
/// (<c>[CascadingParameter] public IPlaySession? Play { get; set; }</c>), limited to the packages its
/// application declares in <see cref="Applications.RegisteredApplication.Subscribes"/>.
/// </summary>
/// <remarks>
/// Part of the plugin contract since 4.1.0. This interface keeps these three members; a later capability is a
/// new interface the same object also implements, found with <c>is</c>.
/// </remarks>
public interface IPlaySession
{
	/// <summary>What the tab is playing now.</summary>
	PlayState State { get; }

	/// <summary>
	/// Starts delivering events to <paramref name="handler"/>, in the order the play connection received them.
	/// Each handler runs on the renderer's dispatcher, inside its own error boundary: a handler that throws is
	/// logged and still receives the next event. Dispose the result to stop.
	/// </summary>
	IDisposable Subscribe(PlaySubscription subscription, Func<PlayEvent, Task> handler);

	/// <summary>
	/// Opens the play connection as the tab's active character when it is not open. Connecting shows the
	/// character online and runs its connect events, so call it from a player's click, never when a component
	/// first renders.
	/// </summary>
	Task ConnectAsync();
}

/// <summary>Which packages a subscription receives and how it starts.</summary>
public sealed record PlaySubscription
{
	/// <summary>
	/// Package names to receive: exact (<c>myapp.private</c>) or a prefix ending in <c>.*</c> (<c>myapp.*</c>).
	/// Empty receives lifecycle events only. <c>query.*</c> is never delivered. Every subscription gets what the engine's
	/// <c>sharp.play</c> says as <see cref="PlayStateChanged"/> and <see cref="PlayReset"/>; naming <c>sharp.*</c> also
	/// delivers the packages themselves.
	/// </summary>
	public IReadOnlyList<string> Packages { get; init; } = [];

	/// <summary>Whether the subscription starts with the cached value of each matching package.</summary>
	public PlayDelivery Delivery { get; init; } = PlayDelivery.Each;

	/// <summary>
	/// Whether packages the player's own softcode sent (<c>think oob(me, …)</c>) are delivered. Off by default:
	/// only a wizard or a holder of <c>Send_OOB</c> can send a package to someone else.
	/// </summary>
	public bool AcceptSelfSent { get; init; }
}

/// <summary>The package names and patterns a play session reads.</summary>
public static class PlayPackageNames
{
	/// <summary>The longest pattern an application may declare.</summary>
	public const int MaxPatternLength = 128;

	/// <summary>The most patterns an application may declare.</summary>
	public const int MaxPatterns = 32;

	/// <summary>Prefixes no application receives: the portal's query answers (<c>query.</c>).</summary>
	public static IReadOnlyList<string> ReservedPrefixes { get; } = ["query."];

	/// <summary>
	/// The prefix of the engine's own packages, such as <c>sharp.play</c>. An application may declare them, but every
	/// subscription already gets what they say as <see cref="PlayStateChanged"/> and <see cref="PlayReset"/>, which the
	/// session raises before it hands the package on; the SHARPMUSH001 analyzer warning points there.
	/// </summary>
	public const string EnginePrefix = "sharp.";

	/// <summary>Whether a package of this name is never delivered to an application.</summary>
	public static bool IsReserved(string package) =>
		ReservedPrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Whether <paramref name="pattern"/> is one an application may declare: an exact name (<c>myapp.private</c>) or a
	/// prefix ending in <c>.*</c> (<c>myapp.*</c>), of letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, not reserved.
	/// </summary>
	public static bool IsPattern(string pattern)
	{
		if (pattern.Length is 0 or > MaxPatternLength) return false;
		var name = pattern.EndsWith(".*", StringComparison.Ordinal) ? pattern[..^2] : pattern;
		return name.Length > 0 && name[0] != '.' && name[^1] != '.'
			&& name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
			&& !IsReserved(name + ".");
	}
}

/// <summary>How a subscription starts.</summary>
public enum PlayDelivery
{
	/// <summary>Only packages that arrive from now on. For event packages, so opening an app does not replay the last event.</summary>
	Each,

	/// <summary>The cached value of each matching package at once (as <see cref="PlayOrigin.Cached"/>), then every change. For state packages.</summary>
	Latest
}

/// <summary>What the tab is playing.</summary>
/// <param name="Character">The character playing, or null when nobody is.</param>
/// <param name="Connection">The play connection's state.</param>
/// <param name="Room">The objid of the room the character is in, or null when unknown.</param>
/// <param name="Generation">Goes up on every <see cref="PlayReset"/>; every event carries the one it belongs to.</param>
public sealed record PlayState(PlayCharacter? Character, PlayConnection Connection, string? Room, long Generation)
{
	/// <summary>Nobody playing, generation 0.</summary>
	public static PlayState NotPlaying { get; } = new(null, PlayConnection.NotPlaying, null, 0);
}

/// <summary>The character a tab plays.</summary>
/// <param name="ObjId">Its objid (<c>#12:1700000000000</c>).</param>
/// <param name="Name">Its name.</param>
/// <param name="Guest">Whether it is a guest character.</param>
public sealed record PlayCharacter(string ObjId, string Name, bool Guest);

/// <summary>The play connection's state.</summary>
public enum PlayConnection
{
	/// <summary>No play connection: the player has not opened Play in this tab, or quit.</summary>
	NotPlaying,

	/// <summary>The connection is opening.</summary>
	Connecting,

	/// <summary>Connected and playing.</summary>
	Connected,

	/// <summary>The connection dropped and is coming back; a resume keeps the session.</summary>
	Reconnecting
}

/// <summary>Why everything an app holds for the previous character or session must be dropped.</summary>
public enum PlayResetReason
{
	/// <summary>The tab switched to another character.</summary>
	CharacterChanged,

	/// <summary>The account signed out.</summary>
	SignedOut,

	/// <summary>A resume was refused and the connection logged in afresh.</summary>
	NewSession,

	/// <summary>The session ended: the player quit, or the server ended it.</summary>
	Ended
}

/// <summary>Where a package came from in the stream.</summary>
public enum PlayOrigin
{
	/// <summary>Sent while the tab was connected.</summary>
	Live,

	/// <summary>Sent while the tab was away and delivered when its session resumed.</summary>
	CatchUp,

	/// <summary>The cached value delivered when a <see cref="PlayDelivery.Latest"/> subscription started.</summary>
	Cached
}

/// <summary>Who sent a package.</summary>
public enum PlaySender
{
	/// <summary>The server did not say.</summary>
	Unknown,

	/// <summary>The character's own softcode.</summary>
	Self,

	/// <summary>Another object, which a wizard or a holder of <c>Send_OOB</c> runs.</summary>
	Other
}

/// <summary>
/// One event of a play session. The set of event types grows: switch on the ones an app handles and ignore the
/// rest with a discard arm.
/// </summary>
/// <param name="Generation">The <see cref="PlayState.Generation"/> it belongs to.</param>
public abstract record PlayEvent(long Generation);

/// <summary>
/// Everything an app holds for the previous character or session is stale: drop it. A
/// <see cref="PlayStateChanged"/> with the new state follows.
/// </summary>
public sealed record PlayReset(long Generation, PlayResetReason Reason) : PlayEvent(Generation);

/// <summary>The character, the connection or the room changed.</summary>
public sealed record PlayStateChanged(long Generation, PlayState Previous, PlayState Current) : PlayEvent(Generation);

/// <summary>
/// A named out-of-band package. <see cref="Data"/> is the payload exactly as sent; <see cref="Read{T}(int, int)"/>
/// turns it into a type without ever throwing.
/// </summary>
/// <param name="Package">The package name.</param>
/// <param name="Version">
/// The payload's version: the integer in the data's top-level <c>"v"</c>, 1 when there is none, 0 when it is
/// there but not a positive integer. A new field keeps the version; a removed or re-meant one raises it.
/// </param>
/// <param name="Data">The payload as sent.</param>
/// <param name="Origin">Live, delivered after a resume, or the cached value at subscribe.</param>
/// <param name="Sender">Whether the character's own softcode sent it.</param>
/// <param name="SenderObjId">The objid of the object that sent it, when the server said.</param>
public sealed record PlayPackage(long Generation, string Package, int Version, JsonElement Data,
	PlayOrigin Origin, PlaySender Sender, string? SenderObjId) : PlayEvent(Generation)
{
	/// <summary>
	/// The options <see cref="Read{T}(int, int)"/> reads with: property names in any case, unknown properties
	/// skipped, numbers accepted from strings, comments and trailing commas allowed.
	/// </summary>
	public static JsonSerializerOptions ReadOptions { get; } = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true
	};

	/// <summary>
	/// Reads <see cref="Data"/> as <typeparamref name="T"/> when <see cref="Version"/> is within
	/// [<paramref name="minVersion"/>, <paramref name="maxVersion"/>]. A missing property keeps its default; a
	/// value of the wrong type, a null for a non-nullable value type or an absent <c>required</c> member comes
	/// back as <see cref="PlayUnreadable"/> with its JSON path. Model payloads with nullable properties and
	/// defaults, not <c>required</c>.
	/// </summary>
	[RequiresUnreferencedCode("Reads T by reflection. Pass a JsonTypeInfo<T> from a source-generated context to avoid it.")]
	[RequiresDynamicCode("Reads T by reflection. Pass a JsonTypeInfo<T> from a source-generated context to avoid it.")]
	public PlayRead<T> Read<T>(int minVersion = 1, int maxVersion = int.MaxValue) =>
		Read(minVersion, maxVersion, () => Data.Deserialize<T>(ReadOptions));

	/// <summary>
	/// <see cref="Read{T}(int, int)"/> with a type from a source-generated <c>JsonSerializerContext</c>, whose own
	/// options apply.
	/// </summary>
	public PlayRead<T> Read<T>(JsonTypeInfo<T> typeInfo, int minVersion = 1, int maxVersion = int.MaxValue) =>
		Read(minVersion, maxVersion, () => Data.Deserialize(typeInfo));

	/// <summary>The version a payload carries: its top-level <c>"v"</c>, 1 without one, 0 for one that is not a positive integer.</summary>
	public static int VersionOf(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("v", out var v)) return 1;
		return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var number) && number > 0 ? number : 0;
	}

	private PlayRead<T> Read<T>(int minVersion, int maxVersion, Func<T?> read)
	{
		if (Version < minVersion || Version > maxVersion) return new PlayWrongVersion(Version);
		try
		{
			return read() is T value ? value : new PlayUnreadable("$", "The payload is null.");
		}
		catch (JsonException ex)
		{
			return new PlayUnreadable(ex.Path ?? "$", ex.Message);
		}
		catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or FormatException or OverflowException)
		{
			return new PlayUnreadable("$", ex.Message);
		}
	}
}

/// <summary>The outcome of <see cref="PlayPackage.Read{T}(int, int)"/>: the value, a version outside the range asked for, or a payload that does not fit.</summary>
public union PlayRead<T>(T, PlayWrongVersion, PlayUnreadable);

/// <summary>The payload's version is outside the range the reader asked for.</summary>
/// <param name="Version">The version it carries.</param>
public readonly record struct PlayWrongVersion(int Version);

/// <summary>The payload does not fit the type.</summary>
/// <param name="Path">The JSON path that failed (<c>$.items[2].count</c>), or <c>$</c>.</param>
/// <param name="Reason">Why.</param>
public readonly record struct PlayUnreadable(string Path, string Reason);
