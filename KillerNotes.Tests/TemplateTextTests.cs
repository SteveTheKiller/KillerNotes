using System;
using System.Linq;
using System.Windows.Documents;
using KillerNotes.Services;
using Xunit;

namespace KillerNotes.Tests
{
    /// <summary>Template placeholders (1.3.1): what gets filled in, and what is left alone.</summary>
    public sealed class TemplateTextTests
    {
        private static readonly DateTime When = new(2026, 9, 1, 14, 5, 0);   // a Tuesday

        [Fact]
        public void FillsEveryTokenFromTheGivenMoment()
        {
            Assert.Equal("2026-09-01", TemplateText.Expand("{date}", When));
            Assert.Equal("14:05", TemplateText.Expand("{time}", When));
            Assert.Equal("2026-09-01 14:05", TemplateText.Expand("{datetime}", When));
            Assert.Equal("2026", TemplateText.Expand("{year}", When));
            Assert.Equal("09", TemplateText.Expand("{month}", When));
            Assert.Equal("01", TemplateText.Expand("{day}", When));
            Assert.Equal(
                System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(DayOfWeek.Tuesday),
                TemplateText.Expand("{weekday}", When));
        }

        [Fact]
        public void TokensAreCaseInsensitiveAndRepeatable()
        {
            Assert.Equal("Standup 2026-09-01 (2026-09-01)", TemplateText.Expand("Standup {DATE} ({Date})", When));
        }

        [Fact]
        public void UnknownBracesAreLeftExactlyAsTyped()
        {
            const string code = "if (x) { return {value}; } {DATEX}";
            Assert.Equal(code, TemplateText.Expand(code, When));
            Assert.False(TemplateText.HasPlaceholders(code));
            Assert.True(TemplateText.HasPlaceholders("Meeting {date}"));
        }

        [Fact]
        public void DocumentRunsAreFilledInsideListsTablesAndSpans() => Sta.Run(() =>
        {
            var doc = new FlowDocument();
            var p = new Paragraph();
            p.Inlines.Add(new Run("Report for "));
            p.Inlines.Add(new Bold(new Run("{date}")));
            doc.Blocks.Add(p);
            var list = new List();
            list.ListItems.Add(new ListItem(new Paragraph(new Run("at {time}"))));
            doc.Blocks.Add(list);
            var table = new Table();
            var group = new TableRowGroup();
            var row = new TableRow();
            row.Cells.Add(new TableCell(new Paragraph(new Run("{year}-{month}"))));
            group.Rows.Add(row);
            table.RowGroups.Add(group);
            doc.Blocks.Add(table);

            TemplateText.ExpandDocument(doc, When);

            string all = new TextRange(doc.ContentStart, doc.ContentEnd).Text;
            Assert.Contains("Report for 2026-09-01", all);
            Assert.Contains("at 14:05", all);
            Assert.Contains("2026-09", all);
            Assert.DoesNotContain("{", all);
            // The bold span survives: only the run's text changed.
            Assert.Single(p.Inlines.OfType<Bold>());
        });
    }
}
