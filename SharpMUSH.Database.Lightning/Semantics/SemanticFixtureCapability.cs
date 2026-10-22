using System.Text.Json;
using SharpMUSH.Database.Lightning.Store;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>
/// In-process authority for one disposable lineage. Trusted orchestration respects its reservation
/// and never replaces paths during admission; this is not a defense against a malicious OS user.
/// </summary>
internal sealed partial class SemanticFixtureCapability : IDisposable
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
	private bool _claiming;
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

	/// <summary>
	/// Recovery tests only: adopts the issued world at <paramref name="path"/> after the process that owned it
	/// has exited. The reservation's exclusive lock is the proof: it is free only once its holder is gone. The
	/// marker must name this exact path, and the first open still checks the identity stored in the world.
	/// </summary>
	internal static SemanticFixtureCapability AdoptAfterOwnerExit(string path)
	{
		var canonical = Path.GetFullPath(path);
		RejectLinkedAncestors(canonical);
		var reservationPath = canonical + ".semantic-reservation";
		var reservation = new FileStream(reservationPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		try
		{
			var markerPath = Path.Combine(canonical, MarkerName);
			if ((File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0 || new FileInfo(markerPath).Length > 4096)
				throw new InvalidOperationException("Invalid fixture marker.");
			var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath));
			if (marker is not { Version: 1 } || marker.CanonicalPath != canonical)
				throw new InvalidOperationException("Fixture marker does not name the adopted path.");
			// The world was initialized by its first owner, and an active one was activated behind both package
			// fences, which activation requires: every open from here on is a reopen of that lineage.
			return new(canonical, marker.FixtureId, reservation, reservationPath)
			{ _claimed = true, _initialized = true, _installerFenceInstalled = true, _runnerFenceInstalled = true };
		}
		catch
		{
			reservation.Dispose();
			throw;
		}
	}

	private ulong _leaseEpoch;

	internal SemanticFixtureLease Claim(LightningStoreOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		bool firstOpen;
		lock (_gate)
		{
			if (_ownership.IsRevoked || _poisoned || _ownership.IsQuarantined || _phase == SemanticFixturePhase.Failed)
				throw new InvalidOperationException("Fixture issuance is revoked or incomplete.");
			if (_active is not null || _claiming) throw new InvalidOperationException("Fixture already has an active storage lease.");
			if (!string.Equals(Path.GetFullPath(options.Path), CanonicalPath, StringComparison.Ordinal))
				throw new InvalidOperationException("Fixture options do not name the issued path.");
			if (options.Sync != LightningSyncMode.Full) throw new InvalidOperationException("Fixtures require full-sync storage.");
			if (_claimed && !_initialized) throw new InvalidOperationException("First fixture migration never completed.");
			firstOpen = !_claimed;
			_claiming = true;
		}
		try
		{
			RejectLinkedAncestors(CanonicalPath);
			var markerPath = Path.Combine(CanonicalPath, MarkerName);
			try
			{
				if ((File.GetAttributes(markerPath) & FileAttributes.ReparsePoint) != 0 || new FileInfo(markerPath).Length > 4096)
					throw new InvalidOperationException("Invalid fixture marker.");
				var marker = JsonSerializer.Deserialize<Marker>(File.ReadAllText(markerPath));
				if (marker is not { Version: 1 } || marker.FixtureId != FixtureId || marker.CanonicalPath != CanonicalPath)
					throw new InvalidOperationException("Fixture marker does not match its issued identity.");
			}
			catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
			{
				throw new InvalidOperationException("Fixture marker is unavailable or invalid.", error);
			}
			AtomicWikiActivation? activation = null;
			if (firstOpen)
			{
				if (Directory.EnumerateFileSystemEntries(CanonicalPath).Any(entry => Path.GetFileName(entry) != MarkerName))
					throw new InvalidOperationException("A first fixture open requires an empty issued directory.");
			}
			else activation = LightningStore.RequireFixtureIdentity(options, FixtureId);
			lock (_gate)
			{
				_phase = activation is null ? SemanticFixturePhase.Inactive : SemanticFixturePhase.Active;
				_active = new SemanticFixtureLease(this, firstOpen, ++_leaseEpoch);
				_claimed = true;
				return _active;
			}
		}
		finally { lock (_gate) _claiming = false; }
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
			lease.MarkInitialized();
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
			if (HasRetainedOperations(lease)) throw new SemanticFixtureOperationInProgressException();
			if (lease.NativeOpenAttempted && !lease.NativeClosed)
				throw new InvalidOperationException("Close native fixture storage before releasing its lease.");
			if (!_initialized) _poisoned = true;
			_active = null;
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_reservationRemoved) return;
			_ownership.Revoke(_active is not null || _claiming);
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
	internal SemanticFixtureCapability Owner => _owner;
	internal SemanticFixtureLease(SemanticFixtureCapability owner, bool firstOpen, ulong epoch = 0)
	{
		_owner = owner;
		IsFirstOpen = firstOpen;
		Epoch = epoch;
	}
	/// <summary>Which claim of this fixture's storage this is; a reopen is a new epoch.</summary>
	internal ulong Epoch { get; }
	/// <summary>
	/// This lease's continuation key: random, in memory only, never persisted, so a reopen refuses every
	/// earlier continuation.
	/// </summary>
	internal byte[] ContinuationKey { get; } = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
	internal Guid FixtureId => _owner.FixtureId;
	internal string CanonicalPath => _owner.CanonicalPath;
	internal bool IsFirstOpen { get; }
	internal bool IsInitialized { get; private set; }
	internal bool IsClosing { get; set; }
	internal bool NativeOpenAttempted { get; set; }
	internal bool NativeClosed { get; set; }
	internal void MarkInitialized() => IsInitialized = true;
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
