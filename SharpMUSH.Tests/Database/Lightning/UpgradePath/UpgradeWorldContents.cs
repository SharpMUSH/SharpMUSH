using DotNext.Threading;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Database.Lightning.UpgradePath;

/// <summary>
/// What an upgrade-path world holds. <see cref="WriteAsync"/> runs once, on the release a fixture world is
/// written with (<see cref="UpgradeWorldFixtureWriter"/>); <see cref="VerifyAsync"/> runs on every later server,
/// against that stored world. Writing and reading go through the provider's public surface, so a change to a
/// record's stored shape that no migration carries shows up here as a value that no longer reads back.
/// </summary>
/// <remarks>
/// A new kind of stored data belongs in a fixture written by the release that first stores it: checking it in
/// <see cref="VerifyAsync"/> for an older world fails, because that world never held it. Give the check the kept
/// world's format and make it only for worlds of that format or newer; do the same for a value a migration
/// deliberately changes.
/// </remarks>
internal static class UpgradeWorldContents
{
	/// <summary>A fixed time, so that two writes of the world hold the same values.</summary>
	internal static readonly DateTimeOffset WrittenAt = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

	private static long WrittenAtMs => WrittenAt.ToUnixTimeMilliseconds();

	// The provider stamps a lock's dbrefs with the object's creation time, so a lock names the traveller by objid.
	private static string BasicLock => $"=#12:{WrittenAtMs}";
	private static string UseLock => $"flag^wizard|#12:{WrittenAtMs}";

	// The dbrefs a fresh world hands out after the seeded #0-#9, in the order WriteAsync creates them.
	internal static readonly DBRef Plaza = new(10);
	internal static readonly DBRef Annex = new(11);
	internal static readonly DBRef Traveller = new(12);
	internal static readonly DBRef Lantern = new(13);
	internal static readonly DBRef LanternParent = new(14);
	internal static readonly DBRef NorthExit = new(15);

	/// <summary>The seeded #0-#9 and the six objects above.</summary>
	internal const int ObjectCount = 16;

	internal const string TravellerName = "Upgrade_Traveller";
	internal const string ChannelName = "UpgradeChat";
	internal const string AccountName = "upgrade-account";
	internal const string AccountEmail = "upgrade@example.invalid";
	internal const string AccountHash = "stored-account-hash";
	internal const string WikiTitle = "Upgrade Notes";
	internal const string ExpandedDataType = "UpgradeWorldNote";
	internal const string ServerDataType = "UpgradeWorldServerNote";
	internal const string CustomPower = "UPGRADE_POWER";

	/// <summary>Expanded data as a package or plugin stores it: a record serialized to JSON.</summary>
	internal sealed record UpgradeWorldNote(string Text, int Count, string[] Tags);

	private static readonly UpgradeWorldNote ObjectNote = new("lantern note", 3, ["brass", "oil"]);
	private static readonly UpgradeWorldNote ServerNote = new("server note", 7, ["upgrade"]);

	/// <summary>Over the size at which the provider compresses an attribute value, and not one repeated byte.</summary>
	private static readonly string LongValue = string.Join(" ",
		Enumerable.Range(0, 2000).Select(i => $"line{i}:{(i * 7919) % 1000}"));

	private static MString Styled() => MarkupText.Concat([
		MarkupText.Wrap(AnsiMarkup.Create(foreground: new AnsiColor.Standard(1, false)), MarkupText.Plain("A red word")),
		MarkupText.Plain(" and plain text.")
	]);

	private static async Task<AnySharpObject> Node(LightningDatabase db, DBRef dbref)
		=> (await db.GetObjectNodeAsync(dbref)).Expect<AnySharpObject>();

	private static async Task<AnySharpContainer> Container(LightningDatabase db, DBRef dbref)
		=> (await Node(db, dbref)).AsOptionalContainer.Expect<AnySharpContainer>();

	private static async Task<SharpPlayer> Player(LightningDatabase db, DBRef dbref)
		=> (await Node(db, dbref)).Expect<SharpPlayer>();

