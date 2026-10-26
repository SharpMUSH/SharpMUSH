using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning;

/// <summary>
/// A world's stored configuration predates every setting added after it was written. Before these defaults
/// were filled in, a required section the document lacked (AsciiTranslations, for a world from before it
/// existed) failed the read and the server would not start.
/// </summary>
public class StoredOptionsDefaultsTests
{
	private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = null };

	private string _path = null!;
	private LightningDatabase _db = null!;

	[Before(Test)]
	public async Task Setup()
	{
		_path = Path.Combine(Path.GetTempPath(), "sharpmush-lmdb-" + Guid.NewGuid().ToString("N"));
		_db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 256L << 20 }, Substitute.For<IPasswordService>(), relations: null);
		await _db.Migrate();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		await _db.DisposeAsync();
		try
		{
			Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort, as PushSubscriptionStoreTests: the lock file can outlive the writer thread briefly.
		}
	}

	private static JsonObject Defaults() => JsonSerializer.SerializeToNode(SharpMUSHOptions.Default(), Json)!.AsObject();

	[Test]
	public async Task AWorldMissingASectionAndASettingStartsWithTheirDefaultsAndKeepsItsOwnValues()
	{
		var stored = Defaults();
		stored.Remove(nameof(SharpMUSHOptions.AsciiTranslations));
		stored[nameof(SharpMUSHOptions.Limit)]!.AsObject().Remove(nameof(LimitOptions.GuestOutputLimit));
		stored[nameof(SharpMUSHOptions.Net)]!.AsObject()[nameof(NetOptions.MudName)] = "Old Coast";
		await _db.SetExpandedServerData(nameof(SharpMUSHOptions), stored);

		await _db.Migrate();

		var read = await _db.GetExpandedServerData<SharpMUSHOptions>(nameof(SharpMUSHOptions));
		var shipped = SharpMUSHOptions.Default();
		await Assert.That(read!.AsciiTranslations.Translations).IsEquivalentTo(shipped.AsciiTranslations.Translations);
		await Assert.That(read.Limit.GuestOutputLimit).IsEqualTo(shipped.Limit.GuestOutputLimit);
		await Assert.That(read.Net.MudName).IsEqualTo("Old Coast");
	}

	[Test]
	public async Task ATableSettingIsNotMergedInto()
	{
		var stored = Defaults();
		var aliases = new JsonObject { ["@EMIT"] = new JsonArray("\\") };
		stored[nameof(SharpMUSHOptions.Alias)]!.AsObject()[nameof(AliasOptions.CommandAliases)] = aliases;

		var filled = StoredOptionsDefaults.Fill(stored, Defaults());

		await Assert.That(filled).IsEmpty();
		await Assert.That(stored[nameof(SharpMUSHOptions.Alias)]![nameof(AliasOptions.CommandAliases)]!.AsObject().Count)
			.IsEqualTo(1);
	}

	[Test]
	public async Task AStoredNullIsKept()
	{
		var stored = Defaults();
		stored[nameof(SharpMUSHOptions.Net)]!.AsObject()[nameof(NetOptions.MudUrl)] = null;

		var filled = StoredOptionsDefaults.Fill(stored, Defaults());

		await Assert.That(filled).IsEmpty();
		await Assert.That(stored[nameof(SharpMUSHOptions.Net)]![nameof(NetOptions.MudUrl)]).IsNull();
	}
}
