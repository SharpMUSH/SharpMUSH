using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;

namespace SharpMUSH.Tests;

/// <summary>
/// Labels every test container and network with the test process that owns it, and removes the
/// ones whose owner has died before a new session starts its own.
/// </summary>
/// <remarks>
/// Local runs disable Ryuk (<c>TESTCONTAINERS_RYUK_DISABLED=true</c>) because it does not work under
/// rootless podman. Without it, a test host that is killed rather than disposed leaves its MySQL and
/// NATS containers running indefinitely; on the shared dev host 22 of them had piled up, each mysqld
/// holding about 420 MB, before memory pressure took the host down.
/// </remarks>
public static class TestContainerJanitor
{
	public const string OwnerPidLabel = "sharpmush.tests.owner-pid";
	public const string OwnerStartLabel = "sharpmush.tests.owner-start";
	public const string OwnerHostLabel = "sharpmush.tests.owner-host";

	/// <summary>
	/// Process ids are reused, so ownership is the pid together with the process start time
	/// (Unix seconds). A container is an orphan only when no process has that pid and start time.
	/// </summary>
	public static IReadOnlyDictionary<string, string> OwnerLabels { get; } = CreateOwnerLabels();

	private static readonly Lazy<Task> Sweep = new(() => SweepAsync());

	/// <summary>Removes orphaned test containers and networks once per test process.</summary>
	public static Task SweepOnceAsync() => Sweep.Value;

	public static async Task SweepAsync(CancellationToken cancellationToken = default)
	{
		// Testcontainers leaves this null when none of the endpoints it probes answers.
		var endpoint = TestcontainersSettings.OS.DockerEndpointAuthConfig
			?? throw Unreachable(Environment.GetEnvironmentVariable("DOCKER_HOST") ?? "the default Docker endpoint", null);
		using var client = endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
		var ownedFilter = new Dictionary<string, IDictionary<string, bool>>
		{
			["label"] = new Dictionary<string, bool> { [OwnerPidLabel] = true }
		};

		IList<ContainerListResponse> containers;
		try
		{
			containers = await client.Containers.ListContainersAsync(
				new ContainersListParameters { All = true, Filters = ownedFilter }, cancellationToken);
		}
		catch (Exception ex) when (IsUnreachable(ex))
		{
			throw Unreachable(endpoint.Endpoint.ToString(), ex);
		}

		var removed = 0;
		foreach (var container in containers.Where(c => IsOrphaned(c.Labels)))
		{
			try
			{
				await client.Containers.RemoveContainerAsync(container.ID,
					new ContainerRemoveParameters { Force = true, RemoveVolumes = true }, cancellationToken);
				removed++;
			}
			catch (DockerApiException)
			{
				// Removed concurrently by another session's sweep, or already being torn down.
			}
		}

		var networks = await client.Networks.ListNetworksAsync(
			new NetworksListParameters { Filters = ownedFilter }, cancellationToken);
		foreach (var network in networks.Where(n => IsOrphaned(n.Labels)))
		{
			try
			{
				await client.Networks.DeleteNetworkAsync(network.ID, cancellationToken);
			}
			catch (DockerApiException)
			{
				// Still attached to a container another sweep is removing; the next sweep gets it.
			}
		}

		if (removed > 0)
			await Console.Error.WriteLineAsync(
				$"TestContainerJanitor: removed {removed} test container(s) left by test processes that no longer exist.");
	}

	/// <summary>
	/// True only when the labels name an owner on this machine that is provably gone. Anything
	/// unlabeled, foreign, or unreadable is left alone.
	/// </summary>
	public static bool IsOrphaned(IDictionary<string, string>? labels)
	{
		if (labels is null
				|| !labels.TryGetValue(OwnerHostLabel, out var host)
				|| !string.Equals(host, Environment.MachineName, StringComparison.Ordinal)
				|| !labels.TryGetValue(OwnerPidLabel, out var pidText) || !int.TryParse(pidText, out var pid)
				|| !labels.TryGetValue(OwnerStartLabel, out var startText) || !long.TryParse(startText, out var start))
			return false;

		return !IsRunning(pid, start);
	}

	private static bool IsRunning(int pid, long startSeconds)
	{
		try
		{
			using var process = Process.GetProcessById(pid);
			return Math.Abs(StartSeconds(process) - startSeconds) <= 1;
		}
		catch (ArgumentException)
		{
			return false; // No process has this id.
		}
		catch (InvalidOperationException)
		{
			return false; // It exited while being inspected.
		}
		catch (Win32Exception)
		{
			return true; // Cannot inspect it; assume it is alive rather than remove a live session's containers.
		}
	}

	private static long StartSeconds(Process process) =>
		new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();

	private static InvalidOperationException Unreachable(string endpoint, Exception? cause) => new(
		$"The container runtime at {endpoint} is not accepting connections, so no test container can start. " +
		"On a host using the podman user socket, restore it with `systemctl --user restart podman.socket`. " +
		"Do not start a private `podman system service` on that path: it replaces the shared listener and " +
		"leaves a dead socket behind when it exits.", cause);

	private static bool IsUnreachable(Exception ex)
	{
		for (var inner = ex; inner is not null; inner = inner.InnerException)
			if (inner is SocketException or HttpRequestException { StatusCode: null })
				return true;
		return false;
	}

	private static Dictionary<string, string> CreateOwnerLabels()
	{
		using var self = Process.GetCurrentProcess();
		return new Dictionary<string, string>
		{
			[OwnerPidLabel] = Environment.ProcessId.ToString(),
			[OwnerStartLabel] = StartSeconds(self).ToString(),
			[OwnerHostLabel] = Environment.MachineName
		};
	}
}
