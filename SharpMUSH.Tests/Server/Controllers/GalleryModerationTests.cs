using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.API;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Authentication;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Server.Hubs;
using SharpMUSH.Server.Services;

namespace SharpMUSH.Tests.Server.Controllers;

/// <summary>
/// The gallery's moderation options (#2879), each off by default: terms an uploader accepts and the image keeps, a byte
/// limit that may leave official art out, portraits that wait for staff, and a lock that leaves galleries to staff.
/// Each test writes the gallery of a character of its own, through the real attribute store, with the options handed
/// to its own controller, so no test changes the shared world's configuration.
/// </summary>
public class GalleryModerationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IServiceProvider Services => WebAppFactoryArg.Services;

	private const string Owner = "account-owner";
	private const string Stranger = "account-stranger";
	private const string Terms = "I made this picture, or its artist lets me use it here.";

	private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

	private sealed class FixedTime : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => Now;
	}

	/// <summary>A character of the test's own, and its name.</summary>
	private async Task<(DBRef Dbref, string Name)> CharacterAsync()
	{
		var name = TestIsolationHelpers.GenerateUniqueName("Gal");
		var dbref = await TestIsolationHelpers.CreateNamedTestPlayerAsync(Services, Services.GetRequiredService<IMediator>(), name);
		return (dbref, name);
	}

	/// <summary>
	/// A controller for a caller on <paramref name="account"/> playing <paramref name="actingAs"/>: the account owns
	/// every character when it is <see cref="Owner"/>, and the caller holds <c>media.admin</c> when
	/// <paramref name="staff"/>.
	/// </summary>
	private GalleryController Controller(GalleryOptions gallery, string account, DBRef actingAs, bool staff,
		DirectoryInfo files, IEventService? events = null)
	{
		// Read before the substitute is configured: NSubstitute would take a call made inside Returns for its own.
		var current = Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue with { Gallery = gallery };
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(current);
		var accounts = Substitute.For<IAccountService>();
		accounts.GetAccountForCharacterAsync(Arg.Any<DBRef>(), Arg.Any<CancellationToken>())
			.Returns(new SharpAccount { Id = Owner, Username = "owner" });
		var authorization = Substitute.For<IAuthorizationService>();
		authorization.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<string>())
			.Returns(staff ? AuthorizationResult.Success() : AuthorizationResult.Failed());

		var controller = new GalleryController(
			new FileSystemWikiAssetService(files.FullName),
			Services.GetRequiredService<IMediator>(),
			Services.GetRequiredService<IAttributeService>(),
			Services.GetRequiredService<IPermissionService>(),
			Services.GetRequiredService<IVisibleWorldProjection>(),
			accounts,
			events ?? Substitute.For<IEventService>(),
			Services.GetRequiredService<IAdministrativeCapabilityService>(),
			options,
			authorization,
			new FixedTime(),
			NullLogger<GalleryController>.Instance);
		controller.ControllerContext = new ControllerContext
		{
			HttpContext = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(
				[
					new Claim(GameHub.CharacterDbrefClaim, actingAs.ToString()),
					new Claim(ClaimTypes.NameIdentifier, account),
				], AccountSessionAuthenticationHandler.SchemeName)),
			},
		};
		return controller;
	}

	/// <summary>A PNG of <paramref name="bytes"/> bytes: the store reads the type from the first ones.</summary>
	private static FormFile Png(string fileName, int bytes = 64)
	{
		var content = new byte[Math.Max(bytes, 16)];
		byte[] header = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
		header.CopyTo(content, 0);
		var stream = new MemoryStream(content);
		return new FormFile(stream, 0, stream.Length, "file", fileName) { Headers = new HeaderDictionary(), ContentType = "image/png" };
	}

	private static List<GalleryEntry> Gallery(IActionResult result) =>
		(List<GalleryEntry>)((OkObjectResult)result).Value!;

	private static string? Refusal(IActionResult result) => result switch
	{
		ObjectResult { Value: ApiErrorDto error } => error.Error,
		_ => null,
	};

	/// <summary>Every test's image store sits under one folder, removed when the class is done.</summary>
	private static readonly string FixtureRoot = Path.Combine(Path.GetTempPath(), $"gallery-moderation-{Guid.NewGuid():N}");

	private static DirectoryInfo Files() => Directory.CreateDirectory(Path.Combine(FixtureRoot, Guid.NewGuid().ToString("N")));

	[After(Class)]
	public static void RemoveTheImageStores()
	{
		if (Directory.Exists(FixtureRoot))
			Directory.Delete(FixtureRoot, true);
	}

	[Test]
	public async Task WithEveryOptionOff_AnAvatarUploadIsThePortraitAtOnce()
	{
		var (dbref, name) = await CharacterAsync();
		var owner = Controller(new GalleryOptions(), Owner, dbref, staff: false, Files());

		var gallery = Gallery(await owner.Upload(name, Png("me.png"), "avatar", null, CancellationToken.None));

		await Assert.That(gallery.Single().IsIcon).IsTrue();
		await Assert.That(gallery.Single().PortraitPending).IsFalse();
		await Assert.That(gallery.Single().Consent).IsNull();
	}

	[Test]
	public async Task ConsentText_RefusesAnUploadThatDidNotAcceptIt_OrAcceptedOtherWording()
	{
		var (dbref, name) = await CharacterAsync();
		var owner = Controller(new GalleryOptions { ConsentText = Terms }, Owner, dbref, staff: false, Files());

		var none = await owner.Upload(name, Png("a.png"), null, null, CancellationToken.None);
		var stale = await owner.Upload(name, Png("a.png"), null, GalleryRules.ConsentVersion("Older terms."), CancellationToken.None);

		await Assert.That(Refusal(none)).IsEqualTo("Accept the gallery terms before uploading.");
		await Assert.That(Refusal(stale)).IsEqualTo("The gallery terms changed after you read them. Read them again before uploading.");
	}

	[Test]
	public async Task ConsentText_RefusesALibraryPickThatDidNotAcceptIt()
	{
		var (dbref, name) = await CharacterAsync();
		var owner = Controller(new GalleryOptions { ConsentText = Terms }, Owner, dbref, staff: false, Files());

		var none = await owner.AddFromLibrary(name, new GalleryLibraryPick("no-such-asset"), CancellationToken.None);
		var accepted = await owner.AddFromLibrary(name,
			new GalleryLibraryPick("no-such-asset") { Consent = GalleryRules.ConsentVersion(Terms) }, CancellationToken.None);

		await Assert.That(Refusal(none)).IsEqualTo("Accept the gallery terms before uploading.");
		await Assert.That(Refusal(accepted)).IsEqualTo("No such file in the image library.").Because("accepted terms let the pick go on");
	}

	[Test]
	public async Task ConsentText_IsKeptWithTheImage_AndShownToStaffOnly()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var options = new GalleryOptions { ConsentText = Terms };
		var owner = Controller(options, Owner, dbref, staff: false, files);

		var uploaded = await owner.Upload(name, Png("a.png"), null, GalleryRules.ConsentVersion(Terms), CancellationToken.None);
		await Assert.That(uploaded).IsTypeOf<OkObjectResult>();

		var staffView = (await Controller(options, Stranger, dbref, staff: true, files).List(name, CancellationToken.None)).Value!;
		var playerView = (await owner.List(name, CancellationToken.None)).Value!;

		await Assert.That(staffView.Single().Consent).IsEqualTo(new GalleryConsent(Terms, dbref.ToString(), Now) { ByName = name });
		await Assert.That(playerView.Single().Consent).IsNull().Because("who accepted what is staff's record");
	}

	[Test]
	public async Task TheByteLimit_RefusesAnUploadPastIt_AndOfficialArtCanBeLeftOut()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var limited = new GalleryOptions { MaxGalleryMegabytes = 1 };
		var owner = Controller(limited, Owner, dbref, staff: false, files);

		var first = Gallery(await owner.Upload(name, Png("big.png", 700_000), null, null, CancellationToken.None));
		var second = await owner.Upload(name, Png("big2.png", 700_000), null, null, CancellationToken.None);
		await Assert.That(Refusal(second)).IsEqualTo(
			"This gallery's images take 0.7 MB of its 1 MB, and this one needs 0.7 MB. Remove an image or add a smaller one.");

		// Staff approve the first as the portrait, which makes it official art.
		await Controller(limited, Stranger, dbref, staff: true, files).ApprovePortrait(name, first.Single().AssetId, CancellationToken.None);
		var stillCounted = await owner.Upload(name, Png("big2.png", 700_000), null, null, CancellationToken.None);
		var exempt = Controller(limited with { LimitSkipsOfficial = true }, Owner, dbref, staff: false, files);
		var leftOut = await exempt.Upload(name, Png("big2.png", 700_000), null, null, CancellationToken.None);

		await Assert.That(Refusal(stillCounted)).IsNotNull();
		await Assert.That(Gallery(leftOut).Count).IsEqualTo(2);
	}

	[Test]
	public async Task PortraitApproval_AnAvatarUploadWaits_AndStaffApproveItAsOfficialArt()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var approval = new GalleryOptions { PortraitApproval = true };
		var events = Substitute.For<IEventService>();
		var owner = Controller(approval, Owner, dbref, staff: false, files, events);

		// Staff who control the character (a wizard) upload its portrait directly; they are never asked to wait.
		var current = Gallery(await Controller(approval, Owner, dbref, staff: true, files).Upload(name, Png("official.png"), "avatar", null, CancellationToken.None));
		var asked = Gallery(await owner.Upload(name, Png("new.png"), "avatar", null, CancellationToken.None));
		var waiting = asked.Single(e => e.FileName == "new.png");

		await Assert.That(asked.Single(e => e.IsIcon).AssetId).IsEqualTo(current.Single().AssetId).Because("the portrait stays until staff decide");
		await Assert.That(waiting.PortraitPending).IsTrue();
		await events.Received(1).TriggerEventAsync(SharpEvents.GallerySubmit, Arg.Any<DBRef?>(),
			Arg.Is<string[]>(a => a[0] == dbref.ToString() && a[1] == waiting.AssetId));

		var staffEvents = Substitute.For<IEventService>();
		var staff = Controller(approval, Stranger, dbref, staff: true, files, staffEvents);
		var queued = (await staff.Portraits(CancellationToken.None)).Where(p => p.Character == name).ToList();
		await Assert.That(queued.Single().Submitted.AssetId).IsEqualTo(waiting.AssetId);
		await Assert.That(queued.Single().Current?.AssetId).IsEqualTo(current.Single().AssetId);

		var approved = Gallery(await staff.ApprovePortrait(name, waiting.AssetId, CancellationToken.None));
		var portrait = approved.Single(e => e.IsIcon);
		await Assert.That(portrait.AssetId).IsEqualTo(waiting.AssetId);
		await Assert.That(portrait.Official).IsTrue();
		await Assert.That(approved.Any(e => e.PortraitPending)).IsFalse();
		await staffEvents.Received(1).TriggerEventAsync(SharpEvents.GalleryPortrait, Arg.Any<DBRef?>(),
			Arg.Is<string[]>(a => a[0] == dbref.ToString() && a[1] == "approve" && a[2] == waiting.AssetId));
		await Assert.That((await staff.Portraits(CancellationToken.None)).Any(p => p.Character == name)).IsFalse();
	}

	[Test]
	public async Task PortraitApproval_APlayerChoosingAnotherImageAsks_AndCannotMarkOfficialArt()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var approval = new GalleryOptions { PortraitApproval = true };
		var owner = Controller(approval, Owner, dbref, staff: false, files);
		await owner.Upload(name, Png("a.png"), null, null, CancellationToken.None);
		var gallery = Gallery(await owner.Upload(name, Png("b.png"), null, null, CancellationToken.None));
		await Assert.That(gallery.Any(e => e.IsIcon)).IsFalse().Because("no image becomes the portrait by being first");

		var sent = gallery.Select(e => e with { IsIcon = e.FileName == "b.png", Official = true }).ToList();
		var after = Gallery(await owner.Replace(name, sent, CancellationToken.None));

		await Assert.That(after.Any(e => e.IsIcon)).IsFalse();
		await Assert.That(after.Single(e => e.PortraitPending).FileName).IsEqualTo("b.png");
		await Assert.That(after.Any(e => e.Official)).IsFalse().Because("only staff mark official art");

		var withdrawn = Gallery(await owner.Replace(name, after.Select(e => e with { PortraitPending = false }).ToList(), CancellationToken.None));
		await Assert.That(withdrawn.Any(e => e.PortraitPending)).IsFalse();
	}

	[Test]
	public async Task PortraitApproval_StaffDecline_AndClearToTheDefault()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var approval = new GalleryOptions { PortraitApproval = true };
		var owner = Controller(approval, Owner, dbref, staff: false, files);
		var staff = Controller(approval, Stranger, dbref, staff: true, files);
		var portrait = Gallery(await Controller(approval, Owner, dbref, staff: true, files).Upload(name, Png("official.png"), "avatar", null, CancellationToken.None)).Single();
		var waiting = Gallery(await owner.Upload(name, Png("new.png"), "avatar", null, CancellationToken.None)).Single(e => e.PortraitPending);

		var declined = Gallery(await staff.DeclinePortrait(name, waiting.AssetId, CancellationToken.None));
		await Assert.That(declined.Count).IsEqualTo(2).Because("a declined image stays in the gallery");
		await Assert.That(declined.Any(e => e.PortraitPending)).IsFalse();
		await Assert.That(Refusal(await staff.DeclinePortrait(name, waiting.AssetId, CancellationToken.None)))
			.IsEqualTo("That image is not waiting to be the portrait.");

		var cleared = Gallery(await staff.ClearPortrait(name, CancellationToken.None));
		await Assert.That(cleared.Any(e => e.IsIcon)).IsFalse();
		await Assert.That(cleared.Select(e => e.AssetId)).Contains(portrait.AssetId).Because("clearing the portrait keeps the image");

		var notOn = Controller(new GalleryOptions(), Stranger, dbref, staff: true, files);
		await Assert.That(Refusal(await notOn.ClearPortrait(name, CancellationToken.None))).IsNotNull()
			.Because("without approval the gallery would choose a portrait again at once");
	}

	[Test]
	public async Task Moderation_KeepsSoftcodeFromWritingTheGalleryOrThePortrait()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var attributes = Services.GetRequiredService<IAttributeService>();
		var player = (await Services.GetRequiredService<IMediator>().Send(new GetObjectNodeQuery(dbref))).Expect<AnySharpObject>();
		var approval = new GalleryOptions { PortraitApproval = true };
		await Controller(approval, Owner, dbref, staff: false, files).Upload(name, Png("a.png"), null, null, CancellationToken.None);

		var forged = await attributes.SetAttributeAsync(player, player, GalleryWriter.GalleryAttribute,
			MString.Plain("""[{"assetId":"x","fileName":"x.png","url":"/x","order":0,"isIcon":true,"official":true}]"""));
		var image = await attributes.SetAttributeAsync(player, player, "IMAGE", MString.Plain("https://example.com/me.png"));
		await Assert.That(forged is Error<string>).IsTrue().Because("a player cannot forge official art");
		await Assert.That(image is Error<string>).IsTrue().Because("under approval only staff choose the portrait");

		// With the options off again, the next gallery write hands both back to the character.
		await Controller(new GalleryOptions(), Owner, dbref, staff: false, files).Upload(name, Png("b.png"), null, null, CancellationToken.None);
		var own = await attributes.SetAttributeAsync(player, player, "IMAGE", MString.Plain("https://example.com/me.png"));
		await Assert.That(own is Success).IsTrue();
	}

	[Test]
	public async Task TheApprovalQueue_IsEmptyWhileApprovalIsOff()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		await Controller(new GalleryOptions { PortraitApproval = true }, Owner, dbref, staff: false, files)
			.Upload(name, Png("new.png"), "avatar", null, CancellationToken.None);

		var off = await Controller(new GalleryOptions(), Stranger, dbref, staff: true, files).Portraits(CancellationToken.None);

		await Assert.That(off).IsEmpty().Because("a request left from before approval was turned off is not acted on");
	}

	[Test]
	public async Task StaffOnly_RefusesTheOwner_AndLetsStaffChangeTheGallery()
	{
		var (dbref, name) = await CharacterAsync();
		var files = Files();
		var locked = new GalleryOptions { StaffOnly = true };

		var refused = await Controller(locked, Owner, dbref, staff: false, files).Upload(name, Png("a.png"), null, null, CancellationToken.None);
		var staffUpload = await Controller(locked, Stranger, dbref, staff: true, files).Upload(name, Png("a.png"), null, null, CancellationToken.None);

		await Assert.That(((ObjectResult)refused).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
		await Assert.That(Refusal(refused)).IsEqualTo("Only staff change galleries in this game.");
		await Assert.That(Gallery(staffUpload).Count).IsEqualTo(1);

		var asset = Gallery(staffUpload).Single().AssetId;
		var deleted = await Controller(locked, Owner, dbref, staff: false, files).Delete(name, asset, CancellationToken.None);
		await Assert.That(((ObjectResult)deleted).StatusCode).IsEqualTo(StatusCodes.Status403Forbidden);
	}

	[Test]
	public async Task ThePolicy_SaysWhatThePortalNeeds()
	{
		var (dbref, _) = await CharacterAsync();
		var policy = await Controller(new GalleryOptions { ConsentText = Terms, MaxGalleryMegabytes = 25, PortraitApproval = true },
			Owner, dbref, staff: true, Files()).Policy();

		await Assert.That(policy.ConsentText).IsEqualTo(Terms);
		await Assert.That(policy.ConsentVersion).IsEqualTo(GalleryRules.ConsentVersion(Terms));
		await Assert.That(policy.MaxBytes).IsEqualTo(25L * 1_048_576);
		await Assert.That(policy.PortraitApproval).IsTrue();
		await Assert.That(policy.StaffOnly).IsFalse();
		await Assert.That(policy.IsStaff).IsTrue();
	}
}
