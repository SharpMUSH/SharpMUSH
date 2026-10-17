namespace SharpMUSH.Plugins.Discord.Links;

/// <summary>Serializes lifecycle and permission changes, including Restore called by unlink.</summary>
public sealed class DiscordLinkMutationGate
{
	private sealed class Scope { public bool Active { get; set; } = true; }
	private readonly SemaphoreSlim gate = new(1, 1);
	private readonly AsyncLocal<Scope?> current = new();
	public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct)
	{
		if (current.Value is { Active: true }) return await work();
		await gate.WaitAsync(ct);
		var previous = current.Value;
		var scope = new Scope(); current.Value = scope;
		try { return await work(); }
		finally { scope.Active = false; current.Value = previous; gate.Release(); }
	}
	/// <summary>Game-queue callers must never wait for a remote Discord operation to release this gate.</summary>
	public async Task<T> TryRunAsync<T>(Func<Task<T>> work, T refused, CancellationToken ct)
	{
		if (current.Value is { Active: true }) return await work();
		if (!await gate.WaitAsync(0, ct)) return refused;
		var previous = current.Value;
		var scope = new Scope(); current.Value = scope;
		try { return await work(); }
		finally { scope.Active = false; current.Value = previous; gate.Release(); }
	}

}
