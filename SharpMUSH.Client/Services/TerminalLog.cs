using System.Text.Json;
using Microsoft.JSInterop;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The play terminal's saved log ("Save log in this browser" in Play's settings): the
/// server's lines for each character, kept in this browser (IndexedDB, through <c>js/terminal-log.js</c>)
/// up to <see cref="MaxLines"/> lines and <see cref="MaxAge"/>, so the terminal can show earlier lines than
/// it holds, after a reload or another visit.
/// </summary>
/// <remarks>
/// <para>On until the player turns it off, and the choice is per browser, in <c>localStorage</c> (only "off" is
/// stored): it is about this device, which anyone using the same browser profile can read. Turning it off deletes
/// every kept line.</para>
/// <para>A line is kept as <see cref="TerminalScrollback"/> keeps one for a reload: the server's lines only
/// (what the player typed can carry a password), with what acts when rendered taken out.</para>
/// </remarks>
public sealed class TerminalLog(IJSRuntime js)
{
	private const string StorageKey = "terminal.keeplogs";

	/// <summary>The most lines kept per character; the oldest go first.</summary>
	public const int MaxLines = 20_000;

	/// <summary>How long a line is kept.</summary>
	public static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

	/// <summary>How many earlier lines one request for them brings back.</summary>
	public const int Page = 500;

	private Task? _load;

	/// <summary>The saved log is on in this browser: it is until the player turns it off. Unknown until <see cref="LoadAsync"/>.</summary>
	public bool On { get; private set; }

	/// <summary>Raised when <see cref="On"/> changes, after it has.</summary>
	public event Action? Changed;

	/// <summary>Reads the kept choice once; later calls return the same task.</summary>
	public Task LoadAsync() => _load ??= LoadCoreAsync();

	public async Task SetAsync(bool on)
	{
		await LoadAsync();
		if (on == On) return;
		On = on;
		if (on)
		{
			await js.RemoveItemAsync(BrowserStore.Local, StorageKey);
		}
		else
		{
			await js.SetItemAsync(BrowserStore.Local, StorageKey, "off");
			await CallVoidAsync("SharpMUSH.TerminalLog.clearAll");
		}
		Changed?.Invoke();
	}

	/// <summary>Keeps <paramref name="line"/> for <paramref name="identity"/>, when the log is on and the line is one to keep.</summary>
	/// <param name="slot">
	/// The connection's resume slot, when it keeps one: a frame the page before a reload logged already, which the
	/// server replays, is left out (<see cref="TerminalResumeSlot.ClaimLog"/>).
	/// </param>
	public async Task AppendAsync(TerminalIdentity identity, TerminalLine line, TerminalResumeSlot? slot = null)
	{
		await LoadAsync();
		if (!On || TerminalScrollback.Serialize(line) is not string stored) return;
		if (slot is not null && !slot.ClaimLog(line.Seq)) return;
		await CallVoidAsync("SharpMUSH.TerminalLog.append", KeyFor(identity), At(line), stored, MaxLines, MaxAge.TotalMilliseconds);
	}

	/// <summary>
	/// Up to <see cref="Page"/> kept lines older than the oldest of <paramref name="shown"/>, oldest first.
	/// Lines kept in the same millisecond as that one and already shown are left out.
	/// </summary>
	public async Task<IReadOnlyList<TerminalLine>> EarlierAsync(TerminalIdentity identity, IReadOnlyList<TerminalLine> shown)
	{
		if (!On) return [];
		var oldest = shown.FirstOrDefault(Kept);
		var before = oldest is null ? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() : At(oldest);
		// The shown lines from that millisecond come back too, as the newest of the page: drop as many.
		var alreadyShown = oldest is null ? 0 : shown.TakeWhile(l => !Kept(l) || At(l) == before).Count(Kept);
		var stored = await CallAsync("SharpMUSH.TerminalLog.earlier", KeyFor(identity), before, Page + alreadyShown);
		var lines = TerminalScrollback.Parse(stored);
		var drop = Math.Min(alreadyShown, lines.Reverse().TakeWhile(l => At(l) == before).Count());
		return [.. lines.Take(lines.Count - drop)];
	}

	/// <summary>
	/// Every kept line of <paramref name="identity"/>'s from <paramref name="from"/> to <paramref name="to"/>, both
	/// included, oldest first, for an export: <see cref="NotFound"/> when none was kept then, an error when the
	/// browser would not let the log be read.
	/// </summary>
	public async Task<FoundResult<IReadOnlyList<TerminalLine>>> BetweenAsync(TerminalIdentity identity, DateTimeOffset from, DateTimeOffset to)
	{
		await LoadAsync();
		if (!On) return new NotFound();
		if (await CallAsync("SharpMUSH.TerminalLog.between", KeyFor(identity), from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds())
			is not string stored)
			return new Error<string>("unreadable");
		var lines = TerminalScrollback.Parse(stored);
		return lines.Count == 0 ? new NotFound() : new FoundResult<IReadOnlyList<TerminalLine>>(lines);
	}

	/// <summary>
	/// The lines of <paramref name="screen"/> (what a terminal holds now) from <paramref name="from"/> to
	/// <paramref name="to"/>, both included, kept as the saved log keeps them, for an export while the log is off:
	/// <see cref="NotFound"/> when there are none.
	/// </summary>
	public static FoundResult<IReadOnlyList<TerminalLine>> OnScreen(IEnumerable<TerminalLine> screen, DateTimeOffset from, DateTimeOffset to)
	{
		var stored = screen
			.Where(l => new DateTimeOffset(l.Timestamp) is var at && at >= from && at <= to)
			.Select(l => TerminalScrollback.Serialize(l) is string kept ? kept : null)
			.OfType<string>()
			.ToList();
		return stored.Count == 0
			? new NotFound()
			: new FoundResult<IReadOnlyList<TerminalLine>>(TerminalScrollback.Parse($"[{string.Join(',', stored)}]"));
	}

	/// <summary>The markup stylesheet and the theme's current colours, for an HTML export (<see cref="TerminalLogExport"/>).</summary>
	public async Task<TerminalLogExport.Style> ExportStyleAsync()
	{
		var json = await CallAsync("SharpMUSH.TerminalLog.exportStyle", (object)TerminalLogExport.ThemeProperties);
		try
		{
			return json is null ? TerminalLogExport.Style.None : JsonSerializer.Deserialize<TerminalLogExport.Style>(json) ?? TerminalLogExport.Style.None;
		}
		catch (JsonException)
		{
			return TerminalLogExport.Style.None;
		}
	}

	public static string KeyFor(TerminalIdentity identity) => $"{identity.Account}/{identity.Character}";

	private static bool Kept(TerminalLine line) => TerminalScrollback.Serialize(line) is string;

	private static long At(TerminalLine line) => new DateTimeOffset(line.Timestamp).ToUnixTimeMilliseconds();

	private async Task LoadCoreAsync()
	{
		var on = await js.GetItemAsync(BrowserStore.Local, StorageKey) != "off";
		if (on == On) return;
		On = on;
		Changed?.Invoke();
	}

	private async Task<string?> CallAsync(string identifier, params object?[] args)
	{
		try
		{
			return await js.InvokeAsync<string?>(identifier, args);
		}
		catch (JSException)
		{
			return null;
		}
	}

	private async Task CallVoidAsync(string identifier, params object?[] args)
	{
		try
		{
			await js.InvokeVoidAsync(identifier, args);
		}
		catch (JSException)
		{
			// The script refused: nothing more is kept, or nothing was.
		}
	}
}
