using System.Xml.Linq;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Wiki;

public class WikiTableRenderingTests
{
	[Test]
	public async Task WideEquipmentTableKeepsItsSemanticsInsideAKeyboardScrollableContainer()
	{
		var html = new WikiMarkdigPipeline().RenderToHtml("Before.\n\n"
			+ "| Category | Group | Damage | Hands | Range | Reload |\n"
			+ "| --- | --- | --- | --- | --- | --- |\n"
			+ "| Martial | Sword | 1d8 slashing | 1 | — | [[Reload|main:reload]] |\n\nAfter.");
		var document = XElement.Parse("<article>" + html + "</article>");
		var table = document.Descendants("table").Single();
		var container = table.Parent!;

		await Assert.That(container.Name.LocalName).IsEqualTo("div");
		await Assert.That((string?)container.Attribute("class")).IsEqualTo("wiki-table-scroll");
		await Assert.That((string?)container.Attribute("tabindex")).IsEqualTo("0");
		await Assert.That(container.Elements().Single()).IsEqualTo(table);
		await Assert.That(table.Descendants("th").Count()).IsEqualTo(6);
		await Assert.That((string?)table.Descendants("a").Single().Attribute("href")).IsEqualTo("/wiki/main/reload");
		await Assert.That(string.Join("|", document.Elements("p").Select(p => p.Value))).IsEqualTo("Before.|After.");
	}

	[Test]
	public async Task SeparateTablesReceiveSeparateContainersWithoutChangingTheirContents()
	{
		const string table = "| Level | DC |\n| --- | --- |\n| 1 | 15 |\n";
		var html = new WikiMarkdigPipeline().RenderToHtml(table + "\nBetween.\n\n" + table);
		var document = XElement.Parse("<article>" + html + "</article>");
		var containers = document.Elements("div").ToArray();

		await Assert.That(containers.Length).IsEqualTo(2);
		await Assert.That(containers.All(c => c.Elements("table").Count() == 1)).IsTrue();
		await Assert.That(containers.SelectMany(c => c.Descendants("td")).Select(c => c.Value).ToArray())
			.IsEquivalentTo(["1", "15", "1", "15"]);
		await Assert.That(document.Elements("p").Single().Value).IsEqualTo("Between.");
	}
}
