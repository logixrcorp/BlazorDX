using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml;

namespace BlazorDX.Documents;

/// <summary>Package-parts readers for <see cref="DocxReader"/> (rels, image media,
/// heading styles) — split out so the main file stays under the line cap.</summary>
public static partial class DocxReader
{
    private static readonly IReadOnlyDictionary<string, int> EmptyHeadingStyles =
        new Dictionary<string, int>(0);

    private const string PackageRelationshipsNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    private static readonly IReadOnlyDictionary<string, string> EmptyLinkRels =
        new Dictionary<string, string>(0);

    private static readonly IReadOnlyDictionary<string, ImagePart> EmptyImageParts =
        new Dictionary<string, ImagePart>(0);

    // Reads each image relationship's media part into memory, keyed by rId, so a
    // <w:drawing> blip can resolve to bytes. The same MaxPartBytes cap guards each part.
    private static IReadOnlyDictionary<string, ImagePart> ReadImageParts(ZipArchive zip)
    {
        ZipArchiveEntry? relsEntry = zip.GetEntry("word/_rels/document.xml.rels");
        if (relsEntry is null)
        {
            return EmptyImageParts;
        }

        // rId -> media target (e.g. "media/image1.png").
        Dictionary<string, string>? targets = null;
        using (Stream content = OpenChecked(relsEntry))
        using (XmlReader reader = XmlReader.Create(content, ReaderSettings))
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element
                    || reader.LocalName != "Relationship"
                    || reader.NamespaceURI != PackageRelationshipsNs)
                {
                    continue;
                }

                string? type = reader.GetAttribute("Type");
                if (type is null || !type.EndsWith("/image", StringComparison.Ordinal))
                {
                    continue;
                }

