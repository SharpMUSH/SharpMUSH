using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Secrets;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Named secrets the server holds for itself: passwords, tokens, signing keys. A stored value is
/// encrypted with the server's Data Protection key ring, which is kept outside the world, so a copy of
/// the world alone opens none of them. Values go in and never come back out: <c>@secret</c> and the
/// portal set and clear them and see only <see cref="SecretStatus"/>, and no softcode reads one.
/// Only server code calls <see cref="GetAsync"/>.
/// </summary>
/// <remarks>
/// An environment variable (<see cref="SecretNames.EnvironmentVariable"/>) or a file in the secrets
/// directory (<see cref="SecretStoreOptions.FilesPath"/>) overrides the stored value, in that order, for
/// operators who keep secrets outside the game.
/// </remarks>
public interface ISecretStore
{
	/// <summary>The secrets the server and its plugins read, by name.</summary>
	IReadOnlyList<SecretDefinition> Definitions { get; }

	/// <summary>
	/// The value of <paramref name="name"/>: from an override, else the stored value decrypted.
	/// <see cref="NotFound"/> when nothing sets it; an error when the stored value cannot be decrypted on
	/// this host.
	/// </summary>
	ValueTask<FoundResult<string>> GetAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>Every declared or stored secret, by name.</summary>
	ValueTask<IReadOnlyList<SecretStatus>> ListAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores <paramref name="value"/> as <paramref name="name"/>, replacing what was stored.</summary>
	/// <returns>The secret's status afterwards, or why it was refused (a bad name, an empty or oversized value).</returns>
	ValueTask<Result<SecretStatus>> SetAsync(string name, string value, SecretAuthor author,
		CancellationToken cancellationToken = default);

	/// <summary>Removes the stored value of <paramref name="name"/>. An override is left alone.</summary>
	/// <returns>The secret's status afterwards, or <see cref="NotFound"/> when nothing was stored.</returns>
	ValueTask<Found<SecretStatus>> ClearAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// The hosts <c>@http</c> may send <paramref name="name"/> to (see <see cref="SecretHosts"/>); empty when it may
	/// be sent nowhere.
	/// </summary>
	ValueTask<IReadOnlyList<string>> HostsAsync(string name, CancellationToken cancellationToken = default);

	/// <summary>
	/// Sets the hosts <c>@http</c> may send <paramref name="name"/> to, replacing the list; none clears it. Kept apart
	/// from the value, so a value an override supplies is bound too.
	/// </summary>
	/// <returns>The secret's status afterwards, or why it was refused (a bad name or host).</returns>
	ValueTask<Result<SecretStatus>> SetHostsAsync(string name, IReadOnlyList<string> hosts,
		CancellationToken cancellationToken = default);

	/// <summary>What is stored under <paramref name="names"/> now, still encrypted, for <see cref="RestoreAsync"/>.</summary>
	ValueTask<SecretSnapshot> SnapshotAsync(IEnumerable<string> names, CancellationToken cancellationToken = default);

	/// <summary>
	/// Puts the stored values of the snapshot's names back as they were when it was taken: a name that had none has
	/// none again. Other names are left alone.
	/// </summary>
	ValueTask RestoreAsync(SecretSnapshot snapshot, CancellationToken cancellationToken = default);
}
