using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Client.Models;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Client.Services;

/// <summary>
/// Who a terminal connection plays as: the account and the character's objid. A resume point is kept
/// per identity, so a reload as anyone else can never resume this one's session.
/// </summary>
public readonly record struct TerminalIdentity(string Account, string Character)
{
	/// <summary>The identity of <paramref name="character"/> on <paramref name="account"/>; none without an account.</summary>
	public static TerminalIdentity? Of(string? account, AccountAuthService.CharacterSummary character) =>
		account is null ? null : new TerminalIdentity(account, $"#{character.DbrefNumber}:{character.CreationTime}");
}

/// <summary>The server's resume token and the last frame number the page showed.</summary>
public readonly record struct TerminalResumePoint(string Token, long LastSeq);

/// <summary>
/// Keeps each terminal's resume point in sessionStorage (through <c>js/terminal-resume.js</c>), so a
/// reload within the connection server's grace period resumes the game session and replays the frames
/// it missed instead of logging in again.
/// </summary>
/// <remarks>
/// <para>sessionStorage is per tab, and the key carries the terminal ("play" or "portal") and the
/// identity. The script offers a stored point back only to a page that was reloaded: a tab the browser
/// duplicated, or one opened from this page, starts with a copy of this tab's storage, and resuming
/// there would take the session away from this tab.</para>
/// <para>A token is written at once; a frame number is staged, and the script writes it on a short timer
/// and when the page is hidden or unloaded. A point that lags is harmless: the server replays from it,
/// and a reloaded page has shown none of those frames.</para>
/// <para>The terminal's screen is kept beside the point (<see cref="LinesKeyFor"/>, see
/// <see cref="TerminalScrollback"/> for what of it), on the same timer. A reload that resumes shows it
/// before the frames it missed. A refused resume does not: that screen belongs to a session the server
/// ended, and the fresh login shows its own banner and room.</para>
/// <para>A browser that refuses storage keeps nothing, and every reload is a fresh login, as before.</para>
/// </remarks>
public sealed class TerminalResumeStore(IJSRuntime js)
{
	public const string KeyPrefix = "sharpmush.resume.";

	private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	private int _generation;

	public static string KeyFor(string presenceClass, TerminalIdentity identity) =>
		$"{KeyPrefix}{presenceClass}.{identity.Account}/{identity.Character}";

	/// <summary>Where the terminal's scrollback (<see cref="TerminalScrollback"/>) is kept, beside its point.</summary>
	public static string LinesKeyFor(string presenceClass, TerminalIdentity identity) =>
		KeyFor(presenceClass, identity) + "#lines";

	/// <summary>
	/// Where the terminal's current prompt is kept (<see cref="TerminalScrollback.SerializePrompt"/>), beside its
	/// point. Apart from the lines: a prompt is not scrollback, and the next one replaces it.
	/// </summary>
	public static string PromptKeyFor(string presenceClass, TerminalIdentity identity) =>
		KeyFor(presenceClass, identity) + "#prompt";

	/// <summary>The slot one connection keeps its point in, with what a reloaded page left there.</summary>
	public async ValueTask<TerminalResumeSlot> OpenAsync(string presenceClass, TerminalIdentity identity)
	{
		var key = KeyFor(presenceClass, identity);
		var linesKey = LinesKeyFor(presenceClass, identity);
		var stored = Parse(await CallAsync<string?>("SharpMUSH.Resume.resumable", key));
		var promptKey = PromptKeyFor(presenceClass, identity);
		var scrollback = stored is TerminalResumePoint
			? TerminalScrollback.Parse(await CallAsync<string?>("SharpMUSH.Resume.resumable", linesKey))
			: [];
		Found<TerminalPrompt> prompt = stored is TerminalResumePoint
			? TerminalScrollback.ParsePrompt(await CallAsync<string?>("SharpMUSH.Resume.resumable", promptKey))
			: new NotFound();
		return new TerminalResumeSlot(this, key, linesKey, promptKey, _generation, stored, scrollback, prompt);
	}

	/// <summary>
	/// Forgets every terminal's point and revokes every slot opened before now, so a connection still
	/// open as the previous identity cannot write one back. Run on logout and on a character switch.
	/// </summary>
	public async ValueTask ClearAllAsync()
	{
		_generation++;
		await CallVoidAsync("SharpMUSH.Resume.removeAll", KeyPrefix);
	}

	internal bool IsCurrent(TerminalResumeSlot slot) => slot.Generation == _generation;

	internal async ValueTask SaveAsync(TerminalResumeSlot slot, TerminalResumePoint point)
	{
		if (IsCurrent(slot)) await CallAsync<bool>("SharpMUSH.Resume.write", slot.Key, Serialize(point));
	}

	internal async ValueTask StageAsync(TerminalResumeSlot slot, TerminalResumePoint point)
	{
		if (IsCurrent(slot)) await CallVoidAsync("SharpMUSH.Resume.stage", slot.Key, Serialize(point));
	}

	internal async ValueTask ClearAsync(TerminalResumeSlot slot)
	{
		if (!IsCurrent(slot)) return;
		await CallVoidAsync("SharpMUSH.Resume.remove", slot.Key);
		await CallVoidAsync("SharpMUSH.Resume.remove", slot.LinesKey);
		await CallVoidAsync("SharpMUSH.Resume.remove", slot.PromptKey);
	}

