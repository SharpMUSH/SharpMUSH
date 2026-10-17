using System.Text.Json;
using SharpMUSH.Library.Services.DatabaseConversion;

namespace SharpMUSH.Server.Services.Imports;

public enum ImportSessionState
{
	Preparing, Converting, Validating, Ready, PromotionRequested, Committing, Committed, Aborted, RecoveryRequired, RollbackRequested, RolledBack
}

/// <summary>Private paths and durable state; source/configuration contents and secret values never enter status responses.</summary>
public sealed record ImportSession(string Id, string Directory, ImportSessionState State)
{
	public string WorldPath => Path.Combine(Directory, "world");
	public string KeyRingPath => Path.Combine(Directory, "keys");
	public string ConfigurationPath => Path.Combine(Directory, "mush.cnf");
	public ConversionResult? Result { get; init; }
	public string? Failure { get; init; }
	public string? StartupFingerprint { get; init; }
	public bool HadKeyRing { get; init; }
}

/// <summary>
/// Commits only before live host construction, with the process-wide engine lease held. World and
/// encryption keys remain paired on rollback. A failed commit blocks startup rather than admitting a
/// host whose services could observe different generations. No caller cancellation crosses the commit point.
/// </summary>
public sealed class ImportPromotionJournal
{
	private readonly string _live;
	private readonly string _root;
	private readonly string _pending;
	private readonly object _sync = new();

	public ImportPromotionJournal(string liveWorldPath, string? liveKeyRingPath = null)
	{
		_live = Path.GetFullPath(liveWorldPath);
		_root = _live + ".imports";
		_pending = Path.Combine(_root, "pending.json");
		LiveKeyRingPath = Path.GetFullPath(liveKeyRingPath ?? _live + ".dataprotection-keys");
	}

