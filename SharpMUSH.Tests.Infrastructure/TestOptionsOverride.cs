using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests;

/// <summary>
/// Scopes a change to the server's <see cref="SharpMUSHOptions"/> to one async flow.
/// <para>
/// The test host builds a single <c>IOptionsWrapper&lt;SharpMUSHOptions&gt;</c> for the whole
/// session, so re-stubbing it mid-run would change the game under every test executing in
/// parallel. The wrapper instead reads through <see cref="Apply"/> on every call, and the
/// override lives in an <see cref="AsyncLocal{T}"/>: it is visible to the command the test
/// awaits and to nothing else. The test host's TestServer preserves the client's execution
/// context, so the same holds for an HTTP request the test sends.
/// </para>
/// </summary>
public static class TestOptionsOverride
{
	private static readonly AsyncLocal<Func<SharpMUSHOptions, SharpMUSHOptions>?> Current = new();

	/// <summary>The options this async flow should see, given the session's configured ones.</summary>
	public static SharpMUSHOptions Apply(SharpMUSHOptions configured)
		=> Current.Value is { } mutate ? mutate(configured) : configured;

	/// <summary>Applies <paramref name="mutate"/> until the returned scope is disposed.</summary>
	public static IDisposable Scope(Func<SharpMUSHOptions, SharpMUSHOptions> mutate)
	{
		var outer = Current.Value;
		Current.Value = outer is null ? mutate : configured => mutate(outer(configured));
		return new Restore(outer);
	}

	private sealed class Restore(Func<SharpMUSHOptions, SharpMUSHOptions>? outer) : IDisposable
	{
		public void Dispose() => Current.Value = outer;
	}
}

/// <summary>
/// The options monitor every engine service reads, with <see cref="TestOptionsOverride"/> applied.
/// <c>IOptionsWrapper</c> is only half the story: <c>PermissionService</c> and friends take
/// <see cref="IOptionsMonitor{T}"/> directly, so the scope has to be honoured here too.
/// </summary>
public sealed class TestOverridableOptionsMonitor(IOptionsMonitor<SharpMUSHOptions> inner)
	: IOptionsMonitor<SharpMUSHOptions>
{
	public SharpMUSHOptions CurrentValue => TestOptionsOverride.Apply(inner.CurrentValue);

	public SharpMUSHOptions Get(string? name) => TestOptionsOverride.Apply(inner.Get(name));

	public IDisposable? OnChange(Action<SharpMUSHOptions, string?> listener) => inner.OnChange(listener);
}
