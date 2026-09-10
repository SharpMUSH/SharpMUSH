using System.Net;
using Bunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.Client.Authentication;
using SharpMUSH.Client.Services;
using SharpMUSH.Library.Authorization;

namespace SharpMUSH.Tests.BUnit.Authentication;

public class AccountAuthServiceRefreshBudgetTests : TrackingBunitContext
{
	private sealed class StallStream(Func<CancellationToken, Task> stall) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position { get => 0; set => throw new NotSupportedException(); }
		public override void Flush() { }
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			await stall(cancellationToken);
			return 0;
		}
		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
	}

	private sealed class StallHandler(bool body, Func<CancellationToken, Task> stall) : HttpMessageHandler
	{
		public int Calls { get; private set; }
		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Calls++;
			if (!body) await stall(cancellationToken);
			return new(HttpStatusCode.OK) { Content = new StreamContent(new StallStream(stall)) };
		}
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task StalledRefreshReleasesAllHydrationWaitersWithoutRestoringGrants(bool body)
	{
		JSInterop.Mode = JSRuntimeMode.Loose;
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.sessionToken").SetResult("stored-token");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.username").SetResult("staff");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.role").SetResult("God");
		JSInterop.Setup<string?>("sessionStorage.getItem", "sharpmush.account.permissions").SetResult("[\"*\"]");
		using var cleanup = new CancellationTokenSource();
		var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		CancellationToken observed = default;
		using var handler = new StallHandler(body, async token =>
		{
			observed = token;
			entered.TrySetResult();
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cleanup.Token);
			await Task.Delay(Timeout.Infinite, linked.Token);
		});
		using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
		var factory = Substitute.For<IHttpClientFactory>();
		factory.CreateClient("api").Returns(http);
		var service = new AccountAuthService(factory, JSInterop.JSRuntime, NullLogger<AccountAuthService>.Instance,
			Substitute.For<ITerminalService>(), Substitute.For<IPlayTerminalService>());
		var provider = new AccountAuthStateProvider(service);
		var authentication = provider.GetAuthenticationStateAsync();
		var second = service.InitAsync();
		try
		{
			await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
			await Task.WhenAll(authentication, second).WaitAsync(TimeSpan.FromSeconds(8));
			await Assert.That(observed.IsCancellationRequested).IsTrue();
			await Assert.That(service.AccountSessionToken).IsEqualTo("stored-token");
			await Assert.That(service.Permissions).IsEmpty();
			await Assert.That((await authentication).User.HasClaim(c => c.Type == PortalPermission.ClaimType)).IsFalse();
			await Assert.That(handler.Calls).IsEqualTo(1);
			await service.InitAsync().WaitAsync(TimeSpan.FromSeconds(1));
		}
		finally
		{
			cleanup.Cancel();
			await Task.WhenAll(authentication, second);
		}
	}
}
