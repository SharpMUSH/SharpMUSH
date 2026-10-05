using DotNext.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Benchmarks;

/// <summary>
/// A bare migrated Lightning world per benchmark class, opened directly: these measure the provider's
/// reads, so no host, NATS or cache sits in front of them.
/// </summary>
public abstract class LightningStoreBenchmark
{
	protected LightningDatabase Db = null!;
	private string _path = null!;

	protected async Task OpenAsync()
	{
		_path = LightningBaseBenchmark.CreateDataDirectory();
		Db = new LightningDatabase(NullLogger<LightningDatabase>.Instance,
			new LightningStoreOptions { Path = _path, MapSize = 8L << 30, Sync = LightningSyncMode.Periodic },
			Substitute.For<IPasswordService>(), relations: null);
		await Db.Migrate();
	}

	[GlobalCleanup]
	public async ValueTask Cleanup()
	{
		await Db.DisposeAsync();
		try
		{
			if (Directory.Exists(_path)) Directory.Delete(_path, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort, as in LightningBaseBenchmark.
		}
	}

	protected static string Body(int size) => new('x', size);
}

/// <summary>
/// #1459: owner- and type-filtered searches as the world grows around a fixed matching population. The
/// indexed reads should stay flat across <see cref="WorldSize"/>; the name-only search is the scan they
/// are compared with.
/// </summary>
[BenchmarkCategory("Read Index", "Lightning")]
public class LightningFilteredSearchBenchmarks : LightningStoreBenchmark
{
	[Params(1_000, 20_000)] public int WorldSize;

	private DBRef _owner;

	[GlobalSetup]
	public async Task Setup()
	{
		await OpenAsync();
		var god = await Db.GetObjectNodeAsync(new DBRef(1)) is AnySharpObject and SharpPlayer g ? g : throw new InvalidOperationException();
		var room = await Db.GetObjectNodeAsync(new DBRef(0)) is AnySharpObject { IsContainer: true } r ? r.AsContainer : throw new InvalidOperationException();
		_owner = await Db.CreatePlayerAsync("BenchOwner", "pw", new DBRef(0), new DBRef(0), 0);
		var owner = await Db.GetObjectNodeAsync(_owner) is AnySharpObject and SharpPlayer o ? o : throw new InvalidOperationException();
		for (var i = 0; i < 20; i++) await Db.CreateThingAsync($"Owned{i}", room, owner, room);
		for (var i = 0; i < 19; i++) await Db.CreatePlayerAsync($"BenchPlayer{i}", "pw", new DBRef(0), new DBRef(0), 0);
		for (var i = 0; i < WorldSize; i++) await Db.CreateThingAsync($"Filler{i}", room, god, room);
	}

	[Benchmark(Description = "Owner = fixed 20 (owner index)")]
	public async Task<int> ByOwner() => (await Db.GetFilteredObjectsAsync(new ObjectSearchFilter { Owner = _owner }).ToListAsync()).Count;

	[Benchmark(Description = "All players, fixed 21 (type index)")]
	public async Task<int> AllPlayers() => (await Db.GetAllPlayersAsync().ToListAsync()).Count;

	[Benchmark(Baseline = true, Description = "Name only (scan)")]
	public async Task<int> ByNameScan() => (await Db.GetFilteredObjectsAsync(new ObjectSearchFilter { NamePattern = "Owned1" }).ToListAsync()).Count;
}

/// <summary>#1463: one mail and the folder list as the mailbox and the bodies grow.</summary>
[BenchmarkCategory("Read Index", "Lightning")]
public class LightningMailReadBenchmarks : LightningStoreBenchmark
{
	[Params(100, 2_000)] public int MailboxSize;
	[Params(100, 10_000)] public int BodySize;

	private SharpPlayer _recipient = null!;

	[GlobalSetup]
	public async Task Setup()
	{
		await OpenAsync();
		var sender = await Db.GetObjectNodeAsync(new DBRef(1)) is AnySharpObject and SharpPlayer s ? s : throw new InvalidOperationException();
		var recipient = await Db.CreatePlayerAsync("BenchReader", "pw", new DBRef(0), new DBRef(0), 0);
		_recipient = await Db.GetObjectNodeAsync(recipient) is AnySharpObject and SharpPlayer p ? p : throw new InvalidOperationException();
		var body = MarkupText.Plain(Body(BodySize));
		for (var i = 0; i < MailboxSize; i++)
		{
			await Db.SendMailAsync(sender.Object, _recipient, new SharpMail
			{
				DateSent = DateTimeOffset.UtcNow, Fresh = true, Read = false, Tagged = false, Urgent = false, Forwarded = false,
				Cleared = false, Folder = i % 4 == 0 ? "SAVED" : "INBOX", Content = body, Subject = MarkupText.Plain($"m{i}"),
				From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException())
			});
		}
	}

