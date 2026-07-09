using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Mediator;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Hubs;

namespace SharpMUSH.Server.Authentication;

/// <summary>
/// HTTP Basic authentication for the in-server MCP endpoint, keyed on a game
/// character's name and password.
///
/// The credential rides on every request as
/// <c>Authorization: Basic base64(character:password)</c>. The character is resolved
/// and the password verified through the exact same flow the in-game <c>connect</c>
/// command uses (<see cref="GetPlayerQuery"/> + <see cref="IPasswordService.PasswordIsValid"/>),
/// so there is a single source of truth for character credentials.
///
/// On success the authenticated character becomes the request identity (its DBRef,
/// key, creation time and name are emitted as claims), which the MCP endpoint's
/// authorization policy requires and future per-enactor scoping can build on.
/// </summary>
public class MushBasicAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	IMediator mediator,
	IPasswordService passwordService)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	public const string SchemeName = "MushBasic";

	/// <summary>
	/// A real, never-matching PBKDF2 hash used only to spend equivalent verification time on the
	/// unknown-character path (see the null-player branch below). Computed once, process-wide.
	/// </summary>
	private static readonly Lazy<string> DummyHash =
		new(() => new PasswordHasher<string>().HashPassword("timing:parity", "timing:parity"));

	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (!Request.Headers.TryGetValue("Authorization", out var authHeaderValues))
		{
			return AuthenticateResult.NoResult();
		}

		var authHeader = authHeaderValues.ToString();
		if (string.IsNullOrEmpty(authHeader) ||
		    !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
		{
			return AuthenticateResult.NoResult();
		}

		string character;
		string password;
		try
		{
			var encoded = authHeader["Basic ".Length..].Trim();
			var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
			var separator = decoded.IndexOf(':');
			if (separator < 0)
			{
				return AuthenticateResult.Fail("Malformed Basic credentials: expected 'character:password'.");
			}

			character = decoded[..separator];
			password = decoded[(separator + 1)..];
		}
		catch (FormatException)
		{
			return AuthenticateResult.Fail("Malformed Basic credentials: not valid base64.");
		}

		if (string.IsNullOrWhiteSpace(character))
		{
			return AuthenticateResult.Fail("Missing character name.");
		}

		var player = await mediator.CreateStream(new GetPlayerQuery(character)).FirstOrDefaultAsync();
		if (player is null)
		{
			// Timing parity: a found character runs a full PBKDF2 verification, so an unknown
			// character must too — otherwise the response latency alone reveals whether the
			// character exists, defeating the opaque error message below. Verify against a real
			// (never-matching) hash so the work is equivalent.
			_ = passwordService.PasswordIsValid("timing:parity", password, DummyHash.Value);

			// Same opaque message whether the character is unknown or the password is wrong,
			// so the endpoint doesn't confirm which characters exist.
			return AuthenticateResult.Fail("Invalid character or password.");
		}

		if (string.IsNullOrEmpty(player.PasswordHash))
		{
			// A passwordless character can't authenticate over MCP. Spend the same verification
			// time as every other failure so its response latency doesn't reveal that it has no
			// password set (PasswordIsValid would otherwise short-circuit on the empty hash).
			_ = passwordService.PasswordIsValid("timing:parity", password, DummyHash.Value);
			return AuthenticateResult.Fail("Invalid character or password.");
		}

		var salt = $"#{player.Object.Key}:{player.Object.CreationTime}";
		if (!passwordService.PasswordIsValid(salt, password, player.PasswordHash))
		{
			return AuthenticateResult.Fail("Invalid character or password.");
		}

		var claims = new List<Claim>
		{
			new(ClaimTypes.NameIdentifier, $"#{player.Object.Key}"),
			new(ClaimTypes.Name, player.Object.Name),
			new("character_key", player.Object.Key.ToString()),
			new("character_creation_time", player.Object.CreationTime.ToString()),
			new("character_name", player.Object.Name),
			new(GameHub.CharacterDbrefClaim, $"#{player.Object.Key}")
		};

		var identity = new ClaimsIdentity(claims, SchemeName);
		var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
		return AuthenticateResult.Success(ticket);
	}

	protected override Task HandleChallengeAsync(AuthenticationProperties properties)
	{
		Response.Headers.WWWAuthenticate = "Basic realm=\"SharpMUSH MCP\", charset=\"UTF-8\"";
		return base.HandleChallengeAsync(properties);
	}
}