	private static void Created(DBRef actual, DBRef expected, string what)
	{
		if (actual.Number != expected.Number)
			throw new InvalidOperationException($"{what} was created as #{actual.Number}, not #{expected.Number}: write the fixture into a fresh world.");
	}

	public static async Task WriteAsync(LightningDatabase db)
	{
		var god = await Player(db, new DBRef(1));

		Created(await db.CreateRoomAsync("Upgrade Plaza", god, WrittenAtMs, WrittenAtMs), Plaza, "Upgrade Plaza");
		Created(await db.CreateRoomAsync("Upgrade Annex", god, WrittenAtMs, WrittenAtMs), Annex, "Upgrade Annex");
		Created(await db.CreatePlayerAsync(TravellerName, "traveller-password", Plaza, Plaza, 25,
			creationTime: WrittenAtMs, modifiedTime: WrittenAtMs), Traveller, TravellerName);
		var traveller = await Player(db, Traveller);
		var inventory = await Container(db, Traveller);
		var plaza = await Container(db, Plaza);
		Created(await db.CreateThingAsync("Upgrade Lantern", inventory, traveller, plaza, WrittenAtMs, WrittenAtMs), Lantern, "Upgrade Lantern");
		Created(await db.CreateThingAsync("Upgrade Lantern Parent", plaza, god, plaza, WrittenAtMs, WrittenAtMs), LanternParent, "Upgrade Lantern Parent");
		Created(await db.CreateExitAsync("North", ["n", "nor"], plaza, god, WrittenAtMs, WrittenAtMs), NorthExit, "North");

		await db.LinkExitAsync((await Node(db, NorthExit)).Expect<SharpExit>(), await Container(db, Annex));
		await db.LinkRoomAsync((await Node(db, Plaza)).Expect<SharpRoom>(), (await Node(db, Annex)).AsOptionalContainer);

		var lantern = await Node(db, Lantern);
		await db.SetObjectParent(lantern, await Node(db, LanternParent));
		await db.SetObjectZone(lantern, await Node(db, Plaza));
		await db.SetObjectFlagAsync(lantern, (await db.GetObjectFlagAsync("DARK"))!);
		await db.SetObjectFlagAsync(await Node(db, Plaza), (await db.GetObjectFlagAsync("TRANSPARENT"))!);
		// A built-in power is stored as a grant; one made with @power/add as an object's power.
		await db.SetObjectPowerAsync(await Node(db, Traveller), (await db.GetPowerAsync("See_All"))!);
		await db.SetObjectPowerAsync(await Node(db, Traveller),
			(await db.CreatePowerAsync(CustomPower, ["UPOWER"], "", false, [], [], ["PLAYER"]))!);
		await db.SetLockAsync(lantern.Object(), "Basic", new SharpLockData(BasicLock) { Creator = Traveller });
		await db.SetLockAsync(lantern.Object(), "Use", new SharpLockData(UseLock) { Creator = new DBRef(1) });

		await db.SetAttributeAsync(Lantern, ["DESCRIBE"], Styled(), traveller);
		await db.SetAttributeAsync(Lantern, ["DATA", "BRANCH", "LEAF"], MarkupText.Plain("deep value"), traveller);
		await db.SetAttributeAsync(Lantern, ["LONGTEXT"], MarkupText.Plain(LongValue), traveller);
		await db.SetAttributeAsync(LanternParent, ["INHERITED"], MarkupText.Plain("from the parent"), god);
		await db.SetAttributeFlagAsync(lantern.Object(), ["DATA"], (await db.GetAttributeFlagAsync("no_inherit"))!);
		await db.SetAttributeFlagAsync(lantern.Object(), ["DESCRIBE"], (await db.GetAttributeFlagAsync("visual"))!);

		await db.SendMailAsync(god.Object, traveller, Mail("Welcome", "First mail.", "INBOX"));
		await db.SendMailAsync(god.Object, traveller, Mail("Archived", "Second mail.", "ARCHIVE"));

		await db.CreateChannelAsync(MarkupText.Plain(ChannelName), ["Player"], god);
		var channel = (await db.GetChannelAsync(ChannelName))!;
		await db.AddUserToChannelAsync(channel, await Node(db, Traveller));
		await db.UpdateChannelUserStatusAsync(channel, await Node(db, Traveller),
			new SharpChannelStatus(Combine: true, Gagged: false, Hide: false, Mute: true, Title: MarkupText.Plain("Wanderer")));

		var account = await db.CreateAccountAsync(AccountName, AccountEmail, AccountHash);
		await db.LinkCharacterToAccountAsync(account.Id!, Traveller);

		await db.SetExpandedObjectData(lantern.Object().Id!, ExpandedDataType, ObjectNote);
		await db.SetExpandedServerData(ServerDataType, ServerNote);

		var wiki = new WikiStoreService(db, new WikiMarkdigPipeline());
		var page = (await wiki.CreateAsync(WikiTitle, "First **revision**.", "#12", WikiNamespace.Main, "en", ["Lore"])).Expect<WikiPage>();
		await wiki.UpdateAsync(page.Id, "Second **revision**.", "#1", "second edit");
		await wiki.SetMetadataAsync(page.Id, ["Lore", "Upgrade"], published: true);
	}

