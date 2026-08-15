namespace SharpMUSH.Client.Models.Widgets;

/// <summary>
/// Config schema for the Welcome Text widget.
/// </summary>
/// <param name="Markdown">Markdown source rendered as the widget's body. Empty renders nothing.</param>
/// <param name="ShowToGuests">
/// Whether signed-out visitors see the text. Defaults to <c>true</c> so a config that omits the key
/// stays visible to everyone.
/// </param>
public record WelcomeTextConfig(string Markdown, bool ShowToGuests = true);
