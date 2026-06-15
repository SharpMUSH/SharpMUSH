namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the Spacer widget: an empty block used to push widgets apart. Width comes from
/// the placement's column span; <see cref="Height"/> sets its height in pixels.
/// </summary>
public record SpacerConfig(int Height = 24);
