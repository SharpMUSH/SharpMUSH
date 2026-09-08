using System.Net.Sockets;

namespace SharpMUSH.Tests.ConnectionServer;

public class RenderingWorkerHealthTests
{
	[Test]
	public async Task HealthRequiresRespondingWorker()
	{
		var directory = Path.Combine(Path.GetTempPath(), "sm-health-" + Guid.NewGuid().ToString("N"));
		var socketPath = Path.Combine(directory, "render.sock");
		await using var worker = SharpMUSH.RenderingWorker.Program.CreateApplication([], socketPath);
		try
		{
			await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(socketPath)).IsFalse();
			await worker.StartAsync();
			await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(socketPath)).IsTrue();
			await worker.StopAsync();
			await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(socketPath)).IsFalse();
		}
		finally
		{
			await worker.DisposeAsync();
			Directory.Delete(directory, recursive: true);
		}
	}

	[Test]
	public async Task SocketListenerWithoutHttpResponseIsUnhealthy()
	{
		var socketPath = Path.Combine(Path.GetTempPath(), "sm-health-" + Guid.NewGuid().ToString("N"));
		using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			listener.Bind(new UnixDomainSocketEndPoint(socketPath));
			listener.Listen(1);
			await Assert.That(await SharpMUSH.RenderingWorker.Program.IsHealthyAsync(socketPath)
				.WaitAsync(TimeSpan.FromSeconds(10))).IsFalse();
		}
		finally
		{
			listener.Dispose();
			File.Delete(socketPath);
		}
	}
}
