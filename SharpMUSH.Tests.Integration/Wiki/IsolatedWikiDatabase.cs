using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// Private provider storage for assertions about global ordering, counts, or offset pagination.
/// Does not change the shared server's database or process environment.
/// </summary>
internal sealed class IsolatedWikiDatabase(LightningDatabase database, string path) : IAsyncDisposable
{
	public IWikiService Wiki { get; } = database;

	public static Task<IsolatedWikiDatabase> CreateAsync()
	{
		var path = Path.Join(Path.GetTempPath(), $"sharpmush-wiki-isolated-{Guid.NewGuid():N}");
		var lightning = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 },
			Substitute.For<IPasswordService>(), relations: null);
		return Task.FromResult(new IsolatedWikiDatabase(lightning, path));
	}

	public async ValueTask DisposeAsync()
	{
		await database.DisposeAsync();
		if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
	}
}