	private static SharpMail Mail(string subject, string content, string folder) => new()
	{
		DateSent = WrittenAt,
		Fresh = true,
		Read = false,
		Tagged = false,
		Urgent = false,
		Forwarded = false,
		Cleared = false,
		Folder = folder,
		Content = MarkupText.Plain(content),
		Subject = MarkupText.Plain(subject),
		From = new AsyncLazy<AnyOptionalSharpObject>(_ => throw new InvalidOperationException("unused on send"))
	};

	public static async Task VerifyAsync(LightningDatabase db)
	{
		await VerifyObjectsAsync(db);
		await VerifyAttributesAsync(db);
		await VerifyMailAsync(db);
		await VerifyChannelAsync(db);
		await VerifyAccountAsync(db);
		await VerifyExpandedDataAsync(db);
		await VerifyWikiAsync(db);
	}

	private static async Task VerifyObjectsAsync(LightningDatabase db)
	{
		await Assert.That((await Node(db, Plaza)).Object().Name).IsEqualTo("Upgrade Plaza");
		await Assert.That((await Node(db, Annex)).Object().Name).IsEqualTo("Upgrade Annex");

		var traveller = await Player(db, Traveller);
		await Assert.That(traveller.Object.Name).IsEqualTo(TravellerName);
		await Assert.That(traveller.Object.CreationTime).IsEqualTo(WrittenAtMs);
		await Assert.That(traveller.Quota).IsEqualTo(25);
		await Assert.That(await db.GetPlayerRefsByNameOrAliasAsync(TravellerName).Select(found => found.Number).ToListAsync())
			.IsEquivalentTo([Traveller.Number]);
		await Assert.That((await db.GetLocationRefAsync(Traveller)).Expect<DBRef>().Number).IsEqualTo(Plaza.Number);
		await Assert.That((await traveller.Object.Grants.WithCancellation(CancellationToken.None)).Has(PortalPermission.GamePower("See_All"))).IsTrue();
		await Assert.That(await traveller.Object.Powers.Value.Select(power => power.Name).ToListAsync()).Contains(CustomPower);
		await Assert.That((await db.GetPowerAsync(CustomPower))?.Aliases).IsEquivalentTo(["UPOWER"]);

		var lantern = (await Node(db, Lantern)).Object();
		await Assert.That(lantern.Name).IsEqualTo("Upgrade Lantern");
		await Assert.That((await db.GetLocationRefAsync(Lantern)).Expect<DBRef>().Number).IsEqualTo(Traveller.Number);
		await Assert.That((await lantern.Owner.WithCancellation(CancellationToken.None)).Object.DBRef.Number).IsEqualTo(Traveller.Number);
		await Assert.That((await lantern.Parent.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().DBRef.Number)
			.IsEqualTo(LanternParent.Number);
		await Assert.That((await lantern.Zone.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().DBRef.Number)
			.IsEqualTo(Plaza.Number);
		await Assert.That(await lantern.Flags.Value.Select(flag => flag.Name).ToListAsync()).Contains("DARK");
		await Assert.That(await (await Node(db, Plaza)).Object().Flags.Value.Select(flag => flag.Name).ToListAsync()).Contains("TRANSPARENT");
		await Assert.That(lantern.Locks["Basic"].LockString).IsEqualTo(BasicLock);
		await Assert.That(lantern.Locks["Basic"].Creator?.Number).IsEqualTo(Traveller.Number);
		await Assert.That(lantern.Locks["Use"].LockString).IsEqualTo(UseLock);

		var exits = await db.GetExitsAsync(Plaza).ToListAsync();
		var north = exits.Single(exit => exit.Object.DBRef.Number == NorthExit.Number);
		await Assert.That(north.Object.Name).IsEqualTo("North");
		await Assert.That(north.Aliases).IsEquivalentTo(["n", "nor"]);
		await Assert.That((await db.GetRelationRefAsync(ObjectRelationKind.Home, NorthExit)).Expect<DBRef>().Number).IsEqualTo(Annex.Number)
			.Because("an exit's destination is its home edge");
		await Assert.That(await db.GetEntrancesAsync(Annex).Select(exit => exit.Object.DBRef.Number).ToListAsync()).Contains(NorthExit.Number);
		await Assert.That((await db.GetRelationRefAsync(ObjectRelationKind.Home, Plaza)).Expect<DBRef>().Number).IsEqualTo(Annex.Number)
			.Because("the Plaza's drop-to is the Annex");
		await Assert.That(await db.GetZoneMemberRefsAsync(Plaza).Select(member => member.Number).ToListAsync()).Contains(Lantern.Number);
	}

	private static async Task<SharpAttribute> Leaf(LightningDatabase db, DBRef dbref, params string[] path)
		=> await db.GetAttributeAsync(dbref, path).LastAsync();

	private static async Task VerifyAttributesAsync(LightningDatabase db)
	{
		var describe = await Leaf(db, Lantern, "DESCRIBE");
		await Assert.That(describe.Value.Render(MarkupFormat.Ansi)).IsEqualTo(Styled().Render(MarkupFormat.Ansi));
		await Assert.That(describe.Flags.Select(flag => flag.Name.ToLowerInvariant())).Contains("visual");
		await Assert.That((await describe.Owner.WithCancellation(CancellationToken.None))!.Object.DBRef.Number).IsEqualTo(Traveller.Number);

		var leaf = await Leaf(db, Lantern, "DATA", "BRANCH", "LEAF");
		await Assert.That(leaf.Value.ToPlainText()).IsEqualTo("deep value");
		await Assert.That(leaf.LongName).IsEqualTo("DATA`BRANCH`LEAF");
		await Assert.That((await Leaf(db, Lantern, "DATA")).Flags.Select(flag => flag.Name.ToLowerInvariant())).Contains("no_inherit");

		await Assert.That((await Leaf(db, Lantern, "LONGTEXT")).Value.ToPlainText()).IsEqualTo(LongValue);

		var inherited = await db.GetAttributeWithInheritanceAsync(Lantern, ["INHERITED"]).LastAsync();
		await Assert.That(inherited.Attributes.Last().Value.ToPlainText()).IsEqualTo("from the parent");
	}

	private static async Task VerifyMailAsync(LightningDatabase db)
	{
		var traveller = await Player(db, Traveller);
		await Assert.That(await db.GetMailFoldersAsync(traveller)).IsEquivalentTo(["ARCHIVE", "INBOX"]);

		var inbox = await db.GetIncomingMailsAsync(traveller, "INBOX").ToListAsync();
		await Assert.That(inbox.Select(mail => mail.Subject.ToPlainText())).IsEquivalentTo(["Welcome"]);
		await Assert.That(inbox[0].Content.ToPlainText()).IsEqualTo("First mail.");
		await Assert.That(inbox[0].DateSent).IsEqualTo(WrittenAt);
		await Assert.That((await inbox[0].From.WithCancellation(CancellationToken.None)).Expect<AnySharpObject>().Object().DBRef.Number).IsEqualTo(1);

		var archive = await db.GetIncomingMailsAsync(traveller, "ARCHIVE").ToListAsync();
		await Assert.That(archive.Select(mail => mail.Subject.ToPlainText())).IsEquivalentTo(["Archived"]);
		await Assert.That(await db.GetAllSentMailsAsync((await Player(db, new DBRef(1))).Object).CountAsync()).IsEqualTo(2);
	}

	private static async Task VerifyChannelAsync(LightningDatabase db)
	{
		var channel = await db.GetChannelAsync(ChannelName);
		await Assert.That(channel).IsNotNull();
		await Assert.That(channel!.Name.ToPlainText()).IsEqualTo(ChannelName);
		await Assert.That(channel.OwnerDBRef.Number).IsEqualTo(1);
		await Assert.That(channel.Privs).Contains("Player");

		var status = (await db.GetChannelMemberStatusAsync(channel, Traveller)).Expect<SharpChannelStatus>();
		await Assert.That(status.Mute).IsTrue();
		await Assert.That(status.Combine).IsTrue();
		await Assert.That(status.Title?.ToPlainText()).IsEqualTo("Wanderer");
		await Assert.That(await db.GetMemberChannelsAsync(await Node(db, Traveller)).Select(member => member.Name.ToPlainText()).ToListAsync())
			.Contains(ChannelName);
	}

	private static async Task VerifyAccountAsync(LightningDatabase db)
	{
		var account = await db.GetAccountByUsernameAsync(AccountName);
		await Assert.That(account).IsNotNull();
		await Assert.That((await db.GetAccountByEmailAsync(AccountEmail))?.Id).IsEqualTo(account!.Id);
		await Assert.That((await db.GetCharactersForAccountAsync(account.Id!)).Select(character => character.Object.DBRef.Number))
			.IsEquivalentTo([Traveller.Number]);
		await Assert.That((await db.GetAccountForCharacterAsync(Traveller))?.Id).IsEqualTo(account.Id);
	}

	private static async Task VerifyExpandedDataAsync(LightningDatabase db)
	{
		var lantern = (await Node(db, Lantern)).Object();
		var note = await db.GetExpandedObjectData<UpgradeWorldNote>(lantern.Id!, ExpandedDataType);
		await Assert.That(note).IsNotNull();
		await Assert.That(note!.Text).IsEqualTo(ObjectNote.Text);
		await Assert.That(note.Count).IsEqualTo(ObjectNote.Count);
		await Assert.That(note.Tags).IsEquivalentTo(ObjectNote.Tags);

		var server = await db.GetExpandedServerData<UpgradeWorldNote>(ServerDataType);
		await Assert.That(server?.Text).IsEqualTo(ServerNote.Text);
	}

	private static async Task VerifyWikiAsync(LightningDatabase db)
	{
		var wiki = new WikiStoreService(db, new WikiMarkdigPipeline());
		var page = (await wiki.GetBySlugAsync(WikiHelpers.Slugify(WikiTitle))).Expect<WikiPage>();
		await Assert.That(page.Title).IsEqualTo(WikiTitle);
		await Assert.That(page.MarkdownSource).IsEqualTo("Second **revision**.");
		await Assert.That(page.RevisionNumber).IsEqualTo(2);
		await Assert.That(page.AuthorDbref).IsEqualTo("#12");
		await Assert.That(page.LastEditorDbref).IsEqualTo("#1");
		await Assert.That(page.Categories).IsEquivalentTo(["lore", "upgrade"]);
		await Assert.That(page.Published).IsTrue();
		await Assert.That(await wiki.GetByCategoryAsync("upgrade", 0, 10, WikiVisibility.PublishedOnly))
			.Count().IsEqualTo(1);
	}
}
