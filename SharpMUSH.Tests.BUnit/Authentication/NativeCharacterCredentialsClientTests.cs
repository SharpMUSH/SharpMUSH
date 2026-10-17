using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NSubstitute;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Tests.BUnit.Authentication;

public class NativeCharacterCredentialsClientTests
{
	private sealed class Handler : HttpMessageHandler
	{
		public string? Path, Body;
		public HttpMethod? Method;
		public object? Reply;
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Path = request.RequestUri!.AbsolutePath;
			Method = request.Method;
			Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
			return new HttpResponseMessage(Reply is null ? HttpStatusCode.NoContent : HttpStatusCode.OK)
			{
				Content = Reply is null ? null : JsonContent.Create(Reply)
			};
		}
	}

	[Test]
	public async Task MintSendsFullCharacterIdentityAndOneUseProofOnlyInRequestBody()
	{
		using var handler = new Handler { Reply = new NativeLoginSecretResponse("opaque-id", "nc1_secret", 10, 1000, DateTimeOffset.UtcNow.AddMinutes(2)) };
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(http);
		await new NativeCharacterCredentialsClient(factory).MintAsync(10, 1000, "one-use-proof");
		await Assert.That(handler.Path).IsEqualTo("/api/account/characters/10/native-login-secrets");
		using var body = JsonDocument.Parse(handler.Body!);
		await Assert.That(body.RootElement.GetProperty("characterCreationTime").GetInt64()).IsEqualTo(1000);
		await Assert.That(body.RootElement.GetProperty("proofGrant").GetString()).IsEqualTo("one-use-proof");
	}

	[Test]
	public async Task PasswordlessCreationSendsExplicitNullAndDoesNotInventEmptyPassword()
	{
		using var handler = new Handler { Reply = new AccountApiClient.CreateCharacterResponse(10, 1000) };
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(http);
		await new NativeCharacterCredentialsClient(factory).CreateWithoutPasswordAsync("New Character");
		await Assert.That(handler.Path).IsEqualTo("/api/account/characters");
		using var body = JsonDocument.Parse(handler.Body!);
		await Assert.That(body.RootElement.GetProperty("password").ValueKind).IsEqualTo(JsonValueKind.Null);
	}

	[Test]
	public async Task RevocationUsesOpaqueStatusIdWithoutNativeSecret()
	{
		using var handler = new Handler();
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>(); factory.CreateClient("api").Returns(http);
		await new NativeCharacterCredentialsClient(factory).RevokeAsync("opaque-id");
		await Assert.That(handler.Method).IsEqualTo(HttpMethod.Delete);
		await Assert.That(handler.Path).IsEqualTo("/api/account/native-login-secrets/opaque-id");
		await Assert.That(handler.Body).IsNull();
	}
}
