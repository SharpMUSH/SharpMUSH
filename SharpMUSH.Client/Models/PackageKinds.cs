using Microsoft.Extensions.Localization;
using MudBlazor;
using SharpMUSH.Library.Models.Packages;

namespace SharpMUSH.Client.Models;

/// <summary>How the package lists name and draw what a package ships.</summary>
public static class PackageKinds
{
	/// <summary>The order the tags are listed in.</summary>
	public static IReadOnlyList<PackageContents> Order { get; } =
		[PackageContents.Softcode, PackageContents.Wiki, PackageContents.Application, PackageContents.Plugin, PackageContents.Ui];

	/// <summary>What an installed package ships, from its kind and what it installed: objects or attributes are softcode.</summary>
	public static PackageContents Of(PackageKind kind, int objectCount, int attributeCount, int wikiPageCount) =>
		(objectCount > 0 || attributeCount > 0 ? PackageContents.Softcode : PackageContents.None)
		| (wikiPageCount > 0 ? PackageContents.Wiki : PackageContents.None)
		| kind switch
		{
			PackageKind.Application => PackageContents.Application,
			PackageKind.Plugin => PackageContents.Plugin,
			PackageKind.Ui => PackageContents.Ui,
			_ => PackageContents.None
		};

	/// <summary>The one kind a package's icon shows: what it mainly is.</summary>
	public static PackageContents Primary(PackageContents contents) =>
		new[] { PackageContents.Plugin, PackageContents.Application, PackageContents.Ui, PackageContents.Softcode, PackageContents.Wiki }
			.FirstOrDefault(kind => contents.HasFlag(kind), PackageContents.Softcode);

	public static string Icon(PackageContents kind) => kind switch
	{
		PackageContents.Wiki => Icons.Material.Outlined.MenuBook,
		PackageContents.Application => Icons.Material.Outlined.Apps,
		PackageContents.Plugin => Icons.Material.Outlined.Extension,
		PackageContents.Ui => Icons.Material.Outlined.WebAsset,
		_ => Icons.Material.Outlined.Code
	};

	public static string Name(PackageContents kind, IStringLocalizer localizer) => kind switch
	{
		PackageContents.Wiki => localizer["PkgKindWiki"],
		PackageContents.Application => localizer["PkgKindApplication"],
		PackageContents.Plugin => localizer["PkgKindPlugin"],
		PackageContents.Ui => "UI",
		_ => localizer["PkgKindSoftcode"]
	};
}