                string? id = reader.GetAttribute("Id");
                string? target = reader.GetAttribute("Target");
                if (id is not null && target is not null)
                {
                    (targets ??= new Dictionary<string, string>(StringComparer.Ordinal))[id] = target;
                }
            }
        }

        if (targets is null)
        {
            return EmptyImageParts;
        }

        Dictionary<string, ImagePart> map = new(StringComparer.Ordinal);
        foreach ((string id, string target) in targets)
        {
            ZipArchiveEntry? media = zip.GetEntry("word/" + target.TrimStart('/'));
            if (media is null)
            {
                continue;
            }

            using Stream ms = OpenChecked(media);
            using MemoryStream buffer = new();
            ms.CopyTo(buffer);
            byte[] data = buffer.ToArray();
            if (data.Length > 0)
            {
                map[id] = new ImagePart(data, ContentTypeForFile(target));
            }
        }

        return map.Count == 0 ? EmptyImageParts : map;
    }

    private static string ContentTypeForFile(string fileName)
    {
        int dot = fileName.LastIndexOf('.');
        string ext = dot >= 0 ? fileName[(dot + 1)..].ToLowerInvariant() : string.Empty;
        return ext switch
        {
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            _ => "image/png",
        };
    }

    // word/_rels/document.xml.rels: builds rId -> external target for hyperlink
    // relationships, so <w:hyperlink r:id> runs can be tagged with their URL.
    private static IReadOnlyDictionary<string, string> ReadHyperlinkRels(ZipArchive zip)
    {
        ZipArchiveEntry? entry = zip.GetEntry("word/_rels/document.xml.rels");
        if (entry is null)
        {
            return EmptyLinkRels;
        }

        Dictionary<string, string>? map = null;
        using Stream content = OpenChecked(entry);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "Relationship"
                || reader.NamespaceURI != PackageRelationshipsNs)
            {
                continue;
            }

            string? type = reader.GetAttribute("Type");
            if (type is null || !type.EndsWith("/hyperlink", StringComparison.Ordinal))
            {
                continue;
            }

            string? id = reader.GetAttribute("Id");
            string? target = reader.GetAttribute("Target");
            if (id is not null && target is not null)
            {
                (map ??= new Dictionary<string, string>(StringComparer.Ordinal))[id] = target;
            }
        }

        return map ?? EmptyLinkRels;
    }

    // word/styles.xml: builds styleId -> heading level for paragraph styles whose name
    // is "heading N" / "Title" or that declare an <w:outlineLvl w:val="0..5">. This
    // resolves localized or custom heading style ids the conventional name check misses.
    private static IReadOnlyDictionary<string, int> ReadHeadingStyles(ZipArchive zip)
    {
        ZipArchiveEntry? entry = zip.GetEntry("word/styles.xml");
        if (entry is null)
        {
            return EmptyHeadingStyles;
        }

        Dictionary<string, int> map = [];
        using Stream content = OpenChecked(entry);
        using XmlReader reader = XmlReader.Create(content, ReaderSettings);

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element
                || reader.LocalName != "style"
                || reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            string? type = reader.GetAttribute("type", WordprocessingMl);
            if (type is not null && type != "paragraph")
            {
                SkipElement(reader, "style");
                continue;
            }

            string? styleId = reader.GetAttribute("styleId", WordprocessingMl);
            int? level = ReadStyleHeadingLevel(reader);
            if (styleId is not null && level is { } lvl)
            {
                map[styleId] = lvl;
            }
        }

        return map.Count == 0 ? EmptyHeadingStyles : map;
    }

    // Reads one <w:style> body, returning a heading level inferred from its <w:name>
    // ("heading 1".."heading 6" / "Title") or <w:outlineLvl> (0-based → level 1-6).
    private static int? ReadStyleHeadingLevel(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return null;
        }

        int styleDepth = reader.Depth;
        int? level = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == styleDepth
                && reader.LocalName == "style")
            {
                break;
            }

            if (reader.NodeType != XmlNodeType.Element || reader.NamespaceURI != WordprocessingMl)
            {
                continue;
            }

            switch (reader.LocalName)
            {
                case "name":
                    string? name = reader.GetAttribute("val", WordprocessingMl);
                    level ??= HeadingLevelFromName(name);
                    break;
                case "outlineLvl":
                    if (int.TryParse(reader.GetAttribute("val", WordprocessingMl),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int outline)
                        && outline is >= 0 and <= 5)
                    {
                        // outlineLvl is authoritative for the level when present.
                        level = outline + 1;
                    }

                    break;
            }
        }

        return level;
    }

    private static int? HeadingLevelFromName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (name.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        const string prefix = "heading ";
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(name.AsSpan(prefix.Length).Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int level)
            && level is >= 1 and <= 6)
        {
            return level;
        }

        return null;
    }

    // Merges adjacent runs that share the same formatting so the model is compact
    // (Word splits runs on proofing/rsid boundaries we don't care about).
    private static IReadOnlyList<WordRun> CoalesceRuns(List<WordRun> runs)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        List<WordRun> merged = new(runs.Count);
        WordRun current = runs[0];
        for (int i = 1; i < runs.Count; i++)
        {
            WordRun next = runs[i];
            if (next.Bold == current.Bold && next.Italic == current.Italic
                && next.Underline == current.Underline && next.Strike == current.Strike
                && next.Href == current.Href
                && next.Color == current.Color && next.Highlight == current.Highlight
                && next.FontFamily == current.FontFamily && next.FontSizePoints == current.FontSizePoints
                && next.VerticalAlign == current.VerticalAlign)
            {
                current = current with { Text = current.Text + next.Text };
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    // Skips to the matching end element of the named container the reader is currently on.
    private static void SkipElement(XmlReader reader, string name)
    {
        if (reader.IsEmptyElement)
        {
            return;
        }

        int depth = reader.Depth;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == depth
                && reader.LocalName == name)
            {
                break;
            }
        }
    }

    // Reads the concatenated text content of the current element and leaves the reader
    // on its EndElement (or on the element itself if it is empty).
    private static string ReadElementText(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            return string.Empty;
        }

        string elementName = reader.LocalName;
        int elementDepth = reader.Depth;
        string? single = null;
        System.Text.StringBuilder? many = null;

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.EndElement
                && reader.Depth == elementDepth
                && reader.LocalName == elementName)
            {
                break;
            }

            if (reader.NodeType is XmlNodeType.Text
                or XmlNodeType.CDATA
                or XmlNodeType.SignificantWhitespace
                or XmlNodeType.Whitespace)
            {
                if (many is not null)
                {
                    many.Append(reader.Value);
                }
                else if (single is null)
                {
                    single = reader.Value;
                }
                else
                {
                    many = new System.Text.StringBuilder(single);
                    many.Append(reader.Value);
                }
            }
        }

        return many?.ToString() ?? single ?? string.Empty;
    }
}
