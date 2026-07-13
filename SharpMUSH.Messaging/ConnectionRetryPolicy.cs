// NOTE: relocated from SharpMUSH.Library so the ConnectionServer does not depend on the full
// Library. The original SharpMUSH.Library.* namespace is preserved so consumers are unchanged.
namespace SharpMUSH.Library.Utilities;

public static class ConnectionRetryPolicy
{
	public const int MaxAttempts = 5;
	public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(50);
}