	/// <summary>Stages the prompt for the page's timer to write, or forgets the kept one when there is none.</summary>
	internal async ValueTask KeepPromptAsync(TerminalResumeSlot slot, Found<string> prompt)
	{
		if (!IsCurrent(slot)) return;
		if (prompt is string stored)
			await CallVoidAsync("SharpMUSH.Resume.stage", slot.PromptKey, stored);
		else
			await CallVoidAsync("SharpMUSH.Resume.remove", slot.PromptKey);
	}

	internal async ValueTask AppendLineAsync(TerminalResumeSlot slot, string line)
	{
		if (IsCurrent(slot))
			await CallVoidAsync("SharpMUSH.Resume.appendLine", slot.LinesKey, line, TerminalScrollback.MaxLines, TerminalScrollback.MaxChars);
	}

	private static string Serialize(TerminalResumePoint point) =>
		JsonSerializer.Serialize(new StoredPoint(point.Token, point.LastSeq), Json);

	private static Found<TerminalResumePoint> Parse(string? stored)
	{
		if (string.IsNullOrEmpty(stored)) return new NotFound();
		try
		{
			return JsonSerializer.Deserialize<StoredPoint>(stored, Json) is { Token: { Length: > 0 } token, LastSeq: >= 0 and var lastSeq }
				? new TerminalResumePoint(token, lastSeq)
				: new NotFound();
		}
		catch (JsonException)
		{
			return new NotFound();
		}
	}

	private async ValueTask<T?> CallAsync<T>(string identifier, params object?[] args)
	{
		try
		{
			return await js.InvokeAsync<T>(identifier, args);
		}
		catch (JSException)
		{
			return default;
		}
	}

	private async ValueTask CallVoidAsync(string identifier, params object?[] args)
	{
		try
		{
			await js.InvokeVoidAsync(identifier, args);
		}
		catch (JSException)
		{
			// The script is the only thing that can fail here, and a point it could not keep is a fresh
			// login on the next reload, which is what the portal did before it kept any.
		}
	}

	private sealed record StoredPoint(string? Token, long LastSeq);
}

/// <summary>Where one connection keeps its resume point. Revoked by <see cref="TerminalResumeStore.ClearAllAsync"/>.</summary>
public sealed class TerminalResumeSlot
{
	private readonly TerminalResumeStore _store;

	private IReadOnlyList<TerminalLine> _scrollback;
	private Found<TerminalPrompt> _prompt;

	internal TerminalResumeSlot(TerminalResumeStore store, string key, string linesKey, string promptKey, int generation,
		Found<TerminalResumePoint> stored, IReadOnlyList<TerminalLine> scrollback, Found<TerminalPrompt> prompt)
	{
		_store = store;
		Key = key;
		LinesKey = linesKey;
		PromptKey = promptKey;
		Generation = generation;
		Stored = stored;
		_scrollback = scrollback;
		_prompt = prompt;
	}

	public string Key { get; }
	public string LinesKey { get; }
	public string PromptKey { get; }
	internal int Generation { get; }

	/// <summary>The point a reloaded page left for this terminal and identity.</summary>
	public Found<TerminalResumePoint> Stored { get; }

	/// <summary>True once the store was cleared after this slot was opened; it then writes nothing.</summary>
	public bool Revoked => !_store.IsCurrent(this);

	/// <summary>Writes now: a new token.</summary>
	public ValueTask SaveAsync(TerminalResumePoint point) => _store.SaveAsync(this, point);

	/// <summary>Leaves the write to the page's timer: a new frame number.</summary>
	public ValueTask StageAsync(TerminalResumePoint point) => _store.StageAsync(this, point);

	/// <summary>Forgets the point and the scrollback: the session they belonged to is over.</summary>
	public ValueTask ClearAsync() => _store.ClearAsync(this);

	/// <summary>
	/// The screen a reloaded page left beside its point, handed out once: a resumed session shows it
	/// before the frames it missed, and a later reconnect in the same page already shows it.
	/// </summary>
	public IReadOnlyList<TerminalLine> TakeScrollback()
	{
		var lines = _scrollback;
		_scrollback = [];
		return lines;
	}

	/// <summary>
	/// The prompt a reloaded page showed, handed out once, like <see cref="TakeScrollback"/>: a resumed session
	/// shows it again until a replayed prompt replaces it or the server clears it.
	/// </summary>
	public Found<TerminalPrompt> TakePrompt()
	{
		var prompt = _prompt;
		_prompt = new NotFound();
		return prompt;
	}

	/// <summary>Keeps the prompt shown now for a reload; null forgets the one kept.</summary>
	public ValueTask KeepPromptAsync(TerminalPrompt? prompt) =>
		_store.KeepPromptAsync(this, prompt is null ? new NotFound() : TerminalScrollback.SerializePrompt(prompt));

	/// <summary>Keeps one line of the screen, when it is one to keep (see <see cref="TerminalScrollback"/>).</summary>
	public ValueTask AppendLineAsync(TerminalLine line) =>
		TerminalScrollback.Serialize(line) is string stored ? _store.AppendLineAsync(this, stored) : ValueTask.CompletedTask;
}
