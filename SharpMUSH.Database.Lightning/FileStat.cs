using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SharpMUSH.Database.Lightning;

/// <summary>
/// The two facts about a file .NET's own APIs do not expose: how much disk the filesystem has allocated
/// for it (an LMDB data file is sparse, so its length overstates that), and which device it is on (so a
/// report can say whether the backups share a disk with the world). Read with Linux's <c>statx</c>, whose
/// structure has one layout on every architecture; elsewhere both answers are "unknown".
/// </summary>
internal static class FileStat
{
	private const int AtFdCwd = -100;
	private const uint StatxBlocks = 0x400;
	private const int StatxSize = 256;
	private const int BlocksOffset = 48;
	/// <summary>stx_dev_major, followed by stx_dev_minor: the 8 bytes naming the device.</summary>
	private const int DevOffset = 136;

	/// <summary>Bytes allocated on disk for <paramref name="path"/>, or -1 when unknown.</summary>
	public static long AllocatedBytes(string path)
		=> TryStatx(path, out var buffer)
			&& (BinaryPrimitives.ReadUInt32LittleEndian(buffer) & StatxBlocks) != 0
				? (long)BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(BlocksOffset)) * 512
				: -1;

	/// <summary>Whether two existing paths are known to be on one filesystem; false when it cannot be told.</summary>
	public static bool SameDevice(string first, string second)
	{
		if (TryStatx(first, out var a) && TryStatx(second, out var b))
		{
			return a.AsSpan(DevOffset, 8).SequenceEqual(b.AsSpan(DevOffset, 8));
		}

		// Where statx is unavailable, a drive letter is the one thing that settles it.
		return OperatingSystem.IsWindows()
			&& string.Equals(Path.GetPathRoot(Path.GetFullPath(first)), Path.GetPathRoot(Path.GetFullPath(second)),
				StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryStatx(string path, out byte[] buffer)
	{
		buffer = new byte[StatxSize];
		if (!OperatingSystem.IsLinux() || !BitConverter.IsLittleEndian) return false;
		try
		{
			return Statx(AtFdCwd, path, 0, StatxBlocks, buffer) == 0;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
		{
			// A C library older than glibc 2.28, or one without statx: the figure is unknown, not an error.
			return false;
		}
	}

	/// <summary>statx(2). The device numbers are filled whatever the mask asks for.</summary>
	[DllImport("libc", EntryPoint = "statx", SetLastError = true)]
	private static extern int Statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask,
		[Out] byte[] buffer);
}
