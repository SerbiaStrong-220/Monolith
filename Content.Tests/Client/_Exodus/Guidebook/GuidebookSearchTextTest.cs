using Content.Client._Exodus.Guidebook;
using NUnit.Framework;

namespace Content.Tests.Client._Exodus.Guidebook;

[TestFixture]
[TestOf(typeof(GuidebookSearchText))]
public sealed class GuidebookSearchTextTest
{
    [Test]
    public void ExtractsVisibleTextWithoutCommentsOrPrototypeIds()
    {
        const string document = """
            <Document>
            # Ремонт

            Используйте [bold]дронов[/bold] и [textlink="станцию" link="HiddenGuideId"].
            <!-- Скрытый комментарий <GuideEntityEmbed Caption="Невидимая подпись"/> -->
            <Box>
              <GuideEntityEmbed Entity="HiddenEntityId" Caption="Ремонтный дрон"/>
            </Box>
            - Первый пункт
            -- Второй пункт
            </Document>
            """;

        Assert.That(GuidebookSearchText.Extract(document),
            Is.EqualTo("Ремонт Используйте дронов и станцию. Ремонтный дрон Первый пункт Второй пункт"));
    }

    [Test]
    public void PreservesWordsAcrossInlineFormattingAndSeparatesBlocks()
    {
        const string document = "<Document>ре[bold]монт[/bold]<Box>дронов</Box></Document>";
        Assert.That(GuidebookSearchText.Extract(document), Is.EqualTo("ремонт дронов"));
    }

    [Test]
    public void HandlesQuotedAngleBracketsAndEscapedDocumentCharacters()
    {
        const string document = """
            <Document>
            <GuideEntityEmbed Entity="HiddenId" Caption="Порог > 5"/>
            Давление \< 10\nСледующая строка
            </Document>
            """;

        Assert.That(GuidebookSearchText.Extract(document),
            Is.EqualTo("Порог > 5 Давление < 10 Следующая строка"));
    }

    [Test]
    public void FindsLocalizedReagentsInIndividualCardsAndGroups()
    {
        const string document = """
            <Document>
            # Лекарства
            <GuideReagentEmbed Reagent="Bicaridine"/>
            <GuideReagentGroupEmbed
                Group="Medicine"/>
            </Document>
            """;

        var text = GuidebookSearchText.Extract(document,
            id => id == "Bicaridine" ? "Бикаридин" : null,
            group => group == "Medicine" ? "Дермалин\nДексалин плюс" : null);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo("Лекарства Бикаридин Дермалин Дексалин плюс"));
            Assert.That(GuidebookSearchText.GetMatchRank("лекарства", GuidebookSearchText.Normalize(text), "дексалин"),
                Is.EqualTo(2));
            Assert.That(GuidebookSearchText.GetSnippet(text, "дексалин"), Does.Contain("Дексалин плюс"));
        });
    }

    [Test]
    public void DoesNotResolveReagentsInCommentsEscapedTagsOrUnrelatedAttributes()
    {
        const string document = """
            <Document>
            <!-- <GuideReagentEmbed Reagent="Hidden"/> -->
            <!-- <GuideReagentGroupEmbed Group="Hidden"/> -->
            \<GuideReagentEmbed Reagent="Literal"/>
            <GuideEntityEmbed Reagent="Unrelated" Group="Unrelated"/>
            <GuideEntityEmbed Caption="Пример Reagent=\"Quoted\""/>
            </Document>
            """;
        var resolved = 0;

        GuidebookSearchText.Extract(document,
            _ =>
            {
                resolved++;
                return "Скрытый препарат";
            },
            _ =>
            {
                resolved++;
                return "Скрытая группа";
            });

        Assert.That(resolved, Is.Zero);
    }

    [Test]
    public void PreservesCaptionsAndLiteralMarkupInNamesWhileSkippingUnknownReagents()
    {
        const string document = """
            <Document>
            <GuideReagentEmbed Caption="Антидот" Reagent="Known"></GuideReagentEmbed>
            <GuideReagentEmbed Reagent="Missing"/>
            <GuideReagentGroupEmbed Group="Missing"/>
            </Document>
            """;

        var text = GuidebookSearchText.Extract(document,
            id => id == "Known" ? "Препарат [bold]X[/bold]" : null,
            _ => null);

        Assert.That(text, Is.EqualTo("Антидот Препарат [bold]X[/bold]"));
    }

    [Test]
    public void IndexesResolvedMutationTableTextWithoutExposingPrototypeIds()
    {
        const string document = """
            <Document>
            <!-- [geneticmutation="Hidden"] -->
            <Table Columns="2">
              <Box>[bold][geneticmutation="GeneticRegeneration"][/bold]</Box>
              <Box>[geneticmutation="GeneticRegeneration" field="instability"]</Box>
            </Table>
            </Document>
            """;

        var text = GuidebookSearchText.Extract(document, markupText: node =>
        {
            if (node.Name != "geneticmutation")
                return null;

            Assert.That(node.Value.StringValue, Is.EqualTo("GeneticRegeneration"));
            return node.Attributes.ContainsKey("field") ? "30" : "Регенерация";
        });

        Assert.That(text, Is.EqualTo("Регенерация 30"));
        Assert.That(GuidebookSearchText.GetMatchRank("мутации", GuidebookSearchText.Normalize(text), "регенерация"),
            Is.EqualTo(2));
    }

    [TestCase("  РЕМОНТ\tДРОНОВ\r\n", "ремонт дронов")]
    [TestCase("ЖЁЛТЫЙ", "желтый")]
    [TestCase("\t\n ", "")]
    public void NormalizesCaseWhitespaceAndRussianYo(string text, string expected)
    {
        Assert.That(GuidebookSearchText.Normalize(text), Is.EqualTo(expected));
    }

    [TestCase("ремонт", "", "ремонт", 0)]
    [TestCase("ремонтные дроны", "", "ремонт", 1)]
    [TestCase("станция", "ремонтные дроны", "ремонт", 2)]
    [TestCase("дроны", "", "д", 1)]
    [TestCase("станция", "дроны", "неизвестное", -1)]
    [TestCase("станция", "дроны", "", -1)]
    [TestCase("станция", "[literal]", "[literal]", 2)]
    public void RanksTitlesBeforeBodyAndAcceptsPartialWords(string title, string text, string query, int expected)
    {
        Assert.That(GuidebookSearchText.GetMatchRank(title, text, query), Is.EqualTo(expected));
    }

    [Test]
    public void SnippetShowsAMatchNearTheEndOfALongArticle()
    {
        var text = string.Concat(System.Linq.Enumerable.Repeat("Начало статьи. ", 40)) + "Ремонтный дрон работает.";
        var snippet = GuidebookSearchText.GetSnippet(text, "ремонтный дрон");

        Assert.Multiple(() =>
        {
            Assert.That(snippet, Does.StartWith("…"));
            Assert.That(snippet, Does.Contain("Ремонтный дрон"));
            Assert.That(snippet.Length, Is.LessThan(text.Length));
        });
    }

    [Test]
    public void SnippetForTitleOnlyMatchUsesTheArticleBeginning()
    {
        var text = string.Concat(System.Linq.Enumerable.Repeat("Начало статьи. ", 40));
        var snippet = GuidebookSearchText.GetSnippet(text, "нет в тексте");

        Assert.That(snippet, Does.StartWith("Начало статьи.").And.EndWith("…"));
    }
}
