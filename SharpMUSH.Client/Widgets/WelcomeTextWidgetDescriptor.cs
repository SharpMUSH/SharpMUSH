using SharpMUSH.Client.Components.Widgets;
using SharpMUSH.Client.Models.Widgets;
using SharpMUSH.Library.Models.Portal.Widgets;

namespace SharpMUSH.Client.Widgets;

/// <summary>
/// Descriptor for the Welcome Text widget.
/// </summary>
public sealed class WelcomeTextWidgetDescriptor : IPortalWidget
{
	public string Name => "WelcomeText";
	public string DisplayName => "Welcome Text";
	public string Description => "Displays a markdown welcome message.";
	public WidgetSize DefaultSize => WidgetSize.Large;
	public WidgetZone[] AllowedZones => [WidgetZone.MainContent];
	public Type ComponentType => typeof(WelcomeTextWidget);
	public Type? ConfigType => typeof(WelcomeTextConfig);
}
