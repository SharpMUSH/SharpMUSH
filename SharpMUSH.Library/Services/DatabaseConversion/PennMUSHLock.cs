using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.DatabaseConversion;

/// <summary>A PennMUSH expression with its original creator and numeric flags.</summary>
public sealed record PennMUSHLock(string Expression, int Flags = 0, int? Creator = null)
{
	public SharpLockData ToSharpLockData(DBRef? creator) => new(Expression, MapFlags(Flags), creator);

	private static LockService.LockFlags MapFlags(int flags)
		=> (LockService.LockFlags)(flags & 0x1f)
			| ((flags & 0x20) != 0 ? LockService.LockFlags.Ox : 0)
			| ((flags & 0x40) != 0 ? LockService.LockFlags.NoSuccessAction : 0)
			| ((flags & 0x80) != 0 ? LockService.LockFlags.NoFailureAction : 0)
			| ((flags & 0x100) != 0 ? LockService.LockFlags.Owner : 0);

	public static implicit operator PennMUSHLock(string expression) => new(expression);
}
