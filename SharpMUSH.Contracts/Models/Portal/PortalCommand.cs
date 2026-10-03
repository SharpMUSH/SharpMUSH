namespace SharpMUSH.Library.Models.Portal;

/// <summary>
/// A command the web portal runs as the account session's character (<c>POST api/commands</c>).
/// </summary>
/// <param name="Command">
/// One line, run as if the character typed it: not split on semicolons, and a <c>$</c>-command it
/// matches runs in place.
/// </param>
/// <param name="Result">
/// Optional. An expression evaluated as the character right after the command, in the same queue
/// entry, so nothing else in the game runs between the two. Its value is the structured answer, for
/// a caller that needs to learn what the command made — <c>scenefocus(me)</c> after <c>+scene/create</c>.
/// </param>
public record PortalCommandRequest(string Command, string? Result = null);

/// <summary>What a portal command answered.</summary>
/// <param name="Output">
/// Every line the character was told while the command ran, as plain text, in order. Work the command
/// queued for later (<c>@wait</c>, <c>@trigger</c>) is not part of it.
/// </param>
/// <param name="Result">The value of <see cref="PortalCommandRequest.Result"/>, or null when none was asked for.</param>
/// <param name="Truncated">True when output past the length limit was left out of <see cref="Output"/>.</param>
public record PortalCommandResponse(IReadOnlyList<string> Output, string? Result, bool Truncated);
