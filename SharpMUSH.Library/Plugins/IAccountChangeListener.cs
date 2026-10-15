namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Hears that an account's roles, characters or standing may have changed: a role or override assigned or taken away,
/// on the account or on one of its characters, a character linked or unlinked, a ban or its end. For C# code that
/// mirrors those somewhere else (a Discord server's roles). A plugin registers an implementation as a singleton from
/// <see cref="IServiceRegistrar.RegisterServices"/>.
/// </summary>
/// <remarks>
/// It is told whenever the account's cached claims are dropped, which is after every such change, and also at other
/// times; it reads what it needs afresh. The call is made while the change waits, so an implementation only queues
/// the account and returns. One that throws is logged and the next is told.
/// </remarks>
public interface IAccountChangeListener
{
	ValueTask AccountChangedAsync(string accountId, CancellationToken cancellationToken);
}
