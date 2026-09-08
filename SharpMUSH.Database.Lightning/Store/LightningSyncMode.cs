namespace SharpMUSH.Database.Lightning.Store;

/// <summary>
/// How hard each commit pushes on the disk. Every mode keeps the file consistent after a crash — LMDB
/// never overwrites a live page — so the choice is only how many of the most recent commits a power
/// loss can take with it. A clean process exit loses nothing in any mode: the data is in the OS page
/// cache by the time a commit returns, and the kernel writes it back regardless of what the process
/// did next.
/// </summary>
public enum LightningSyncMode
{
	/// <summary>Data pages and the meta page are both fsynced on every commit. Nothing is lost on power
	/// failure. Each commit costs one disk sync, which caps a single writer at the disk's sync rate.</summary>
	Full,

	/// <summary>Data pages are fsynced on every commit; the meta page is not. One sync per commit instead
	/// of two, and a power failure can lose the last committed transaction only.</summary>
	NoMetaSync,

	/// <summary>No sync on commit. A timer forces one every <see cref="LightningStoreOptions.FlushInterval"/>
	/// while there is anything unflushed. A power failure can lose every commit since the last flush, so
	/// at most one interval's worth. This is the guarantee RocksDB-backed engines give by default, and it
	/// is still far tighter than a periodic database dump.</summary>
	Periodic
}
