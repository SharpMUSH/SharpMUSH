using System.Reflection;
using SharpMUSH.Library.Attributes;

namespace SharpMUSH.Tests.LanguageServer;

/// <summary>
/// Guards the ParameterNames declared on [SharpFunction]s. These drive the LSP's inlay hints
/// (see InlayHintHandler), so a wrong or mislabelled entry silently annotates real softcode with
/// the wrong argument name — a defect no runtime test would ever catch.
/// </summary>
public class FunctionParameterNamesTests
{
	private static IEnumerable<(string Name, SharpFunctionAttribute Attr)> AllFunctions()
	{
		var assembly = typeof(SharpMUSH.Implementation.Functions.Functions).Assembly;
		foreach (var type in assembly.GetTypes())
		{
			foreach (var method in type.GetMethods(
				BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
			{
				foreach (var attr in method.GetCustomAttributes<SharpFunctionAttribute>())
				{
					yield return (attr.Name, attr);
				}
			}
		}
	}

	/// <summary>
	/// A name can only annotate an argument the function actually accepts. More names than MaxArgs
	/// means at least one hint is attached to a position that can never be supplied.
	/// (A single "param..." variadic entry names one-or-more positions, so it never trips this.)
	/// </summary>
	[Test]
	public async Task NoFunctionNamesMoreParametersThanItAccepts()
	{
		var offenders = AllFunctions()
			.Where(f => f.Attr.ParameterNames.Length > 0)
			.Where(f => !f.Attr.ParameterNames.Any(n => n.Contains("...")))
			.Where(f => f.Attr.ParameterNames.Length > f.Attr.MaxArgs)
			.Select(f => $"{f.Name}: {f.Attr.ParameterNames.Length} names > MaxArgs {f.Attr.MaxArgs}")
			.ToList();

		await Assert.That(offenders).IsEmpty();
	}

	/// <summary>
	/// The ufun-style list functions all read Arguments["0"] as the attribute/predicate to
	/// evaluate, so their first inlay hint must read "attribute". filterbool() ("list") and step()
	/// ("start") both violated this — the names shifted or were copied from another function,
	/// mislabelling every argument. This is the exact defect class that reflection can pin.
	/// </summary>
	[Test]
	[Arguments("filter")]
	[Arguments("filterbool")]
	[Arguments("map")]
	[Arguments("mix")]
	[Arguments("munge")]
	[Arguments("step")]
	[Arguments("fold")]
	public async Task UfunStyleFunctions_NameTheirFirstArgumentAttribute(string functionName)
	{
		var fn = AllFunctions().FirstOrDefault(f => f.Name == functionName);
		await Assert.That(fn.Attr).IsNotNull();
		await Assert.That(fn.Attr.ParameterNames.Length).IsGreaterThan(0);
		await Assert.That(fn.Attr.ParameterNames[0]).IsEqualTo("attribute");
	}
}
