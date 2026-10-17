using System.Text.Json;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>
/// In-process authority for one disposable lineage. Trusted orchestration respects its reservation
/// and never replaces paths during admission; this is not a defense against a malicious OS user.
/// </summary>
internal sealed class SemanticFixtureCapability : IDisposable
{
	private const string MarkerName = ".semantic-fixture.json";
	private readonly object _gate = new();
	private readonly FileStream _reservation;
	private readonly string _reservationPath;
	private SemanticFixtureLease? _active;
	private bool _claimed;
	private bool _initialized;
	private bool _poisoned;
	private bool _reservationRemoved;
	private readonly SemanticFixtureOwnership _ownership = new();

	private sealed record Marker(int Version, string CanonicalPath, Guid FixtureId);

	private SemanticFixtureCapability(string path, Guid fixtureId, FileStream reservation, string reservationPath)
	{
		CanonicalPath = path;
		FixtureId = fixtureId;
		_reservation = reservation;
		_reservationPath = reservationPath;
	}

	internal Guid FixtureId { get; }
	internal string CanonicalPath { get; }
	internal bool IsQuarantined { get { lock (_gate) return _ownership.IsQuarantined; } }
	internal bool MayRemoveArtifacts { get { lock (_gate) return _ownership.MayRemoveArtifacts; } }

	internal static SemanticFixtureCapability IssueNew(string ownedScratchRoot)
	{
		var root = Path.GetFullPath(ownedScratchRoot);
		RejectLinkedAncestors(root);
		Directory.CreateDirectory(root);
		RejectLinkedAncestors(root);
		var id = Guid.NewGuid();
		var path = Path.Combine(root, id.ToString("N"));
		var reservationPath = path + ".semantic-reservation";
		// CreateNew is the exclusivity protocol; random naming alone never authorizes adoption.
		var reservation = new FileStream(reservationPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
		var ownsDirectory = false;
		try
		{
			if (Path.Exists(path)) throw new InvalidOperationException("A fixture directory must not already exist.");
			Directory.CreateDirectory(path);
			ownsDirectory = true;
			RejectLinkedAncestors(path);
			using var marker = new FileStream(Path.Combine(path, MarkerName), FileMode.CreateNew, FileAccess.Write, FileShare.None);
			JsonSerializer.Serialize(marker, new Marker(1, path, id));
			marker.Flush(flushToDisk: true);
			return new(path, id, reservation, reservationPath);
		}
		catch
		{
			reservation.Dispose();
			if (ownsDirectory) Directory.Delete(path, true);
			File.Delete(reservationPath);
			throw;
		}
	}

	internal SemanticFixtureLease Claim(LightningStoreOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		lock (_gate)
		{
			if (_ownership.IsRevoked || _poisoned || _ownership.IsQuarantined) throw new InvalidOperationException("Fixture issuance is revoked or incomplete.");
			if (_active is not null) throw new InvalidOperationException("Fixture already has an active storage lease.");
			if (!string.Equals(Path.GetFullPath(options.Path), CanonicalPath, StringComparison.Ordinal))
				throw new InvalidOperationException("Fixture options do not name the issued path.");
			if (options.Sync != LightningSyncMode.Full) throw new InvalidOperationException("Fixtures require full-sync storage.");
			RejectLinkedAncestors(CanonicalPath);
			var markerPath = Path.Combine(CanonicalPath, MarkerName);
			try
			{
				if ((File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0)
					throw new InvalidOperationException("A fixture marker cannot be linked.");
				if (new FileInfo(markerPath).Length > 4096) throw new InvalidOperationException("Invalid fixture marker length.");
				var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath));
				if (marker is not { Version: 1 } || marker.FixtureId != FixtureId || marker.CanonicalPath != CanonicalPath)
					throw new InvalidOperationException("Fixture marker does not match its issued identity.");
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
			{
				throw new InvalidOperationException("Fixture marker is unavailable or invalid.", error);
			}

			if (!_claimed)
			{
				if (Directory.EnumerateFileSystemEntries(CanonicalPath).Any(entry => Path.GetFileName(entry) != MarkerName))
					throw new InvalidOperationException("A first fixture open requires an empty issued directory.");
			}
			else
			{
				if (!_initialized) throw new InvalidOperationException("First fixture migration never completed.");
				// Closed-only checker: dispose its environment before the writing constructor runs.
				LightningStore.RequireFixtureIdentity(options, FixtureId);
			}

			_active = new SemanticFixtureLease(this, !_claimed);
			_claimed = true;
			return _active;
		}
	}

	internal static void RejectLinkedAncestors(string path)
	{
		for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
		{
			if (current.LinkTarget is not null || (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0))
				throw new InvalidOperationException("Fixture paths and ancestors cannot be linked.");
		}
	}

	internal void Initialized(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (_active != lease || _poisoned || _ownership.IsQuarantined) throw new InvalidOperationException("Fixture lease is not usable.");
			_initialized = true;
		}
	}

	internal void InitializationFailed(SemanticFixtureLease lease)
	{
		lock (_gate) { if (_active == lease && !_initialized) _poisoned = true; }
	}

	internal void Quarantine(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (_active != lease) return;
			_ownership.Quarantine();
			_poisoned = true;
		}
	}

	internal void Release(SemanticFixtureLease lease)
	{
		lock (_gate)
		{
			if (_active != lease || !_ownership.MayReleaseLease) return;
			if (!_initialized) _poisoned = true;
			_active = null;
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_reservationRemoved) return;
			_ownership.Revoke(_active is not null);
			// Uncertain native ownership retains the reservation and all potentially live files.
			if (!_ownership.MayRemoveArtifacts) return;
			_reservation.Dispose();
			File.Delete(_reservationPath);
			_reservationRemoved = true;
		}
	}
}

/// <summary>Native ownership determines when a lease may be released.</summary>
internal sealed class SemanticFixtureLease : IDisposable
{
	private readonly SemanticFixtureCapability _owner;
	internal SemanticFixtureLease(SemanticFixtureCapability owner, bool firstOpen)
	{
		_owner = owner;
		IsFirstOpen = firstOpen;
	}
	internal Guid FixtureId => _owner.FixtureId;
	internal string CanonicalPath => _owner.CanonicalPath;
	internal bool IsFirstOpen { get; }
	internal void Initialized() => _owner.Initialized(this);
	internal void InitializationFailed() => _owner.InitializationFailed(this);
	internal void Quarantine() => _owner.Quarantine(this);
	public void Dispose() => _owner.Release(this);
}

/// <summary>Managed ownership disposition, shared by lease release and artifact cleanup.</summary>
internal sealed class SemanticFixtureOwnership
{
	internal bool IsRevoked { get; private set; }
	internal bool IsQuarantined { get; private set; }
	internal bool MayReleaseLease => !IsQuarantined;
	internal bool MayRemoveArtifacts => IsRevoked && !IsQuarantined;
	internal void Quarantine() => IsQuarantined = true;
	internal void Revoke(bool hasActiveLease)
	{
		if (hasActiveLease && !IsQuarantined)
			throw new InvalidOperationException("Close the fixture's storage before revoking issuance.");
		IsRevoked = true;
	}
}
