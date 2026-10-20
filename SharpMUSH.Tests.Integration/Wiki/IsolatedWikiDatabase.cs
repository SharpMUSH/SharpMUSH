using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// Private provider storage for assertions about global ordering, counts, or offset pagination.
/// Does not change the shared server's database or process environment.
/// </summary>
internal sealed class IsolatedWikiDatabase(LightningDatabase database, string path) : IAsyncDisposable
{
	public IWikiService Wiki { get; } = new WikiStoreService(database, new WikiMarkdigPipeline());

	public static async Task<IsolatedWikiDatabase> CreateAsync()
	{
		var scratch = Environment.GetEnvironmentVariable("TMPDIR") ?? Path.Combine(AppContext.BaseDirectory, "work");
		Directory.CreateDirectory(scratch);
		var path = Path.Join(scratch, $"sharpmush-wiki-isolated-{Guid.NewGuid():N}");
		var lightning = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 },
			Substitute.For<IPasswordService>(), relations: null);
		try
		{
			await lightning.Migrate();
			return new IsolatedWikiDatabase(lightning, path);
		}
		catch (Exception startupFailure)
		{
			try { await lightning.DisposeAsync(); }
			catch (Exception cleanupFailure)
			{
				throw new AggregateException($"Private wiki startup failed; world retained at {path} because disposal failed.", startupFailure, cleanupFailure);
			}
			try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
			catch (Exception cleanupFailure)
			{
				throw new AggregateException($"Private wiki startup and scratch cleanup failed at {path}.", startupFailure, cleanupFailure);
			}
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		await database.DisposeAsync();
		if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
	}
}
