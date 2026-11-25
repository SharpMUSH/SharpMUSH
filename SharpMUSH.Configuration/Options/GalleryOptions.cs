namespace SharpMUSH.Configuration.Options;

/// <summary>
/// How a game moderates character galleries on the web portal. Every option is off by default, which keeps a gallery
/// its owner's to change. <c>max_gallery_images</c> stays with the other limits.
/// </summary>
public sealed record GalleryOptions
{
	/// <summary>The most <see cref="ConsentText"/> holds.</summary>
	public const int MaxConsentTextLength = 2000;

	[SharpConfig(Name = "gallery_consent_text", Category = "Gallery", Group = "Uploads", Order = 1,
		Description = "Terms an uploader accepts before every gallery upload; who accepted them, when, and the text they read are kept with the image. Empty asks for nothing",
		Example = "I made this picture, or its artist lets me use it here, and this game may show it on my character.",
		ValidationPattern = @"^[\s\S]{0,2000}$")]
	public string ConsentText { get; init; } = "";

	[SharpConfig(Name = "max_gallery_megabytes", Category = "Gallery", Group = "Limits", Order = 1,
		Description = "Megabytes one character's gallery images may take together; an upload past it is refused. 0 sets no limit",
		Example = "25", Min = 0, Max = 100000)]
	public uint MaxGalleryMegabytes { get; init; }

	[SharpConfig(Name = "gallery_limit_skips_official", Category = "Gallery", Group = "Limits", Order = 2,
		Description = "Official art (a portrait staff approved, or an image staff marked official) does not count toward max_gallery_images or max_gallery_megabytes")]
	public bool LimitSkipsOfficial { get; init; }

	[SharpConfig(Name = "gallery_portrait_approval", Category = "Gallery", Group = "Moderation", Order = 1,
		Description = "A player's new portrait waits for staff to approve it, and the current one stays until they do; staff can also clear a portrait back to the default")]
	public bool PortraitApproval { get; init; }

	[SharpConfig(Name = "gallery_staff_only", Category = "Gallery", Group = "Moderation", Order = 2,
		Description = "Only staff who manage the image library (media.admin) add, change or remove gallery images; players can still view and report them")]
	public bool StaffOnly { get; init; }
}
