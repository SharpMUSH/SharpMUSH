namespace SharpMUSH.Library.Utilities;

public static class ConnectionRetryPolicy
{
	public const int MaxAttempts = 5;
	public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);
}
