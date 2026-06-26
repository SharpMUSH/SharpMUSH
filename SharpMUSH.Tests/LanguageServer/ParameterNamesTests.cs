namespace SharpMUSH.Tests.LanguageServer;

/// <summary>
/// Tests for parameter name expansion in LSP handlers.
/// Validates fixed, variadic, and paired parameter patterns.
/// </summary>
public class ParameterNamesTests
{
	/// <summary>
	/// Helper method to test ExpandParameterName logic (mimics the LSP handler logic)
	/// </summary>
	private static string ExpandParameterName(string[] parameterNames, int index)
	{
		int currentIndex = 0;

		foreach (var paramName in parameterNames)
		{
			if (paramName.Contains("..."))
			{
				if (paramName.Contains("|"))
				{
					var parts = paramName.Split('|');
					var cleanParts = parts.Select(p => p.Replace("...", "").Trim()).ToArray();

					var pairIndex = (index - currentIndex) / cleanParts.Length;
					var partIndex = (index - currentIndex) % cleanParts.Length;

					return $"{cleanParts[partIndex]}{pairIndex + 1}";
				}
				else
				{
					var cleanName = paramName.Replace("...", "").Trim();
					return $"{cleanName}{index - currentIndex + 1}";
				}
			}
			else
			{
				if (index == currentIndex)
				{
					return paramName;
				}
				currentIndex++;
			}
		}

		return $"arg{index + 1}";
	}

	[Test]
	public async Task FixedParameters_ReturnCorrectNames()
	{
		var paramNames = new[] { "number1", "number2" };

		await Assert.That(ExpandParameterName(paramNames, 0)).IsEqualTo("number1");
		await Assert.That(ExpandParameterName(paramNames, 1)).IsEqualTo("number2");
	}

	[Test]
	public async Task VariadicParameters_GenerateNumberedNames()
	{
		var paramNames = new[] { "number..." };

		await Assert.That(ExpandParameterName(paramNames, 0)).IsEqualTo("number1");
		await Assert.That(ExpandParameterName(paramNames, 1)).IsEqualTo("number2");
		await Assert.That(ExpandParameterName(paramNames, 2)).IsEqualTo("number3");
		await Assert.That(ExpandParameterName(paramNames, 9)).IsEqualTo("number10");
	}

	[Test]
	public async Task PairedParameters_AlternateCorrectly()
	{
		var paramNames = new[] { "string", "case...|result...", "default" };

		await Assert.That(ExpandParameterName(paramNames, 0)).IsEqualTo("string");

		await Assert.That(ExpandParameterName(paramNames, 1)).IsEqualTo("case1");
		await Assert.That(ExpandParameterName(paramNames, 2)).IsEqualTo("result1");
		await Assert.That(ExpandParameterName(paramNames, 3)).IsEqualTo("case2");
		await Assert.That(ExpandParameterName(paramNames, 4)).IsEqualTo("result2");
		await Assert.That(ExpandParameterName(paramNames, 5)).IsEqualTo("case3");
		await Assert.That(ExpandParameterName(paramNames, 6)).IsEqualTo("result3");
	}

	[Test]
	public async Task MixedFixedAndVariadic_WorkCorrectly()
	{
		var paramNames = new[] { "object/attribute", "arguments..." };

		await Assert.That(ExpandParameterName(paramNames, 0)).IsEqualTo("object/attribute");
		await Assert.That(ExpandParameterName(paramNames, 1)).IsEqualTo("arguments1");
		await Assert.That(ExpandParameterName(paramNames, 2)).IsEqualTo("arguments2");
		await Assert.That(ExpandParameterName(paramNames, 3)).IsEqualTo("arguments3");
	}

	[Test]
	public async Task RealWorldExample_Cond()
	{
		var paramNames = new[] { "expression...|result...", "default" };

		await Assert.That(ExpandParameterName(paramNames, 0)).IsEqualTo("expression1");
		await Assert.That(ExpandParameterName(paramNames, 1)).IsEqualTo("result1");
		await Assert.That(ExpandParameterName(paramNames, 2)).IsEqualTo("expression2");
		await Assert.That(ExpandParameterName(paramNames, 3)).IsEqualTo("result2");
	}
}
