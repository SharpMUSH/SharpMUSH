using Microsoft.JSInterop;
using SharpMUSH.Client.Models;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The play terminal's longer log, which the player turns on in Play's settings ("Keep longer logs"): the
/// server's lines for each character, kept in this browser (IndexedDB, through <c>js/terminal-log.js</c>)
/// up to <see cref="MaxLines"/> lines and <see cref="MaxAge"/>, so the terminal can show earlier lines than
/// it holds, after a reload or another visit.
/// </summary>
/// <remarks>
/// <para>Off until the player turns it on, and the choice is per browser, in <c>localStorage</c>: it is about
/// this device, which anyone using the same browser profile can read. Turning it off deletes every kept line.</para>
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

	/// <summary>The player turned the longer log on in this browser.</summary>
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
			await js.SetItemAsync(BrowserStore.Local, StorageKey, "on");
		}
		else
		{
			await js.RemoveItemAsync(BrowserStore.Local, StorageKey);
			await CallVoidAsync("SharpMUSH.TerminalLog.clearAll");
		}
		Changed?.Invoke();
	}

	/// <summary>Keeps <paramref name="line"/> for <paramref name="identity"/>, when the log is on and the line is one to keep.</summary>
	public async Task AppendAsync(TerminalIdentity identity, TerminalLine line)
	{
		await LoadAsync();
		if (!On || TerminalScrollback.Serialize(line) is not string stored) return;
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

	public static string KeyFor(TerminalIdentity identity) => $"{identity.Account}/{identity.Character}";

	private static bool Kept(TerminalLine line) => TerminalScrollback.Serialize(line) is string;

	private static long At(TerminalLine line) => new DateTimeOffset(line.Timestamp).ToUnixTimeMilliseconds();

	private async Task LoadCoreAsync()
	{
		var on = await js.GetItemAsync(BrowserStore.Local, StorageKey) == "on";
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