	public string LiveKeyRingPath { get; }
	public string LiveWorldPath => _live;
	public string SessionsPath => _root;
	public bool RequiresConnectionReset => File.Exists(Path.Combine(_root, "reset-connections.json"));
	public void CompleteConnectionReset() => File.Delete(Path.Combine(_root, "reset-connections.json"));
	public string? AppliedStartupFingerprint => File.Exists(Path.Combine(_root, "startup-applied.json"))
		? JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(_root, "startup-applied.json"))) : null;

	/// <summary>Held for the complete engine lifetime, including normal shutdown and provider disposal.</summary>
	public FileStream AcquireEngineLease()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(_live)!);
		return new FileStream(_live + ".engine.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
	}

	public ImportSession Create()
	{
		lock (_sync)
		{
			PrivateDirectory(_root);
			if (File.Exists(_pending)) throw new InvalidOperationException("An import promotion or rollback is pending restart.");
			foreach (var active in List())
			{
				if (active.State is ImportSessionState.Preparing or ImportSessionState.Converting or ImportSessionState.Validating or ImportSessionState.Ready
					or ImportSessionState.PromotionRequested or ImportSessionState.RollbackRequested or ImportSessionState.Committing or ImportSessionState.RecoveryRequired)
					throw new InvalidOperationException("Another import session is active; finish or abort it first.");
			}
			var id = Guid.NewGuid().ToString("N");
			var session = new ImportSession(id, Path.Combine(_root, id), ImportSessionState.Preparing);
			PrivateDirectory(session.Directory);
			try { Save(session); }
			catch
			{
				try { Directory.Delete(session.Directory, recursive: true); }
				catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
				{ /* An unjournaled directory carries no intent and is ignored by discovery. */ }
				throw;
			}
			return session;
		}
	}

	public ImportSession Get(string id)
	{
		if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid import session identifier.", nameof(id));
		var directory = Path.Combine(_root, id);
		var session = JsonSerializer.Deserialize<ImportSession>(File.ReadAllText(Path.Combine(directory, "session.json")))
			?? throw new IOException("Import session journal is empty.");
		if (session.Id != id || session.Directory != directory) throw new IOException("Import session journal path mismatch.");
		return session;
	}

	public IReadOnlyList<ImportSession> List()
	{
		if (!Directory.Exists(_root)) return [];
		return Directory.EnumerateDirectories(_root)
			.Where(path => Guid.TryParseExact(Path.GetFileName(path), "N", out _)
				&& HasInitialJournal(path))
			.OrderByDescending(path => File.GetLastWriteTimeUtc(Path.Combine(path, "session.json")))
			.Select(path => Get(Path.GetFileName(path))).ToArray();
	}

	private static bool HasInitialJournal(string directory)
	{
		try
		{
			File.GetAttributes(Path.Combine(directory, "session.json"));
			return true;
		}
		catch (FileNotFoundException) { return false; }
		catch (DirectoryNotFoundException) { return false; }
	}

	public void Save(ImportSession session) => WriteDurable(Path.Combine(session.Directory, "session.json"), session);
	public void MarkReady(string id)
	{
		lock (_sync)
		{
			var session = Get(id);
			if (session.State is not (ImportSessionState.Preparing or ImportSessionState.Validating))
				throw new InvalidOperationException("This preview can no longer become ready.");
			Save(session with { State = ImportSessionState.Ready });
		}
	}

	public bool AbortPreview(string id, string reason)
	{
		lock (_sync)
		{
			var session = Get(id);
			if (session.State is not (ImportSessionState.Preparing or ImportSessionState.Converting or ImportSessionState.Validating or ImportSessionState.Ready or ImportSessionState.Aborted)) return false;
			Save(session with
			{
				State = ImportSessionState.Aborted,
				Failure = reason,
				Result = (session.Result ?? new ConversionResult()) with { Aborted = true, Errors = [.. session.Result?.Errors ?? [], reason] }
			});
			return true;
		}
	}

	public void RequestPromotion(string id, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (File.Exists(_pending)) throw new InvalidOperationException("A restart operation is already pending.");
			var session = Get(id);
			if (session.State != ImportSessionState.Ready) throw new InvalidOperationException("Only a validated ready preview can be promoted.");
			EnsureNoPrevious();
			if (!System.IO.Directory.Exists(session.WorldPath) || !System.IO.Directory.Exists(session.KeyRingPath))
				throw new IOException("The staged world or its matching encryption key ring is missing.");
			Save(session with { State = ImportSessionState.PromotionRequested, HadKeyRing = System.IO.Directory.Exists(LiveKeyRingPath) });
			WriteDurable(_pending, id);
		}
	}

	public void RequestRollback(string id)
	{
		lock (_sync)
		{
			var session = Get(id);
			if (File.Exists(_pending) && (session.State != ImportSessionState.RecoveryRequired
				|| JsonSerializer.Deserialize<string>(File.ReadAllText(_pending)) != id))
				throw new InvalidOperationException("A restart operation is already pending.");
			if (session.State is not (ImportSessionState.Committed or ImportSessionState.RecoveryRequired))
				throw new InvalidOperationException("Only a committed import or interrupted commit can be rolled back.");
			if (!System.IO.Directory.Exists(_live + ".previous")
				&& !(System.IO.Directory.Exists(_live) && System.IO.Directory.Exists(Path.Combine(session.Directory, "replaced-world")))
				&& !(session.State == ImportSessionState.RecoveryRequired && System.IO.Directory.Exists(_live) && System.IO.Directory.Exists(session.WorldPath)))
				throw new IOException("The retained previous world is missing.");
			Save(session with { State = ImportSessionState.RollbackRequested });
			WriteDurable(_pending, id);
		}
	}

	public void ApplyPendingBeforeHostConstruction()
	{
		if (!File.Exists(_pending))
		{
			// The authorized intent record may have reached disk before the separate marker write.
			var intents = List().Where(session => session.State is ImportSessionState.PromotionRequested or ImportSessionState.RollbackRequested).ToArray();
			if (intents.Length == 0) return;
			if (intents.Length != 1) throw new IOException("Multiple restart intents require explicit operator recovery.");
			WriteDurable(_pending, intents[0].Id);
		}
		var id = JsonSerializer.Deserialize<string>(File.ReadAllText(_pending)) ?? throw new IOException("Empty promotion intent.");
		var session = Get(id);
		if (session.State is ImportSessionState.Committed or ImportSessionState.RolledBack)
		{
			// A crash may have persisted Committed before the receipt and intent cleanup.
			// Complete that durable tail before accepting another startup preview.
			if (session.StartupFingerprint is not null)
				WriteDurable(Path.Combine(_root, "startup-applied.json"), session.StartupFingerprint);
			File.Delete(_pending);
			return;
		}
		try
		{
			if (session.State == ImportSessionState.RollbackRequested)
			{
				var replacedWorld = Path.Combine(session.Directory, "replaced-world");
				var replacedKeys = Path.Combine(session.Directory, "replaced-keys");
				// Filesystem phases are replayable if the process dies between a move and the next journal write.
				if (System.IO.Directory.Exists(_live + ".previous"))
				{
					if (System.IO.Directory.Exists(_live)) System.IO.Directory.Move(_live, replacedWorld);
					System.IO.Directory.Move(_live + ".previous", _live);
				}
				else if (!System.IO.Directory.Exists(_live)
					|| !(System.IO.Directory.Exists(replacedWorld) || System.IO.Directory.Exists(session.WorldPath)))
					throw new IOException("The original world cannot be identified safely during rollback.");
				if (System.IO.Directory.Exists(LiveKeyRingPath + ".previous"))
				{
					if (System.IO.Directory.Exists(LiveKeyRingPath)) System.IO.Directory.Move(LiveKeyRingPath, replacedKeys);
					System.IO.Directory.Move(LiveKeyRingPath + ".previous", LiveKeyRingPath);
				}
				else if (!session.HadKeyRing && System.IO.Directory.Exists(LiveKeyRingPath))
					System.IO.Directory.Move(LiveKeyRingPath, replacedKeys);
				else if (session.HadKeyRing && (!System.IO.Directory.Exists(LiveKeyRingPath)
					|| !(System.IO.Directory.Exists(replacedKeys) || System.IO.Directory.Exists(session.KeyRingPath))))
					throw new IOException("The original encryption key ring cannot be identified safely during rollback.");
				WriteDurable(Path.Combine(_root, "reset-connections.json"), id);
				Save(session with { State = ImportSessionState.RolledBack });
			}
			else
			{
				if (session.State != ImportSessionState.PromotionRequested)
					throw new IOException("An interrupted import commit requires explicit rollback before startup.");
				EnsureNoPrevious();
				Save(session with { State = ImportSessionState.Committing });
				if (System.IO.Directory.Exists(_live)) System.IO.Directory.Move(_live, _live + ".previous");
				System.IO.Directory.Move(session.WorldPath, _live);
				if (System.IO.Directory.Exists(LiveKeyRingPath)) System.IO.Directory.Move(LiveKeyRingPath, LiveKeyRingPath + ".previous");
				if (System.IO.Directory.Exists(session.KeyRingPath)) System.IO.Directory.Move(session.KeyRingPath, LiveKeyRingPath);
				WriteDurable(Path.Combine(_root, "reset-connections.json"), id);
				Save(session with { State = ImportSessionState.Committed });
			}
			if (session.StartupFingerprint is not null)
				WriteDurable(Path.Combine(_root, "startup-applied.json"), session.StartupFingerprint);
			File.Delete(_pending);
		}
		catch
		{
			Save(session with { State = ImportSessionState.RecoveryRequired, Failure = "Restart commit failed; retained world/key paths require rollback before serving requests." });
			throw;
		}
	}

	private void EnsureNoPrevious()
	{
		if (System.IO.Directory.Exists(_live + ".previous") || System.IO.Directory.Exists(LiveKeyRingPath + ".previous"))
			throw new IOException("A previous world or key ring is retained; archive it before another promotion.");
	}

	public static void PrivateDirectory(string path)
	{
		System.IO.Directory.CreateDirectory(path);
		if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}

	private static void WriteDurable<T>(string path, T value)
	{
		var temporary = path + ".writing-" + Guid.NewGuid().ToString("N");
		try
		{
			using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
				JsonSerializer.Serialize(stream, value);
				stream.Flush(flushToDisk: true);
			}
			File.Move(temporary, path, overwrite: true);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}
}
