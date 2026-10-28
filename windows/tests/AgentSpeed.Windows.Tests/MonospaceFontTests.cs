using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AgentSpeed.Core.Reporting;
using AgentSpeed.Reporting;
using AgentSpeed.Theme;

namespace AgentSpeed.Windows.Tests;

/// <summary>
/// The consent sheet's request preview, the device fingerprint and the saved API key are set in <see cref="AppFonts.Monospace"/>.
/// DirectWrite lists the Cascadia fonts that Windows Terminal from the Store shares with the system even where opening them fails
/// with an access error, and WPF then throws UnauthorizedAccessException when it lays text out in them instead of using the next
/// family of the list: the consent sheet never opened and live reporting could not be turned on. Text must lay out in whichever
/// fonts the computer has. A test host has no theme, so a text box or rich text box here has no template and lays nothing out;
/// these tests format a document and a text block, which need none.
/// </summary>
public class MonospaceFontTests
{
    /// <summary>The preview is a FlowDocument paragraph, formatted by the paragraph engine; paginating the document formats it.</summary>
    [Fact]
    public void ThePreviewDocumentFormatsInTheMonospaceFace()
    {
        var pageCount = OnStaThread(() =>
        {
            var box = ReportPreviewText.Create("preview");
            ReportPreviewText.Update(box,
            [
                new ReportJsonToken("{", ReportJsonTokenKind.Punctuation),
                new ReportJsonToken("\n  \"tokens\"", ReportJsonTokenKind.Key),
                new ReportJsonToken(": ", ReportJsonTokenKind.Punctuation),
                new ReportJsonToken("1420", ReportJsonTokenKind.Number),
                new ReportJsonToken("\n}", ReportJsonTokenKind.Punctuation),
            ]);
            var paginator = ((IDocumentPaginatorSource)box.Document).DocumentPaginator;
            paginator.PageSize = new Size(480, 240);
            paginator.ComputePageCount();
            using var page = paginator.GetPage(0);
            return paginator.PageCount;
        });
        Assert.True(pageCount > 0);
    }

    /// <summary>The fingerprint box and the key text are laid out by the simple text engine, as a text block is.</summary>
    [Fact]
    public void TextLaysOutInTheMonospaceFaceWithEveryCharacterAsWideAsTheOthers()
    {
        var (narrow, wide) = OnStaThread(() => (Width("iiiiiiii"), Width("WWWWWWWW")));
        Assert.True(narrow > 0);
        Assert.Equal(narrow, wide, 0.01);
    }

    private static double Width(string text)
    {
        var block = new TextBlock { Text = text, FontFamily = AppFonts.Monospace, FontSize = 12 };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result : throw new InvalidOperationException("Layout failed.", error);
    }
}