	[Benchmark(Description = "GetIncomingMailAsync(INBOX, 5)")]
	public async Task<SharpMail?> FifthMail() => await Db.GetIncomingMailAsync(_recipient, "INBOX", 5);

	[Benchmark(Description = "GetMailFoldersAsync")]
	public async Task<string[]> Folders() => await Db.GetMailFoldersAsync(_recipient);
}

/// <summary>#1462: small wiki list pages and revision pages as page bodies, page counts and histories grow.</summary>
[BenchmarkCategory("Read Index", "Lightning")]
public class LightningWikiReadBenchmarks : LightningStoreBenchmark
{
	[Params(200, 2_000)] public int PageCount;
	[Params(1_000, 50_000)] public int BodySize;
	[Params(10, 1_000)] public int RevisionCount;

	private string _historyPage = null!;

	[GlobalSetup]
	public async Task Setup()
	{
		await OpenAsync();
		IWikiStore wiki = Db;
		var body = Body(BodySize);
		var at = DateTimeOffset.UtcNow;
		for (var i = 0; i < PageCount; i++)
		{
			await wiki.CreatePageAsync(new WikiPage("", $"page_{i}", $"Page {i}", "main", body, body, body, "#1", "#1", at, at.AddSeconds(i), 1)
			{
				Categories = i % 10 == 0 ? ["lore", "bench"] : ["general", "bench"]
			});
		}

		_historyPage = (await wiki.GetPagesAsync("main", 0, 1, WikiVisibility.All))[0].Id;
		for (var i = 2; i <= RevisionCount; i++)
		{
			await wiki.UpdatePageBodyAsync(_historyPage, new WikiBody(body, body, body), "#1", null, at);
		}
	}

	[Benchmark(Description = "GetRecentPagesAsync(10)")]
	public async Task<int> Recent() => (await ((IWikiStore)Db).GetRecentPagesAsync(10, WikiVisibility.All)).Count;

	[Benchmark(Description = "GetPagesByCategoryAsync(lore, 0, 10)")]
	public async Task<int> Category() => (await ((IWikiStore)Db).GetPagesByCategoryAsync("lore", 0, 10, WikiVisibility.All)).Count;

	[Benchmark(Description = "GetRevisionsAsync(skip 0, take 20)")]
	public async Task<int> RevisionsHead() => (await ((IWikiStore)Db).GetRevisionsAsync(_historyPage, "", 0, 20)).Count;

	[Benchmark(Description = "GetRevisionsBeforeAsync(oldest page of 20)")]
	public async Task<int> RevisionsCursorTail() => (await ((IWikiStore)Db).GetRevisionsBeforeAsync(_historyPage, "", 21, 20)).Count;
}

/// <summary>#1458: sweeping a fixed backlog of expired sessions as the live session count grows.</summary>
[BenchmarkCategory("Read Index", "Lightning")]
public class LightningSessionSweepBenchmarks : LightningStoreBenchmark
{
	[Params(1_000, 50_000)] public int LiveSessions;

	[GlobalSetup]
	public async Task Setup()
	{
		await OpenAsync();
		var live = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
		for (var i = 0; i < LiveSessions; i++)
		{
			await Db.UpsertSessionAsync(new SharpSession { Token = $"live{i}", AccountId = $"a{i % 100}", OriginIp = $"10.0.{i % 250}.1", ExpiryUnixMs = live, TtlMs = 1 });
		}
	}

	[IterationSetup]
	public void AddBacklog()
	{
		for (var i = 0; i < 100; i++)
		{
			Db.UpsertSessionAsync(new SharpSession { Token = $"old{i}", AccountId = "old", OriginIp = "192.0.2.1", ExpiryUnixMs = 1_000 + i, TtlMs = 1 })
				.AsTask().GetAwaiter().GetResult();
		}
	}

	[Benchmark(Description = "DeleteExpiredSessionsAsync — 100 expired")]
	public async Task<int> Sweep() => await Db.DeleteExpiredSessionsAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), 500);
}
