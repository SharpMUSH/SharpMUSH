using System.Net;
using System.Net.Http.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor.Services;
using MudBlazor;
using SharpMUSH.Client.Pages.Admin;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.API;
using SharpMUSH.Tests.BUnit.Resources;

namespace SharpMUSH.Tests.BUnit.Pages;

public class AdminRemoteBackupsTests : TrackingBunitContext
{
	private sealed class Wire(RemoteBackupStatus status) : HttpMessageHandler
	{
		public readonly List<string> Calls = [];
		public RemoteBackupConfigure? Configured;
		public bool FailNextStatus;
		public bool FailRun;
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
		{
			Calls.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
			if (request.Method == HttpMethod.Get && FailNextStatus)
			{
				FailNextStatus = false;
				return new(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("status-unavailable") };
			}
			if (request.Method == HttpMethod.Post && FailRun)
				return new(HttpStatusCode.BadRequest) { Content = new StringContent("run-rejected") };
			if (request.Method == HttpMethod.Put) Configured = await request.Content!.ReadFromJsonAsync<RemoteBackupConfigure>(ct);
			return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(status) };
		}
	}

	private Wire Setup(RemoteBackupStatus status)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddMudServices();
		Services.AddSingleton<IStringLocalizer<SharedResource>, EchoLocalizer<SharedResource>>();
		var wire = new Wire(status);
		Services.AddHttpClient("api", client => client.BaseAddress = new("https://game.example/")).ConfigurePrimaryHttpMessageHandler(() => wire);
		Services.AddSingleton<RemoteBackupsService>();
		AddAuthorization().SetAuthorized("administrator");
		return wire;
	}

	[Test]
	public async Task SuccessfulStatusPollClearsItsEarlierError()
	{
		var wire = Setup(new(new(true, 24, "b2:bucket:path"), true, true, true, DateTimeOffset.UtcNow, null, []));
		wire.FailNextStatus = true;
		var page = Render<AdminRemoteBackups>();
		page.WaitForAssertion(() => { if (!page.Markup.Contains("status-unavailable")) throw new Exception("Initial status error missing"); });
		page.WaitForAssertion(() =>
		{
			if (!page.Markup.Contains("AdmRemoteBackupAgentSeen") || page.Markup.Contains("status-unavailable"))
				throw new Exception("Recovered status kept its old error");
		}, TimeSpan.FromSeconds(15));
		await Assert.That(wire.Calls.Count(call => call.StartsWith("GET", StringComparison.Ordinal))).IsGreaterThanOrEqualTo(2);
	}

	[Test]
	public async Task SuccessfulStatusReadDoesNotHideRejectedRun()
	{
		var wire = Setup(new(new(true, 24, "b2:bucket:path"), true, true, true, DateTimeOffset.UtcNow, null, []));
		wire.FailRun = true;
		var page = Render<AdminRemoteBackups>();
		page.WaitForAssertion(() => { if (!page.Markup.Contains("AdmRemoteBackupAgentSeen")) throw new Exception("Status missing"); });
		await page.FindAll("button").Single(button => button.TextContent.Contains("AdmRemoteBackupNow")).ClickAsync();
		await Assert.That(page.Markup).Contains("run-rejected");
	}

	[Test]
	public async Task MissingAgentAndLocalCopyDoNotShowRemoteSuccess()
	{
		Setup(new(new(true, 24, "b2:bucket:path"), true, true, true, null, null,
			[new(Guid.NewGuid(), "manual", "ready", DateTimeOffset.UtcNow)]));
		var page = Render<AdminRemoteBackups>();
		page.WaitForAssertion(() => { if (!page.Markup.Contains("AdmRemoteBackupAgentMissing")) throw new Exception("Missing agent was not shown"); });
		await Assert.That(page.Markup.Contains("AdmRemoteBackupNever")).IsTrue();
		await Assert.That(page.FindAll("input[type=password]").All(input => string.IsNullOrEmpty(input.GetAttribute("value")))).IsTrue();
	}

	[Test]
	public async Task UnknownJobHasExplicitRecoveryControl()
	{
		var id = Guid.NewGuid();
		var wire = Setup(new(new(true, 24, "b2:bucket:path"), true, true, true, DateTimeOffset.UtcNow, null,
			[new(id, "manual", "unknown", DateTimeOffset.UtcNow)]));
		var page = Render<AdminRemoteBackups>();
		page.WaitForAssertion(() => { if (!page.Markup.Contains("AdmRemoteBackupAbandonWarning")) throw new Exception("Unknown recovery warning missing"); });
		await page.FindAll("button").Single(button => button.TextContent.Contains("AdmRemoteBackupAbandon")).ClickAsync();
		await Assert.That(wire.Calls.Contains($"POST /api/admin/backups/remote/{id}/abandon")).IsTrue();
	}
	[Test]
	public async Task CredentialClearControlsSendExplicitFlagsWithoutReadingSecrets()
	{
		var wire = Setup(new(new(true, 24, "b2:bucket:path"), true, true, true, DateTimeOffset.UtcNow, null, []));
		var page = Render<AdminRemoteBackups>();
		page.WaitForAssertion(() => { if (page.FindComponents<MudCheckBox<bool>>().Count != 2) throw new Exception("Credential clear controls missing"); });
		foreach (var clear in page.FindComponents<MudCheckBox<bool>>())
			await page.InvokeAsync(() => clear.Instance.ValueChanged.InvokeAsync(true));
		await page.FindAll("button").Single(button => button.TextContent.Contains("AdmRemoteBackupSave")).ClickAsync();
		await Assert.That(wire.Configured!.ClearAccountId).IsTrue();
		await Assert.That(wire.Configured.ClearAccountKey).IsTrue();
		await Assert.That(wire.Configured.Password).IsEqualTo("");
	}

}
