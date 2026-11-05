using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using SharpMUSH.Client.Components.Kit;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Components.Kit;

/// <summary>
/// The library's search says what it is doing in one status region that is there from the start, so a screen
/// reader hears "Loading" and then the outcome; a region inserted already holding its text is seldom read.
/// </summary>
public class ImageLibraryGridTests : TrackingBunitContext
{
	private sealed class HeldHandler : HttpMessageHandler
	{
		public TaskCompletionSource<HttpResponseMessage> Answer { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
			Answer.Task;
	}

	private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) =>
			new(handler, disposeHandler: false) { BaseAddress = new Uri("https://localhost/") };
	}

	[Test]
	public async Task OneStatusRegion_SaysLoadingThenTheOutcome()
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		Services.AddMudServices();
		Services.AddLocalization();
		using var handler = new HeldHandler();
		Services.AddSingleton(new WikiAssetService(new SingleClientFactory(handler), NullLogger<WikiAssetService>.Instance));

		var cut = Render<ImageLibraryGrid>();

		var loading = cut.FindAll("[role='status']");
		await Assert.That(loading.Count).IsEqualTo(1);
		await Assert.That(loading[0].TextContent.Trim()).IsNotEmpty();
		await Assert.That(cut.FindAll(".kit-library-grid[aria-hidden='true'] .kit-skeleton").Count).IsEqualTo(8);
		var loadingText = loading[0].TextContent.Trim();

		handler.Answer.SetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
		{
			Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json")
		});

		cut.WaitForAssertion(() => cut.Find(".kit-library-empty"));
		var done = cut.FindAll("[role='status']");
		await Assert.That(done.Count).IsEqualTo(1);
		await Assert.That(done[0].TextContent.Trim()).IsNotEmpty();
		await Assert.That(done[0].TextContent.Trim()).IsNotEqualTo(loadingText);
	}
}
