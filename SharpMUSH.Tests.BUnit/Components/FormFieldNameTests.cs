using System.Text.RegularExpressions;

namespace SharpMUSH.Tests.BUnit.Components;

/// <summary>
/// Chrome lists every form field with neither an id nor a name under Issues ("A form field element
/// should have an id or name attribute"), because autofill cannot tell such fields apart.
/// </summary>
public class FormFieldNameTests
{
	[Test]
	public async Task EveryFormField_HasAnIdOrName()
	{
		var unnamed = new List<string>();
		foreach (var file in Directory.EnumerateFiles(ClientSource.RazorRoot, "*.razor", SearchOption.AllDirectories))
		{
			var source = File.ReadAllText(file);
			foreach (Match m in Regex.Matches(source, @"<(?:input|textarea|select)\b[^>]*"))
			{
				if (!Regex.IsMatch(m.Value, @"\s(?:id|name)="))
				{
					var line = source[..m.Index].Count(c => c == '\n') + 1;
					unnamed.Add($"{Path.GetRelativePath(ClientSource.RazorRoot, file)}:{line}");
				}
			}
		}

		await Assert.That(unnamed).IsEmpty().Because(string.Join("\n", unnamed));
	}
}
