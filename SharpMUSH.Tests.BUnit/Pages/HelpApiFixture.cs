using Bunit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor.Services;
using NSubstitute;
using SharpMUSH.Client.Resources;
using SharpMUSH.Client.Services;
using SharpMUSH.Implementation.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Server.Controllers;
using SharpMUSH.Tests.BUnit.Resources;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Pages;

/// <summary>
/// A text-file index built from a dictionary instead of a directory, so the real
/// <see cref="HelpTopicResolver"/> can run its actual matching over a corpus a test can state in
/// four lines. Entry keys are topic names, exactly as the markdown-header indexer produces them.
/// </summary>
internal sealed class DictionaryTextFileService(Dictionary<string, Dictionary<string, string>> corpora)
	: ITextFileService
{
	private Dictionary<string, string>? Corpus(string reference) =>
		corpora.TryGetValue(reference.Split('/')[0], out var entries) ? entries : null;

	public Task<IEnumerable<string>> ListCategoriesAsync() =>
		Task.FromResult<IEnumerable<string>>(corpora.Keys);

	public Task<string> ListEntriesAsync(string fileReference, string separator = " ") =>
		Task.FromResult(string.Join(separator, Corpus(fileReference)?.Keys ?? Enumerable.Empty<string>()));

	public Task<string?> GetEntryAsync(string fileReference, string entryName)
	{
		var corpus = Corpus(fileReference);
		return Task.FromResult(corpus is not null && corpus.TryGetValue(entryName, out var body) ? body : null);
	}

	public Task<IEnumerable<string>> ListFilesAsync(string? category = null) =>
		Task.FromResult<IEnumerable<string>>([]);

	public Task<string?> GetFileContentAsync(string fileReference) => Task.FromResult<string?>(null);

	public Task<IEnumerable<string>> SearchEntriesAsync(string fileReference, string pattern)
	{
		var regex = new Regex(
			"^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",
			RegexOptions.IgnoreCase);
		return Task.FromResult<IEnumerable<string>>(
			(Corpus(fileReference)?.Keys ?? Enumerable.Empty<string>()).Where(k => regex.IsMatch(k)).ToList());
	}

	public Task<IEnumerable<string>> SearchContentAsync(string fileReference, string searchTerm) =>
		Task.FromResult<IEnumerable<string>>(
			(Corpus(fileReference) ?? new Dictionary<string, string>())
			.Where(kv => kv.Value.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
			.Select(kv => kv.Key)
			.ToList());

	public Task ReindexAsync() => Task.CompletedTask;
}

/// <summary>
/// Serves <c>/api/help*</c> by invoking the real <see cref="HelpController"/>, so the pages are
/// tested against the shapes and the HTML the server actually produces rather than against a
/// hand-written fixture that could drift from it.
/// </summary>
/// <remarks>
/// The controller's <c>[Authorize(Roles = "Wizard,God")]</c> gate is framework-enforced and does not
/// run here; that refusal is asserted over real HTTP in
/// <c>SharpMUSH.Tests.Integration.Portal.HelpApiTests</c>. What this handler does honour is the
/// <paramref name="isStaff"/> flag, so the page's own decision about whether to ask for the admin
/// corpus can be observed.
/// </remarks>
internal sealed class HelpApiHandler(IHelpTopicResolver resolver, bool isStaff) : HttpMessageHandler
{
	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var controller = new HelpController(resolver);
		var path = request.RequestUri!.AbsolutePath.TrimStart('/');
		var topic = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query)["topic"];

		if (path.StartsWith("api/help/admin", StringComparison.Ordinal) && !isStaff)
		{
			return new HttpResponseMessage(HttpStatusCode.Forbidden);
		}

		object? result = path switch
		{
			"api/help" => (await controller.Index()).Result,
			"api/help/entry" => (await controller.Entry(topic)).Result,
			"api/help/admin" => (await controller.AdminIndex()).Result,
			"api/help/admin/entry" => (await controller.AdminEntry(topic)).Result,
			_ => null
		};

		return result switch
		{
			OkObjectResult ok => Json(ok.Value!, HttpStatusCode.OK),
			NotFoundObjectResult missing => Json(missing.Value!, HttpStatusCode.NotFound),
			_ => new HttpResponseMessage(HttpStatusCode.NotFound)
		};
	}

	private static HttpResponseMessage Json(object value, HttpStatusCode status) =>
		new(status) { Content = JsonContent.Create(value, value.GetType()) };
}

/// <summary>Registers the help pages' services over <see cref="HelpApiHandler"/> and the real resolver.</summary>
internal static class HelpApi
{
	/// <summary>
	/// <see cref="GameHelpService"/>, MudBlazor and the HTTP client, answering from <paramref name="corpora"/>.
	/// The localizer is the caller's: the page tests read keys, the sidebar tests read English.
	/// </summary>
	public static void Install(TrackingBunitContext ctx, bool isStaff, Dictionary<string, Dictionary<string, string>> corpora)
	{
		var resolver = new HelpTopicResolver(new DictionaryTextFileService(corpora));
		var client = ctx.Track(new HttpClient(new HelpApiHandler(resolver, isStaff))
		{
			BaseAddress = new Uri("https://localhost:8081/")
		});

		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(client);

		ctx.Services
			.AddSingleton(client)
			.AddMudServices()
			.AddSingleton(factory)
			.AddSingleton(sp => new GameHelpService(
				sp.GetRequiredService<IHttpClientFactory>(),
				NullLogger<GameHelpService>.Instance));

		ctx.JSInterop.Mode = JSRuntimeMode.Loose;
	}
}

