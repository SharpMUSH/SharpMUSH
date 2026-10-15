using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <inheritdoc cref="ISystemNames"/>
/// <remarks>
/// The registry is read once and kept until the installed packages change, which it notices by their
/// ids and revisions: an install, upgrade or uninstall moves one of them.
/// </remarks>
public sealed class SystemNames(IPackageRegistryService registry) : ISystemNames
{
	private sealed record Snapshot(string Signature, IReadOnlyDictionary<int, (DBRef Object, string Package)> Owners);

	private Snapshot? _snapshot;
	private readonly SemaphoreSlim _rebuild = new(1, 1);

	public async ValueTask<string> SystemOfAsync(DBRef obj, CancellationToken cancellationToken = default)
	{
		var owners = (await CurrentAsync(cancellationToken)).Owners;
		return owners.TryGetValue(obj.Number, out var owner) && owner.Object.SameObjectAs(obj)
			? owner.Package
			: obj.ToString();
	}

	private async ValueTask<Snapshot> CurrentAsync(CancellationToken cancellationToken)
	{
		var packages = await registry.GetInstalledPackagesAsync();
		var signature = string.Join(';', packages.Select(package => $"{package.Id}@{package.CurrentRevision}"));
		if (_snapshot is { } known && known.Signature == signature) return known;

		await _rebuild.WaitAsync(cancellationToken);
		try
		{
			if (_snapshot is { } rebuilt && rebuilt.Signature == signature) return rebuilt;
			var owners = new Dictionary<int, (DBRef, string)>();
			foreach (var package in packages)
			{
				foreach (var record in await registry.GetPackageObjectsAsync(package.Id))
				{
					if (DBRef.TryParse(record.Objid, out var parsed) && parsed is { } objid)
						owners.TryAdd(objid.Number, (objid, package.Id));
				}
			}

			return _snapshot = new Snapshot(signature, owners);
		}
		finally
		{
			_rebuild.Release();
		}
	}
}
