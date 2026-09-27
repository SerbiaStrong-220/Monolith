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
