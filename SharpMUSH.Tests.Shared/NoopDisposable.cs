namespace SharpMUSH.Tests.Shared;

/// <summary>An <see cref="IDisposable"/> that does nothing — the subscription a fake hands back.</summary>
public sealed class NoopDisposable : IDisposable
{
	public static readonly NoopDisposable Instance = new();

	private NoopDisposable() { }

	public void Dispose() { }
}
