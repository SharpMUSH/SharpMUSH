using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Portal.Discord;
using SharpMUSH.Plugins.Discord;
using SharpMUSH.Plugins.Discord.Links;
using SharpMUSH.Plugins.Discord.Members;
using SharpMUSH.Plugins.Discord.Roles;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>
/// The portal's <c>api/discord/admin</c> and <c>api/discord</c> endpoints over HTTP, mapped as the server maps them, with
/// the plugin registered as the server registers it and no Discord to talk to.
/// </summary>
public class DiscordAdminEndpointsTests
{
	private const string Admin = "admin-account";
	private const string Member = "member-account";
	private const ulong Channel = 4242;
	private const ulong Guild = 900;

	/// <summary>Signs a request in as the account its <c>X-Account</c> header names; no header, no sign-in.</summary>
	private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
		: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
	{
		public const string Name = "Header";

		protected override Task<AuthenticateResult> HandleAuthenticateAsync()
		{
			if (Request.Headers["X-Account"].FirstOrDefault() is not { Length: > 0 } account)
			{
				return Task.FromResult(AuthenticateResult.NoResult());
			}

			var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account), new Claim(ClaimTypes.Name, account)], Name);
			return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
		}
	}

	private DiscordPluginHarness _harness = null!;
	private WebApplication _app = null!;
	private HttpClient _client = null!;

	[Before(Test)]
	public async Task Start()
	{
		_harness = await DiscordPluginHarness.StartAsync();
		_harness.Capabilities.AuthorizeAsync(Arg.Is<CapabilityActor>(actor => actor.AccountId == Admin), SharpMUSH.Plugins.Discord.DiscordPlugin.AdminPermission,
			Arg.Any<CancellationToken>()).Returns(true);

		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseTestServer();
		builder.Logging.ClearProviders();
		builder.Services.AddAuthentication(HeaderAuthentication.Name)
			.AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>(HeaderAuthentication.Name, null);
		builder.Services.AddAuthorization();
		_harness.Register(builder.Services);
		_app = builder.Build();
		_app.UseAuthentication();
		_app.UseAuthorization();
		new SharpMUSH.Plugins.Discord.DiscordPlugin().MapEndpoints(_app);
		await _app.StartAsync();
		_harness.UseHost(_app.Services);
		_client = _app.GetTestClient();
	}

	[After(Test)]
	public async Task Stop()
	{
		_client.Dispose();
		await _app.StopAsync();
		await _app.DisposeAsync();
		await _harness.DisposeAsync();
	}

	private HttpRequestMessage As(string? account, HttpMethod method, string path, object? body = null)
	{
		var request = new HttpRequestMessage(method, path);
		if (account is not null) request.Headers.Add("X-Account", account);
		if (body is not null) request.Content = JsonContent.Create(body);
		return request;
	}

	private async Task<T> ReadAsync<T>(string? account, HttpMethod method, string path, object? body = null)
	{
		using var response = await _client.SendAsync(As(account, method, path, body));
		await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
		return (await response.Content.ReadFromJsonAsync<T>())!;
	}

	private Task<DiscordAdminAnswer> AnswerAsync(HttpMethod method, string path, object? body = null)
		=> ReadAsync<DiscordAdminAnswer>(Admin, method, path, body);

	private Task LinkAsync(ulong channel, string name)
		=> _harness.Get<IDiscordLinkStore>().SaveAsync(
			new DiscordChannelLink(channel, Guild, channel + 1, name, "Guild", DiscordPluginHarness.Now, "#1:1001")).AsTask();

	private Task MapAsync(ulong role, string name, string slug, DiscordRoleMode mode = DiscordRoleMode.Report)
		=> _harness.Get<IDiscordRoleStore>().SaveAsync(
			new DiscordRoleMapping(role, Guild, name, slug, mode, DiscordPluginHarness.Now, "#1:1001")).AsTask();

	[Test]
	public async Task AdminEndpoints_RefuseAnyoneWithoutDiscordAdmin()
	{
		using var anonymous = await _client.SendAsync(As(null, HttpMethod.Get, "api/discord/admin/connection"));
		using var member = await _client.SendAsync(As(Member, HttpMethod.Get, "api/discord/admin/connection"));

		await Assert.That(anonymous.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
		await Assert.That(member.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
	}

	[Test, Timeout(30_000)]
	public async Task Connection_ReportsTheGatewayAndTheSetupSteps(CancellationToken cancellationToken)
	{
		// The host started the gateway, which finds no token.
		var gateway = _harness.Get<DiscordGatewayService>();
		while (gateway.State.Status == DiscordGatewayStatus.Connecting)
		{
			await Task.Delay(10, cancellationToken);
		}

		var view = await ReadAsync<DiscordConnectionView>(Admin, HttpMethod.Get, "api/discord/admin/connection");

		await Assert.That(view.Status).IsEqualTo("noToken");
		await Assert.That(view.Guilds).IsEqualTo(0);
		await Assert.That(view.MirrorBans).IsFalse();
		await Assert.That(view.Steps).IsNotEmpty();
	}

	[Test]
	public async Task Settings_TurnBanMirroringOnAndOff()
	{
		var on = await AnswerAsync(HttpMethod.Put, "api/discord/admin/settings", new DiscordSettingsRequest(true));
		var mirrored = _harness.Get<IDiscordSettingsStore>().Get().MirrorBans;
		var off = await AnswerAsync(HttpMethod.Put, "api/discord/admin/settings", new DiscordSettingsRequest(false));

		await Assert.That(on).IsEqualTo(new DiscordAdminAnswer("A game ban now bans the account's Discord member in every server the bot is in.", "ok"));
		await Assert.That(mirrored).IsTrue();
		await Assert.That(off.Kind).IsEqualTo("ok");
		await Assert.That(_harness.Get<IDiscordSettingsStore>().Get().MirrorBans).IsFalse();
	}

	[Test]
	public async Task Reconnect_AsksTheGatewayToLogInAgain()
	{
		var answer = await AnswerAsync(HttpMethod.Post, "api/discord/admin/connection");

		await Assert.That(answer).IsEqualTo(new DiscordAdminAnswer("Connecting to Discord again with the stored token.", "ok"));
	}

	[Test]
	public async Task Channels_ListTheLinksAndNoAvailableChannelsWhileAway()
	{
		await LinkAsync(Channel, "general");

		var links = await ReadAsync<List<DiscordChannelView>>(Admin, HttpMethod.Get, "api/discord/admin/channels");
		var available = await ReadAsync<List<DiscordTextChannelView>>(Admin, HttpMethod.Get, "api/discord/admin/channels/available");

		var link = links.Single();
		await Assert.That(link.ChannelId).IsEqualTo(Channel.ToString());
		await Assert.That(link.ChannelName).IsEqualTo("general");
		await Assert.That(link.Feed).IsEqualTo($"discord/{Channel}");
		await Assert.That(link.State).IsEqualTo("ok");
		await Assert.That(available).IsEmpty();
	}

	[Test]
	public async Task Link_RefusesABadIdAndSaysWhenTheBotIsAway()
	{
		var bad = await AnswerAsync(HttpMethod.Post, "api/discord/admin/channels", new DiscordLinkChannelRequest("general"));
		var away = await AnswerAsync(HttpMethod.Post, "api/discord/admin/channels", new DiscordLinkChannelRequest(Channel.ToString()));

		await Assert.That(bad).IsEqualTo(new DiscordAdminAnswer("That is not a Discord channel id.", "error"));
		await Assert.That(away).IsEqualTo(new DiscordAdminAnswer("The bot is not connected to Discord. @discord shows why.", "error"));
	}

	[Test]
	public async Task Unlink_AnswersForABadIdAnUnlinkedChannelAndALinkedOne()
	{
		await LinkAsync(Channel, "general");

		var bad = await AnswerAsync(HttpMethod.Delete, "api/discord/admin/channels/general");
		var unlinked = await AnswerAsync(HttpMethod.Delete, $"api/discord/admin/channels/{Channel + 1}");
		var away = await AnswerAsync(HttpMethod.Delete, $"api/discord/admin/channels/{Channel}");

		await Assert.That(bad).IsEqualTo(new DiscordAdminAnswer("That is not a Discord channel id.", "error"));
		await Assert.That(unlinked).IsEqualTo(new DiscordAdminAnswer("That channel is not linked.", "warn"));
		await Assert.That(away.Kind).IsEqualTo("error");
		await Assert.That(away.Message).StartsWith("Discord permission update failed: The bot is not connected to Discord.");
		await Assert.That(_harness.Get<IDiscordLinkStore>().Get(Channel)).IsNotNull();
	}

	[Test]
	public async Task Resume_ClearsALinksFault()
	{
		await _harness.Get<IDiscordLinkStore>().SaveAsync(new DiscordChannelLink(Channel, Guild, Channel + 1, "general", "Guild",
			DiscordPluginHarness.Now, "#1:1001", new DiscordLinkFault(50013, "Missing Permissions", DiscordPluginHarness.Now)));

		var resumed = await AnswerAsync(HttpMethod.Post, $"api/discord/admin/channels/{Channel}/resume");
		var unlinked = await AnswerAsync(HttpMethod.Post, $"api/discord/admin/channels/{Channel + 1}/resume");

		await Assert.That(resumed).IsEqualTo(new DiscordAdminAnswer("#general posts again.", "ok"));
		await Assert.That(unlinked).IsEqualTo(new DiscordAdminAnswer("That channel is not linked.", "warn"));
		await Assert.That(_harness.Get<IDiscordLinkStore>().Get(Channel)!.Fault).IsNull();
	}

	[Test]
	public async Task GateAndAccess_AnswerForUnlinkedChannelsAndWhileTheBotIsAway()
	{
		await LinkAsync(Channel, "general");

		var gateUnlinked = await AnswerAsync(HttpMethod.Post, $"api/discord/admin/channels/{Channel + 1}/gate");
		var accessUnlinked = await AnswerAsync(HttpMethod.Put, $"api/discord/admin/channels/{Channel + 1}/access", new DiscordChannelAccessRequest(true));
		var gate = await AnswerAsync(HttpMethod.Post, $"api/discord/admin/channels/{Channel}/gate");
		var access = await AnswerAsync(HttpMethod.Put, $"api/discord/admin/channels/{Channel}/access", new DiscordChannelAccessRequest(true));

		await Assert.That(gateUnlinked).IsEqualTo(new DiscordAdminAnswer("That channel is not linked.", "warn"));
		await Assert.That(accessUnlinked).IsEqualTo(new DiscordAdminAnswer("That channel is not linked.", "warn"));
		await Assert.That(gate.Kind).IsEqualTo("error");
		await Assert.That(gate.Message).StartsWith("Could not require linked accounts in #general:");
		await Assert.That(access.Kind).IsEqualTo("error");
		await Assert.That(access.Message).StartsWith("Could not change access in #general:");
		await Assert.That(_harness.Get<IDiscordLinkStore>().Get(Channel)!.Unrestricted).IsFalse();
	}

	[Test]
	public async Task Roles_ListMappingsAndDrift()
	{
		await MapAsync(77, "Builders", "builder", DiscordRoleMode.Enforce);
		await _harness.Get<IDiscordRoleStore>().RecordAsync(77, new DiscordDrift(555, "Alice", true, DiscordPluginHarness.Now));

		var roles = await ReadAsync<DiscordRolesView>(Admin, HttpMethod.Get, "api/discord/admin/roles");

		var mapping = roles.Mappings.Single();
		await Assert.That(mapping.RoleId).IsEqualTo("77");
		await Assert.That(mapping.Slug).IsEqualTo("builder");
		await Assert.That(mapping.Mode).IsEqualTo("enforce");
		await Assert.That(mapping.Problem).IsEqualTo("the bot is not connected");
		await Assert.That(mapping.Drift).IsEqualTo(1);
		var drift = roles.Drift.Single();
		await Assert.That(drift.UserId).IsEqualTo("555");
		await Assert.That(drift.UserName).IsEqualTo("Alice");
		await Assert.That(drift.Added).IsTrue();
	}

	[Test]
	public async Task MapRole_OfAnUnknownGameRole_IsRefused()
	{
		_harness.Roles.GetRoleAsync("nosuchrole", Arg.Any<CancellationToken>()).Returns(new NotFound());

		var answer = await AnswerAsync(HttpMethod.Post, "api/discord/admin/roles", new DiscordMapRequest("nosuchrole", "Builders"));

		await Assert.That(answer).IsEqualTo(new DiscordAdminAnswer("No game role is called nosuchrole. @role lists them.", "error"));
	}

	[Test]
	public async Task UnmapAndMode_ChangeAMapping()
	{
		await MapAsync(77, "Builders", "builder");

		var badMode = await AnswerAsync(HttpMethod.Put, "api/discord/admin/roles/77/mode", new DiscordModeRequest("sometimes"));
		var enforce = await AnswerAsync(HttpMethod.Put, "api/discord/admin/roles/77/mode", new DiscordModeRequest("Enforce"));
		var report = await AnswerAsync(HttpMethod.Put, "api/discord/admin/roles/77/mode", new DiscordModeRequest("report"));
		var unmapped = await AnswerAsync(HttpMethod.Delete, "api/discord/admin/roles/77");
		var again = await AnswerAsync(HttpMethod.Delete, "api/discord/admin/roles/77");
		var modeOfNothing = await AnswerAsync(HttpMethod.Put, "api/discord/admin/roles/77/mode", new DiscordModeRequest("report"));

		await Assert.That(badMode).IsEqualTo(new DiscordAdminAnswer("The mode is report or enforce.", "error"));
		await Assert.That(enforce).IsEqualTo(new DiscordAdminAnswer("A change to Builders made by hand on Discord is put back at once, and still listed.", "ok"));
		await Assert.That(report).IsEqualTo(new DiscordAdminAnswer("A change to Builders made by hand on Discord is listed and left for you to decide.", "ok"));
		await Assert.That(unmapped).IsEqualTo(new DiscordAdminAnswer("Builders is no longer mapped to builder. Members keep it as it is.", "ok"));
		await Assert.That(again).IsEqualTo(new DiscordAdminAnswer("77 is not a mapped Discord role. @discord/map lists them.", "warn"));
		await Assert.That(modeOfNothing.Kind).IsEqualTo("warn");
		await Assert.That(_harness.Get<IDiscordRoleStore>().Mappings()).IsEmpty();
	}

	[Test]
	public async Task Settle_PutsOneRoleOrEveryRoleBack()
	{
		await MapAsync(77, "Builders", "builder");

		var all = await AnswerAsync(HttpMethod.Post, "api/discord/admin/roles/settle");
		var one = await AnswerAsync(HttpMethod.Post, "api/discord/admin/roles/settle/Builders");
		var unknown = await AnswerAsync(HttpMethod.Post, "api/discord/admin/roles/settle/Nobody");

		await Assert.That(all).IsEqualTo(new DiscordAdminAnswer("Putting every role changed by hand back to the game's roles.", "ok"));
		await Assert.That(one).IsEqualTo(new DiscordAdminAnswer("Putting Builders back to the game's roles wherever it was changed by hand.", "ok"));
		await Assert.That(unknown).IsEqualTo(new DiscordAdminAnswer("That is not a mapped Discord role.", "warn"));
	}

	[Test]
	public async Task Members_ListLinkedAccountsAndSearchThem()
	{
		var store = _harness.Get<IDiscordAccountStore>();
		await store.SaveAsync(new DiscordAccountLink(555, "acct-a", "alice", DiscordPluginHarness.Now));
		await store.SaveAsync(new DiscordAccountLink(556, "acct-b", "bob", DiscordPluginHarness.Now.AddHours(1)));
		await store.SaveAsync(new DiscordAccountLink(557, "acct-c", "carol", DiscordPluginHarness.Now, DiscordPluginHarness.Now.AddHours(2)));
		_harness.Accounts.GetByIdAsync("acct-a", Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = "acct-a", Username = "Alicia" });

		var everyone = await ReadAsync<List<DiscordMemberView>>(Admin, HttpMethod.Get, "api/discord/admin/members");
		var byAccountName = await ReadAsync<List<DiscordMemberView>>(Admin, HttpMethod.Get, "api/discord/admin/members?search=alicia");
		var byId = await ReadAsync<List<DiscordMemberView>>(Admin, HttpMethod.Get, "api/discord/admin/members?search=556");

		// Newest link first; carol unlinked.
		await Assert.That(everyone.Select(member => member.Username)).IsEquivalentTo(["bob", "alice"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
		await Assert.That(byAccountName.Single().AccountName).IsEqualTo("Alicia");
		await Assert.That(byId.Single().Username).IsEqualTo("bob");
	}

	[Test]
	public async Task UnlinkMember_OfSomeoneNotLinked_SaysSo()
	{
		var bad = await AnswerAsync(HttpMethod.Delete, "api/discord/admin/members/alice");
		var nobody = await AnswerAsync(HttpMethod.Delete, "api/discord/admin/members/555");

		await Assert.That(bad).IsEqualTo(new DiscordAdminAnswer("That Discord user is not linked.", "warn"));
		await Assert.That(nobody).IsEqualTo(new DiscordAdminAnswer("That Discord user is not linked.", "warn"));
	}

	[Test]
	public async Task LinkTicket_IsShownAndConfirmedByTheSignedInAccount()
	{
		_harness.Accounts.GetByIdAsync(Member, Arg.Any<CancellationToken>()).Returns(new SharpAccount { Id = Member, Username = "alice" });
		var ticket = _harness.Get<DiscordAccounts>().StartLink(new DiscordUserCard(555, "alice#1", null)).Expect<DiscordLinkTicket>();

		var shown = await ReadAsync<DiscordLinkTicketView>(Member, HttpMethod.Get, $"api/discord/link/{ticket.Id}");
		var confirmed = await ReadAsync<DiscordAccountView>(Member, HttpMethod.Post, $"api/discord/link/{ticket.Id}");
		var account = await ReadAsync<DiscordAccountView>(Member, HttpMethod.Get, "api/discord/account");

		await Assert.That(shown.Username).IsEqualTo("alice#1");
		await Assert.That(confirmed.Username).IsEqualTo("alice#1");
		await Assert.That(account.Username).IsEqualTo("alice#1");
		await Assert.That(_harness.Get<IDiscordAccountStore>().ForAccount(Member)!.UserId).IsEqualTo(555UL);
	}

	[Test]
	public async Task LinkTicket_ThatIsUnknown_IsNotFoundAndCannotBeConfirmed()
	{
		using var shown = await _client.SendAsync(As(Member, HttpMethod.Get, "api/discord/link/nosuchticket"));
		using var confirmed = await _client.SendAsync(As(Member, HttpMethod.Post, "api/discord/link/nosuchticket"));

		await Assert.That(shown.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
		await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
		await Assert.That(await confirmed.Content.ReadAsStringAsync()).Contains("That link has expired or was already used.");
	}

	[Test]
	public async Task Account_WithNoLink_ShowsNoneAndCannotBeUnlinked()
	{
		var account = await ReadAsync<DiscordAccountView>(Member, HttpMethod.Get, "api/discord/account");
		using var unlink = await _client.SendAsync(As(Member, HttpMethod.Delete, "api/discord/account"));

		await Assert.That(account.Username).IsNull();
		await Assert.That(unlink.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
	}

	private void ActorIs(PermissionContext context)
		=> _harness.Capabilities.GetContextAsync(Arg.Is<CapabilityActor>(actor => actor.AccountId == Admin), Arg.Any<CancellationToken>())
			.Returns(context);

	private void GameRole(string slug, string name, int priority = 10, bool administrator = false)
		=> _harness.Roles.GetRoleAsync(slug, Arg.Any<CancellationToken>()).Returns(new SharpRole
		{
			Slug = slug,
			Name = name,
			Priority = priority,
			Permissions = administrator
				? new Dictionary<string, PermissionState> { [PortalPermission.Administrator] = PermissionState.Allow }
				: [],
		});

	private static readonly PermissionContext Owner = new([], new Dictionary<string, PermissionState>(), IsOwner: true);

	[Test]
	[Arguments(BuiltInRoles.EveryoneSlug, "Everyone", false, false, "Everyone is held implicitly, so it cannot be mapped.")]
	[Arguments("council", "Council", true, false, "Council allows administrator, so it cannot be mapped.")]
	[Arguments(BuiltInRoles.WizardSlug, "Wizard", false, false, "Mapping Wizard shows on Discord who is staff. @discord/map/confirm maps it anyway.")]
	[Arguments("builder", "Builder", false, true, "The bot is not connected to Discord.")]
	[Arguments(BuiltInRoles.WizardSlug, "Wizard", false, true, "The bot is not connected to Discord.")]
	public async Task MapRole_RefusesWhatCannotBeMappedAndThenNeedsTheBot(string slug, string name, bool administrator, bool confirm, string message)
	{
		ActorIs(Owner);
		GameRole(slug, name, administrator: administrator);

		var answer = await AnswerAsync(HttpMethod.Post, "api/discord/admin/roles", new DiscordMapRequest(slug, "Builders", confirm));

		await Assert.That(answer).IsEqualTo(new DiscordAdminAnswer(message, "error"));
		await Assert.That(_harness.Get<IDiscordRoleStore>().Mappings()).IsEmpty();
	}

	[Test]
	public async Task MapRole_AboveTheAdminsOwnRoles_IsRefused()
	{
		ActorIs(PermissionContext.None);
		GameRole("builder", "Builder", priority: 10);

		var answer = await AnswerAsync(HttpMethod.Post, "api/discord/admin/roles", new DiscordMapRequest("builder", "Builders"));

		await Assert.That(answer).IsEqualTo(new DiscordAdminAnswer("Builder is not below your highest role, so you cannot map it.", "error"));
	}
}
