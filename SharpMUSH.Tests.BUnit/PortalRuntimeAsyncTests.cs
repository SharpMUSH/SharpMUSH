using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// The portal runs on the browser's Mono runtime, which cannot run a runtime-async method: one compiled with
/// <c>runtime-async=on</c> fails the portal in <c>Main</c>. Directory.Build.targets turns runtime async on for every
/// net11.0 project, and the portal's own assemblies opt out with <c>UseRuntimeAsync=false</c>. This fails when a
/// project the portal comes to reference has not opted out.
/// </summary>
public class PortalRuntimeAsyncTests
{
	[Test]
	public async Task PortalAssemblies_HaveNoRuntimeAsyncMethods()
	{
		var offenders = PortalAssemblies()
			.SelectMany(path => RuntimeAsyncMethods(path).Select(method => $"{Path.GetFileName(path)}: {method}"))
			.Take(10)
			.ToList();

		await Assert.That(offenders).IsEmpty();
	}

	[Test]
	public async Task PortalAssemblies_IncludeTheContractAssemblies()
	{
		var names = PortalAssemblies().Select(Path.GetFileNameWithoutExtension).ToList();

		await Assert.That(names).Contains("SharpMUSH.Client");
		await Assert.That(names).Contains("SharpMUSH.Contracts");
		await Assert.That(names).Contains("SharpMUSH.Configuration");
	}

	/// <summary>SharpMUSH.Client and every SharpMUSH assembly it references, directly or not.</summary>
	private static List<string> PortalAssemblies()
	{
		var directory = AppContext.BaseDirectory;
		var seen = new HashSet<string>(StringComparer.Ordinal);
		var pending = new Queue<string>(["SharpMUSH.Client"]);
		var paths = new List<string>();

		while (pending.TryDequeue(out var name))
		{
			var path = Path.Combine(directory, $"{name}.dll");
			if (!seen.Add(name) || !File.Exists(path))
			{
				continue;
			}

			paths.Add(path);
			foreach (var reference in References(path).Where(r => r.StartsWith("SharpMUSH.", StringComparison.Ordinal)))
			{
				pending.Enqueue(reference);
			}
		}

		return paths;
	}

	private static List<string> References(string path)
	{
		using var stream = File.OpenRead(path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		return [.. reader.AssemblyReferences.Select(h => reader.GetString(reader.GetAssemblyReference(h).Name))];
	}

	private static List<string> RuntimeAsyncMethods(string path)
	{
		using var stream = File.OpenRead(path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		return [.. reader.MethodDefinitions
			.Select(reader.GetMethodDefinition)
			.Where(method => (method.ImplAttributes & MethodImplAttributes.Async) != 0)
			.Select(method => $"{reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name)}.{reader.GetString(method.Name)}")];
	}
}
