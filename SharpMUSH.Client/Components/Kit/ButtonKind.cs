using MudBlazor;

namespace SharpMUSH.Client.Components.Kit;

/// <summary>
/// The portal's three button styles. A <c>MudButton</c> takes one of them and nothing else:
/// <list type="bullet">
/// <item><see cref="Primary"/>: filled in the accent, the one thing the view is for.</item>
/// <item><see cref="Secondary"/>: outlined, everything else.</item>
/// <item><see cref="DangerQuiet"/>: text in the danger colour, for an action that destroys or takes away.
/// It asks first, and on a row it sits in the row's menu.</item>
/// </list>
/// A button whose style is not fixed (a toggle) takes <see cref="ButtonKinds.Variant"/> and <see cref="ButtonKinds.Color"/>.
/// </summary>
public enum ButtonKind
{
	Primary,
	Secondary,
	DangerQuiet,
}

public static class ButtonKinds
{
	public static Variant Variant(ButtonKind kind) => kind switch
	{
		ButtonKind.Primary => MudBlazor.Variant.Filled,
		ButtonKind.Secondary => MudBlazor.Variant.Outlined,
		_ => MudBlazor.Variant.Text,
	};

	public static Color Color(ButtonKind kind) => kind switch
	{
		ButtonKind.Primary => MudBlazor.Color.Primary,
		ButtonKind.Secondary => MudBlazor.Color.Default,
		_ => MudBlazor.Color.Error,
	};
}
