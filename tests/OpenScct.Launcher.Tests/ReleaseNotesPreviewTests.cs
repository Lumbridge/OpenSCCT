using System.Globalization;
using System.Linq;
using OpenScct.Launcher.GitHub;
using Xunit;

namespace OpenScct.Launcher.Tests;

public sealed class ReleaseNotesPreviewTests
{
    [Fact]
    public void UsesTheFirstThreeTopLevelChangesAcrossReleaseSections()
    {
        const string notes = """
            ## Gameplay fixes

            - Fixed movement-action queues.
            - Restored crouched movement after a rejected stand-up.
            - Fixed focus-loss input cleanup.
            - Expanded diagnostics.

            ## Launcher 0.8.36

            - Simplified setup.

            Verified with a local client and server.
            """;

        Assert.Equal(new[] { "Fixed movement-action queues.", "Restored crouched movement after a rejected stand-up.",
            "Fixed focus-loss input cleanup." }, ReleaseNotesPreview.Create(notes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t")]
    public void RecognizesCommonIndentationAndJoinsWrappedLines(string indent)
    {
        string notes = $"{indent}- Fixed the launcher\r\n{indent}  on smaller screens.\r\n{indent}  - Nested detail.\r\n{indent}    More nested detail.\r\n{indent}* Improved updates.\r\n{indent}+ Preserved saves.";

        Assert.Equal(new[] { "Fixed the launcher on smaller screens.", "Improved updates.", "Preserved saves." },
            ReleaseNotesPreview.Create(notes));
    }

    [Fact]
    public void IgnoresBothKindsOfFencedCodeAndTheirApparentListItems()
    {
        const string notes = """
            ```markdown
            - Not a change.
            ```
            - Actual change.
            ~~~~text
            - Another example.
            ~~~
            - Still code because the fence is too short.
            ~~~~
            - Second change.
            """;

        Assert.Equal(new[] { "Actual change.", "Second change." }, ReleaseNotesPreview.Create(notes));
    }

    [Fact]
    public void ExcludesUnorderedChildrenOfOrderedParentsWhenSelectingTopLevelChanges()
    {
        const string notes = """
            1. Installation example.
               - Nested detail.
                 More nested detail.

            - Actual change.
            2. Another numbered example.
               - Another nested detail.
            - Second actual change.
            """;

        Assert.Equal(new[] { "Actual change.", "Second actual change." }, ReleaseNotesPreview.Create(notes));
    }

    [Fact]
    public void AnOrderedListAndItsNestedDetailsDoNotBecomePreviewOrProseFallback()
    {
        const string notes = """
            1. Installation example.
               - Nested detail.
                 Wrapped nested detail.

                 Another nested paragraph.
            2. Another numbered example.
               Wrapped numbered detail.
            """;

        Assert.Empty(ReleaseNotesPreview.Create(notes));
    }

    [Fact]
    public void ProseAfterAnOrderedListRemainsAvailableAsFallback()
    {
        const string notes = """
            1. Installation example.
               - Nested detail.

            Actual release information.
            Wrapped release information.
            """;

        Assert.Equal(new[] { "Actual release information. Wrapped release information." }, ReleaseNotesPreview.Create(notes));
    }

    [Fact]
    public void FlattensLinksAndInlineFormattingWithoutLosingTheirReadableText()
    {
        const string notes = """
            - **Improved** *updates*, __saved__ _settings_, ~~old behavior~~ and `user://`.
            - Read [the guide](https://example.invalid/docs_(launcher)) &amp; [the FAQ][faq].
            - Keep snake_case and escaped \*literal stars\*.

            [faq]: https://example.invalid/faq
            """;

        Assert.Equal(new[] { "Improved updates, saved settings, old behavior and user://.", "Read the guide & the FAQ.",
            "Keep snake_case and escaped *literal stars*." }, ReleaseNotesPreview.Create(notes));
    }

    [Fact]
    public void FallsBackToTheFirstProseParagraphWhenThereAreNoChanges()
    {
        const string notes = """
            ## OpenSCCT update

            **A small update** for the launcher,
            with more reliable downloads.

            Built from commit `abcdef0`.
            """;

        Assert.Equal(new[] { "A small update for the launcher, with more reliable downloads." }, ReleaseNotesPreview.Create(notes));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t")]
    [InlineData("# Release\n\n---\n\n```text\n- Example only\n```\n\n***")]
    [InlineData("- \n\n** **")]
    public void EmptyOrStructuralNotesHaveNoPreview(string? notes) => Assert.Empty(ReleaseNotesPreview.Create(notes));

    [Fact]
    public void TruncatesAtAWordBoundaryAndIncludesTheEllipsisInTheLimit()
    {
        string notes = "- " + string.Join(' ', Enumerable.Repeat("download", 30));

        string preview = Assert.Single(ReleaseNotesPreview.Create(notes));

        Assert.Equal(string.Join(' ', Enumerable.Repeat("download", 17)) + "…", preview);
        Assert.True(StringInfo.ParseCombiningCharacters(preview).Length <= 160);
    }

    [Fact]
    public void KeepsCompleteUnicodeTextElementsWhenThereIsNoWordBoundary()
    {
        const string element = "👩‍💻e\u0301";
        string text = string.Concat(Enumerable.Repeat(element, 100));

        string preview = Assert.Single(ReleaseNotesPreview.Create("- " + text));

        Assert.Equal(string.Concat(Enumerable.Repeat(element, 79)) + "👩‍💻…", preview);
        Assert.Equal(160, StringInfo.ParseCombiningCharacters(preview).Length);
    }

    [Fact]
    public void LeavesTextAtTheLimitIntact()
    {
        string text = new('a', 160);

        Assert.Equal(new[] { text }, ReleaseNotesPreview.Create("- " + text));
    }
}
