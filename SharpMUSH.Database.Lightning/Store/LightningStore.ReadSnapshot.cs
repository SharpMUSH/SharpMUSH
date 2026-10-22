using SharpMUSH.Library.Plugins.Storage.Lightning;

namespace SharpMUSH.Database.Lightning.Store;

public sealed partial class LightningStore
{
	private sealed partial class Tx : IReadSnapshotTx
	{
		// The existing callback wrapper is its own lifetime owner: no extra owner allocation for
		// every store read. Reset/Renew of the pooled native transaction cannot revive this bit.
		private bool _activeRead = readTransactionId.HasValue;
		internal void EndReadCallback() => _activeRead = false;
		private void RequireReadCallback()
		{
			if (!readTransactionId.HasValue) throw new NotSupportedException("Native read capabilities require a readonly callback.");
			ObjectDisposedException.ThrowIf(!_activeRead, this);
		}

		public ulong ReadTransactionId
		{
			get
			{
				RequireReadCallback();
				return readTransactionId.GetValueOrDefault();
			}
		}
	}
}
