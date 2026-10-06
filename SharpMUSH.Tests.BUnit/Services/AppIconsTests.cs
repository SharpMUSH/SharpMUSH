using MudBlazor;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// An application's icon is a Material icon name in its package (<c>icon: support_agent</c>); the portal
/// draws it as MudBlazor's SVG for that name (<see cref="AppIcons"/>).
/// </summary>
public class AppIconsTests
{
	[Test]
	[Arguments("support_agent")]
	[Arguments("SupportAgent")]
	[Arguments("support-agent")]
	[Arguments(" supportagent ")]
	public async Task A_material_name_in_any_spelling_is_its_svg(string name)
	{
		await Assert.That(AppIcons.Outline(name, Icons.Material.Outlined.Apps)).IsEqualTo(Icons.Material.Outlined.SupportAgent);
		await Assert.That(AppIcons.Fill(name, Icons.Material.Filled.Apps)).IsEqualTo(Icons.Material.Filled.SupportAgent);
	}

	[Test]
	public async Task A_name_starting_with_a_digit_matches_the_underscored_field()
	{
		await Assert.That(AppIcons.Fill("3d_rotation", Icons.Material.Filled.Apps)).IsEqualTo(Icons.Material.Filled._3dRotation);
	}

	[Test]
	public async Task Svg_markup_is_used_as_it_is()
	{
		await Assert.That(AppIcons.Outline(Icons.Material.Filled.Badge, Icons.Material.Outlined.Apps)).IsEqualTo(Icons.Material.Filled.Badge);
	}

	[Test]
	[Arguments(null)]
	[Arguments("")]
	[Arguments("no_such_icon_anywhere")]
	public async Task No_name_or_an_unknown_one_is_the_fallback(string? name)
	{
		await Assert.That(AppIcons.Outline(name, Icons.Material.Outlined.Apps)).IsEqualTo(Icons.Material.Outlined.Apps);
	}
}
