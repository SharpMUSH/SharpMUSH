using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using SharpMUSH.Client.Components.Kit;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// The shell's page-sidebar outlet beside some content, as MainLayout composes them, so a section
/// layout's sidebar has somewhere to render in a test. The outlet is <c>aside.test-pagebar</c>.
/// </summary>
public sealed class PageSidebarHost : ComponentBase
{
	[Parameter] public RenderFragment? ChildContent { get; set; }

	protected override void BuildRenderTree(RenderTreeBuilder builder)
	{
		builder.OpenElement(0, "aside");
		builder.AddAttribute(1, "class", "test-pagebar");
		builder.OpenComponent<SectionOutlet>(2);
		builder.AddAttribute(3, nameof(SectionOutlet.SectionName), PageSidebarSlot.Name);
		builder.CloseComponent();
		builder.CloseElement();
		builder.AddContent(4, ChildContent);
	}
}
