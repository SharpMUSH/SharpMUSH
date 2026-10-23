using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SharpMUSH.Library.Models.Portal;
using SharpMUSH.Library.Models.Portal.Onboarding;

namespace SharpMUSH.Client.Services;

/// <summary>
/// The game's <c>+welcome</c> checklist (the bundled onboarding package), as the portal guide sees it: the guide
/// covers that list's "Learn the basics" step, so finishing the guide ticks the step for the character the account
/// is playing. Nothing is sent unless the reviewed <c>+welcome</c> is installed.
/// </summary>
public static class WelcomeChecklist
{
	public const string Command = "+welcome";

	public const string BasicsStep = "basics";

	/// <param name="character">The objid of the character that finished the guide; the tick is refused once the session plays another.</param>
	public static async Task TickBasicsAsync(IHttpClientFactory http, string token, string character)
	{
		try
		{
			using var client = http.CreateClient("api");
			using var capabilities = new HttpRequestMessage(HttpMethod.Get, "api/onboarding/capabilities");
			capabilities.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using var found = await client.SendAsync(capabilities);
			if (!found.IsSuccessStatusCode
				|| await found.Content.ReadFromJsonAsync<GuideCapability[]>() is not { } installed
				|| !installed.Any(capability => capability.Command == Command)) return;
			using var tick = new HttpRequestMessage(HttpMethod.Post, "api/commands")
			{
				Content = JsonContent.Create(new PortalCommandRequest($"{Command}/done {BasicsStep}", Character: character))
			};
			tick.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			using var _ = await client.SendAsync(tick);
		}
		catch (Exception error) when (error is HttpRequestException or JsonException or TaskCanceledException) { }
	}
}
