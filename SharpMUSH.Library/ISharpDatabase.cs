namespace SharpMUSH.Library;

/// <summary>
/// The full surface a database provider implements: every per-aggregate store, composed. Providers
/// implement this; consumers depend on the store (or stores) they actually call — <see cref="IObjectStore"/>,
/// <see cref="IAttributeStore"/>, and so on — so that a handler's constructor states which part of the
/// database it touches. Take <c>ISharpDatabase</c> itself only where the whole provider is genuinely
/// needed: migration-then-read bootstrap paths, staging and import, and type tests against
/// <see cref="ISharpDatabaseWithLogging"/>.
/// </summary>
public interface ISharpDatabase :
	IDatabaseLifecycle,
	IObjectStore,
	IFlagAndPowerStore,
	INavigationStore,
	IAttributeStore,
	IMailStore,
	IExpandedDataStore,
	IChannelStore,
	IAccountStore,
	IServerStateStore,
	ISessionRecordStore
{
}
