using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Models.Portal.Applications;
using SharpMUSH.Library.Plugins;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Plugins.Discord;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordConfigurationTileTests
{
	[Test]
	public async Task DiscordContributesItsExistingProtectedAdminScreen()
	{
		var source = (IApplicationSource)new SharpMUSH.Plugins.Discord.DiscordPlugin();
		var tile = source.GetConfigurationTiles().Single();
		await Assert.That(tile.Route).IsEqualTo("/admin/discord");
		await Assert.That(tile.Permission).IsEqualTo(SharpMUSH.Plugins.Discord.DiscordPlugin.AdminPermission);
		await Assert.That(tile.HasSafeRoute()).IsTrue();
		await Assert.That(source.GetApplications().Count()).IsEqualTo(0);
	}

	[Test]
	public async Task ConfigurationMetadataSurvivesClosingAndReopeningTheWorld()
	{
		var path = Path.Join(Path.GetTempPath(), "application-tile-" + Guid.NewGuid().ToString("N"));
		var tile = new ApplicationConfigurationTile("example", "Example", "Settings", "Settings", "/apps/example/settings", "config.admin");
		var app = new RegisteredApplication("example", "Example", null, ApplicationKind.Page,
			"http/example/schema", null, null, PortalRole.Wizard, null, null, 0, ConfigurationTile: tile);
		LightningDatabase Open() => new(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = path, MapSize = 64L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		try
		{
			await using (var database = Open())
			{
				await database.Migrate();
				await database.UpsertApplicationAsync(app);
			}
			await using (var database = Open())
			{
				var stored = (await database.GetApplicationAsync("example")).Expect<RegisteredApplication>();
				await Assert.That(stored.ConfigurationTile).IsEqualTo(tile);
			}
		}
		finally { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
	}
}
