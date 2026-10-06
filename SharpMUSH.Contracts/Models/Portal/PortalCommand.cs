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
/// <param name="Character">
/// Optional. The objid (<c>#N:creation</c>) of the character the caller means to act as. The command
/// runs only while the session is still bound to that character, and is refused with 409 otherwise,
/// so a flow of several requests stays with the character it started as.
/// </param>
public record PortalCommandRequest(string Command, string? Result = null, string? Character = null);

/// <summary>What a portal command answered.</summary>
/// <param name="Output">
/// Every line the character was told while the command ran, as plain text, in order. Work the command
/// queued for later (<c>@wait</c>, <c>@trigger</c>) is not part of it.
/// </param>
/// <param name="Result">The value of <see cref="PortalCommandRequest.Result"/>, or null when none was asked for.</param>
/// <param name="Truncated">True when output past the length limit was left out of <see cref="Output"/>.</param>
public record PortalCommandResponse(IReadOnlyList<string> Output, string? Result, bool Truncated);

/// <summary>
/// An expression the web portal evaluates as the account session's character (<c>POST api/commands/eval</c>),
/// the Softcode Editor's console.
/// </summary>
/// <param name="Expression">
/// The code to evaluate, as a function body: <c>u()</c> would evaluate it the same way were it stored in an
/// attribute. It may span lines.
/// </param>
/// <param name="Arguments">
/// Optional. <c>%0</c> to <c>%9</c>, in order. Each is evaluated as the character first, as <c>u()</c>
/// evaluates its arguments in its caller.
/// </param>
/// <param name="Object">
/// Optional. The dbref number of an object the character controls, to evaluate as: <c>%!</c> and <c>me</c>
/// are that object, while <c>%#</c> and <c>%@</c> stay the character, as when the character calls
/// <c>u()</c> on one of the object's attributes. Without it the expression runs as the character.
/// </param>
/// <param name="Character">Optional. The objid of the character the caller means to act as; see <see cref="PortalCommandRequest.Character"/>.</param>
public record PortalEvalRequest(string Expression, IReadOnlyList<string>? Arguments = null, int? Object = null, string? Character = null);
