using System.Reflection;
using LightningDB;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Plugins.Storage.Lightning;

// Narrow test-only managed bridge. No native handles, pointers, live field replacement or second store.
internal static class ActualBoundedReader
{
	public static IBoundedReadTx Create(LightningTransaction transaction, TableDef table, LightningDatabase database)
	{
		var store = typeof(LightningStore);
		var snapshotType = store.GetNestedType("TableSnapshot", BindingFlags.NonPublic)
			?? throw new MissingMemberException(store.FullName, "TableSnapshot");
		var txType = store.GetNestedType("Tx", BindingFlags.NonPublic)
			?? throw new MissingMemberException(store.FullName, "Tx");
		var capture = snapshotType.GetMethod("Capture", BindingFlags.Public | BindingFlags.Static, null,
			[typeof(LightningTransaction), typeof(Dictionary<TableDef, LightningDatabase>)], null)
			?? throw new MissingMethodException(snapshotType.FullName, "Capture");
		var constructor = txType.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
			[typeof(LightningTransaction), snapshotType], null)
			?? throw new MissingMethodException(txType.FullName, ".ctor(LightningTransaction, TableSnapshot)");
		var handles = new Dictionary<TableDef, LightningDatabase> { [table] = database };
		// Capture reads actual persisted flags through this valid transaction and its environment's DBI.
		var snapshot = capture.Invoke(null, [transaction, handles])
			?? throw new InvalidOperationException("TableSnapshot.Capture returned null");
		return constructor.Invoke([transaction, snapshot]) as IBoundedReadTx
			?? throw new InvalidOperationException("Actual LightningStore.Tx no longer implements IBoundedReadTx");
	}
}
