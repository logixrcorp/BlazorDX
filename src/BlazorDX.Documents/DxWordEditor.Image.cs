namespace BlazorDX.Documents;

/// <summary>
/// Image insertion for <see cref="DxWordEditor"/>: a toolbar action picks an image file (via the
/// editor's bridge), turns it into a <see cref="WordImage"/> block, and inserts it into the
/// document model after the caret's block — so it round-trips to <c>.docx</c> like any other
/// block. The model is the source of truth; the surface is re-seeded through the shared commit.
/// </summary>
public sealed partial class DxWordEditor
{
    // The image types the editor accepts (and DocxWriter knows how to embed).
    private static bool IsAllowedImageType(string mime) =>
        mime is "image/png" or "image/jpeg" or "image/gif" or "image/webp";

    private async Task InsertImageAsync()
    {
        if (_rte is null)
        {
            return;
        }

        // The bridge returns "mimeType|base64" for the chosen file, or "" on cancel.
        string picked = await _rte.PickImageAsync();
        int sep = picked.IndexOf('|');
        if (sep <= 0)
        {
            return;
        }

        string mime = picked[..sep];
        if (!IsAllowedImageType(mime))
        {
            return;
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(picked[(sep + 1)..]);
        }
        catch (FormatException)
        {
            return;
        }

        if (data.Length == 0)
        {
            return;
        }

        // Insert after the block the caret sits in (append when there is no addressable caret).
        int container = TryParseRange(await _rte.GetSelectionRangeAsync(), out int c, out _, out _) ? c : -1;
        var image = new WordImage(data, mime, AltText: "Inserted image");
        await CommitModelEditAsync(InsertBlockAfter(Current, container, image));
    }

    // Returns a copy of the document with newBlock inserted after the block owning the given
    // run-container (containers numbered in document order, matching the rest of the editor).
    // An unaddressable container appends at the end; an empty document just holds the new block.
    private static WordDocument InsertBlockAfter(WordDocument document, int containerIndex, WordBlock newBlock)
    {
        if (document.Blocks.Count == 0)
        {
            return new WordDocument([newBlock]);
        }

        int insertAfter = document.Blocks.Count - 1; // default: append
        int seen = -1;
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            int containers = ContainerCount(document.Blocks[i]);
            if (containerIndex >= seen + 1 && containerIndex <= seen + containers)
            {
                insertAfter = i;
                break;
            }

            seen += containers;
        }

        var blocks = new List<WordBlock>(document.Blocks.Count + 1);
        for (int i = 0; i < document.Blocks.Count; i++)
        {
            blocks.Add(document.Blocks[i]);
            if (i == insertAfter)
            {
                blocks.Add(newBlock);
            }
        }

        return new WordDocument(blocks);
    }

    // Run-containers a block contributes, consistent with the selection addressing elsewhere.
    private static int ContainerCount(WordBlock block) => block switch
    {
        WordHeading => 1,
        WordParagraph => 1,
        WordList list => list.Items.Count,
        WordTable table => table.Rows.Sum(r => r.Cells.Count),
        _ => 0, // image / container-less
    };
}
