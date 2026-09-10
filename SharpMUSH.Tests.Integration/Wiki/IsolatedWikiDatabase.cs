using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Database.SurrealDB;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;
using SurrealDb.Net;

namespace SharpMUSH.Tests.Integration.Wiki;

/// <summary>
/// Private provider storage for assertions about global ordering, counts, or offset pagination.
/// Does not change the shared server's database or process environment.
/// </summary>
internal sealed class IsolatedWikiDatabase(IWikiService wiki, IAsyncDisposable owner, string? path = null)
	: IAsyncDisposable
{
	public IWikiService Wiki { get; } = wiki;

	public static async Task<IsolatedWikiDatabase> CreateAsync()
	{
		if (string.Equals(Environment.GetEnvironmentVariable("SHARPMUSH_DATABASE_PROVIDER"),
			"surrealdb", StringComparison.OrdinalIgnoreCase))
		{
			var services = new ServiceCollection();
			services.AddSurreal($"Endpoint=mem://;Namespace=isolated_wiki;Database=test_{Guid.NewGuid():N}")
				.AddInMemoryProvider();
			var provider = services.BuildServiceProvider();
			try
			{
				var client = provider.GetRequiredService<ISurrealDbClient>();
				await client.Connect();
				var database = new SurrealDatabase(NullLogger<SurrealDatabase>.Instance, client,
					Substitute.For<IPasswordService>(), Substitute.For<IObjectRelationLoader>());
				await database.Migrate();
				return new(database, provider);
			}
			catch
			{
				await provider.DisposeAsync();
				throw;
			}
		}

		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-wiki-isolated-{Guid.NewGuid():N}");
		var lightning = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 256L << 20 },
			Substitute.For<IPasswordService>(), relations: null);
		return new(lightning, lightning, path);
	}

	public async ValueTask DisposeAsync()
	{
		await owner.DisposeAsync();
		if (path is not null && Directory.Exists(path)) Directory.Delete(path, recursive: true);
	}
}
