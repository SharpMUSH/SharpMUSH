using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using SharpMUSH.Library.API;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Secrets;

/// <summary>
/// The stored secrets, as the world keeps them: ciphertext only.
/// </summary>
public sealed class SecretsData
{
	public Dictionary<string, StoredSecret> Secrets { get; set; } = [];
}

/// <summary>One stored secret.</summary>
/// <param name="Ciphertext">The value, protected with the key ring for this secret's name, in base64.</param>
/// <param name="SetAt">When it was set.</param>
/// <param name="SetBy">Who set it.</param>
public sealed record StoredSecret(string Ciphertext, DateTimeOffset SetAt, SecretAuthor SetBy);

/// <summary>Some names' stored secrets as they stood, ciphertext only; null where nothing was stored.</summary>
public sealed record SecretSnapshot(IReadOnlyDictionary<string, StoredSecret?> Stored);

/// <inheritdoc />
/// <remarks>
/// Each name has a protector of its own (purpose <c>SharpMUSH.Secrets</c>, then the name), so one secret's
/// ciphertext copied over another's does not decrypt. Reading a value written with a key the ring has
/// since replaced writes it again with the current key, so a stored secret follows the ring's rotation
/// and an old key can be retired without losing it.
/// </remarks>
public sealed class SecretStore(
	IExpandedObjectDataService serverData,
	IDataProtectionProvider protection,
	IEnumerable<SecretDefinition> definitions,
	SecretStoreOptions options,
	TimeProvider clock,
	ILogger<SecretStore> logger) : ISecretStore
{
	/// <summary>The longest value stored: room for a PEM private key, not for a file.</summary>
	public const int MaxValueLength = 16 * 1024;

	private const string Purpose = "SharpMUSH.Secrets";

	private readonly SemaphoreSlim _writeLock = new(1, 1);

	// A declaration under a name no one could set is dropped, so every name this store looks up, reads a file
	// for or logs has passed SecretNames.IsValid.
	private readonly IReadOnlyDictionary<string, SecretDefinition> _definitions = definitions
		.Where(definition => SecretNames.IsValid(definition.Name))
		.GroupBy(definition => definition.Name, StringComparer.Ordinal)
		.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

	/// <inheritdoc />
	public IReadOnlyList<SecretDefinition> Definitions => _definitions.Values.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();

	/// <inheritdoc />
	public async ValueTask<FoundResult<string>> GetAsync(string name, CancellationToken cancellationToken = default)
	{
		if (!SecretNames.IsValid(name))
		{
			return new NotFound();
		}

		if (Override(name) is { } overridden)
		{
			return overridden.Value;
		}

		if (!(await LoadAsync()).Secrets.TryGetValue(name, out var stored))
		{
			return new NotFound();
		}

		return Unprotect(name, stored) switch
		{
			Plaintext plaintext => await RotatedAsync(name, stored, plaintext),
			Error<string> error => error
		};
	}

	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<SecretStatus>> ListAsync(CancellationToken cancellationToken = default)
	{
		var stored = (await LoadAsync()).Secrets;
		return _definitions.Keys.Union(stored.Keys)
			.Order(StringComparer.Ordinal)
			.Select(name => Status(name, stored.GetValueOrDefault(name)))
			.ToList();
	}

	/// <inheritdoc />
	public async ValueTask<Result<SecretStatus>> SetAsync(string name, string value, SecretAuthor author,
		CancellationToken cancellationToken = default)
	{
		if (!SecretNames.IsValid(name))
		{
			return new Error<string>($"'{name}' is not a secret name: use lower-case letters and digits, in parts joined by '.'.");
		}

		if (value.Length == 0)
		{
			return new Error<string>("A secret cannot be empty: clear it instead.");
		}

		if (value.Length > MaxValueLength)
		{
			return new Error<string>($"A secret holds at most {MaxValueLength} characters.");
		}

		var secret = new StoredSecret(Protect(name, value), clock.GetUtcNow(), author);
		await ChangeAsync(data =>
		{
			data.Secrets[name] = secret;
			return true;
		});
		logger.LogInformation("Secret {Secret} set by {Author} ({AuthorId})", name, author.Name, author.Id);
		return Status(name, secret);
	}

	/// <inheritdoc />
	public async ValueTask<Found<SecretStatus>> ClearAsync(string name, CancellationToken cancellationToken = default)
	{
		var removed = false;
		await ChangeAsync(data => removed = data.Secrets.Remove(name));
		if (!removed)
		{
			return new NotFound();
		}

		logger.LogInformation("Secret {Secret} cleared", name);
		return Status(name, null);
	}

	/// <inheritdoc />
	public async ValueTask<SecretSnapshot> SnapshotAsync(IEnumerable<string> names, CancellationToken cancellationToken = default)
	{
		var stored = (await LoadAsync()).Secrets;
		return new SecretSnapshot(names.Distinct(StringComparer.Ordinal)
			.ToDictionary(name => name, name => stored.GetValueOrDefault(name), StringComparer.Ordinal));
	}

	/// <inheritdoc />
	public async ValueTask RestoreAsync(SecretSnapshot snapshot, CancellationToken cancellationToken = default)
		=> await ChangeAsync(data =>
		{
			foreach (var (name, stored) in snapshot.Stored)
			{
				if (stored is null)
				{
					data.Secrets.Remove(name);
				}
				else
				{
					data.Secrets[name] = stored;
				}
			}

			return true;
		});

	private SecretStatus Status(string name, StoredSecret? stored)
	{
		var definition = _definitions.GetValueOrDefault(name);
		var overridden = Override(name);
		var source = (overridden, stored) switch
		{
			({ } found, _) => found.Source,
			(null, null) => SecretSource.Unset,
			(null, { } secret) => Unprotect(name, secret) is Plaintext ? SecretSource.Stored : SecretSource.Unreadable
		};

		return new SecretStatus(name, definition?.Description, source, overridden?.Where, stored is not null, stored?.SetAt, stored?.SetBy,
			definition?.ManagedBy, definition is not null);
	}

	/// <summary>The environment variable's value, else the file's; null when neither sets the secret.</summary>
	private Overridden? Override(string name)
	{
		var variable = SecretNames.EnvironmentVariable(name);
		if (options.Environment(variable) is { Length: > 0 } environment)
		{
			return new Overridden(SecretSource.Environment, variable, environment);
		}

		var path = Path.Combine(options.FilesPath, name);
		try
		{
			// A mounted secret usually ends in the newline its editor added; the value does not.
			return File.Exists(path) && File.ReadAllText(path).TrimEnd('\r', '\n') is { Length: > 0 } file
				? new Overridden(SecretSource.File, path, file)
				: null;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			logger.LogWarning(e, "The secret file {Path} could not be read; the stored value of {Secret} is used", path, name);
			return null;
		}
	}

	private IDataProtector Protector(string name) => protection.CreateProtector(Purpose, name);

	private string Protect(string name, string value)
		=> Convert.ToBase64String(Protector(name).Protect(Encoding.UTF8.GetBytes(value)));

	private Result<Plaintext> Unprotect(string name, StoredSecret stored)
	{
		try
		{
			var ciphertext = Convert.FromBase64String(stored.Ciphertext);
			return Protector(name) switch
			{
				IPersistedDataProtector persisted => new Plaintext(
					Encoding.UTF8.GetString(persisted.DangerousUnprotect(ciphertext, ignoreRevocationErrors: false,
						out var requiresMigration, out _)), requiresMigration),
				var protector => new Plaintext(Encoding.UTF8.GetString(protector.Unprotect(ciphertext)), false)
			};
		}
		catch (Exception e) when (e is CryptographicException or FormatException)
		{
			logger.LogWarning("The stored secret {Secret} cannot be decrypted with this host's key ring: {Reason}", name, e.Message);
			return new Error<string>(
				$"The stored secret '{name}' cannot be decrypted: the key it was written with is not in this server's key ring. Set it again.");
		}
	}

	/// <summary>
	/// Writes the value again with the ring's current key when it was written with one the ring has
	/// replaced, unless it changed meanwhile.
	/// </summary>
	private async ValueTask<string> RotatedAsync(string name, StoredSecret stored, Plaintext plaintext)
	{
		if (plaintext.RequiresMigration)
		{
			await ChangeAsync(data =>
			{
				if (!data.Secrets.TryGetValue(name, out var current) || current.Ciphertext != stored.Ciphertext)
				{
					return false;
				}

				data.Secrets[name] = stored with { Ciphertext = Protect(name, plaintext.Value) };
				return true;
			});
			logger.LogInformation("Secret {Secret} written again with the key ring's current key", name);
		}

		return plaintext.Value;
	}

	private async ValueTask<SecretsData> LoadAsync()
		=> await serverData.GetExpandedServerDataAsync<SecretsData>() ?? new SecretsData();

	/// <summary>Runs <paramref name="change"/> on the stored secrets under the lock, and writes them when it returns true.</summary>
	private async ValueTask ChangeAsync(Func<SecretsData, bool> change)
	{
		await _writeLock.WaitAsync();
		try
		{
			var data = await LoadAsync();
			if (change(data))
			{
				await serverData.SetExpandedServerDataAsync(data);
			}
		}
		finally
		{
			_writeLock.Release();
		}
	}

	/// <summary>An override: where it is (the variable's name or the file's path) and its value.</summary>
	private sealed record Overridden(SecretSource Source, string Where, string Value);

	private readonly record struct Plaintext(string Value, bool RequiresMigration);
}
