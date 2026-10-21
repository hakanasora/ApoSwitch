using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ApoSwitch.Core;

public sealed record Preset(string Id, string Name, string IncludePath, bool IsEnabled);

/// <summary>Only parses lines in the supplied root file. Never opens an included file.</summary>
public sealed class ConfigDocument
{
    private static readonly Regex Include = new(
        @"^(?<indent>[ \t]*)(?<comment>#[ \t]*)?(?<command>Include[ \t]*:[ \t]*)(?<path>.*?)(?<tail>[ \t]*)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private sealed record Line(string Text, string Ending, string? Id, int Indent, int CommentLength);
    private readonly List<Line> lines = [];
    private readonly Encoding encoding;
    private readonly byte[] preamble;
    public string FilePath { get; }
    public byte[] OriginalBytes { get; }
    public IReadOnlyList<Preset> Presets { get; }

    private ConfigDocument(string path, byte[] bytes)
    {
        FilePath = Path.GetFullPath(path);
        OriginalBytes = bytes;
        (encoding, preamble) = DetectEncoding(bytes);
        var text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
        var entries = new List<Preset>();
        // Splitting the root text is the entire discovery operation: no recursive includes or directory scan.
        foreach (Match split in Regex.Matches(text, @"([^\r\n]*)(\r\n|\n|\r|$)"))
        {
            if (split.Length == 0) continue;
            var body = split.Groups[1].Value;
            var ending = split.Groups[2].Value;
            var match = Include.Match(body);
            string? id = null;
            if (match.Success && !string.IsNullOrWhiteSpace(match.Groups["path"].Value))
            {
                var includePath = match.Groups["path"].Value;
                // APO normally uses unquoted paths, including paths containing spaces.
                var target = includePath.Length > 1 && includePath[0] == '"' && includePath[^1] == '"'
                    ? includePath[1..^1] : includePath;
                id = Path.GetFullPath(target, Path.GetDirectoryName(FilePath)!);
                var previous = entries.FindIndex(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, id));
                bool enabled = !match.Groups["comment"].Success;
                if (previous < 0)
                    entries.Add(new(id, Path.GetFileNameWithoutExtension(target), includePath, enabled));
                else if (enabled)
                    entries[previous] = entries[previous] with { IsEnabled = true };
            }
            lines.Add(new(body, ending, id, match.Groups["indent"].Length, match.Groups["comment"].Length));
        }
        Presets = entries;
    }

    public static ConfigDocument Load(string path) => new(path, File.ReadAllBytes(path));

    public byte[] Activate(string id)
    {
        if (!Presets.Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Id, id)))
            throw new IOException("该配置已从 config.txt 移除，请重新选择。");
        var builder = new StringBuilder();
        bool selectedWritten = false;
        foreach (var line in lines)
        {
            string body = line.Text;
            if (line.Id is not null)
            {
                bool activate = !selectedWritten && StringComparer.OrdinalIgnoreCase.Equals(line.Id, id);
                if (activate)
                {
                    selectedWritten = true;
                    if (line.CommentLength > 0) body = body.Remove(line.Indent, line.CommentLength);
                }
                else if (line.CommentLength == 0) body = body.Insert(line.Indent, "# ");
            }
            builder.Append(body).Append(line.Ending);
        }
        return [.. preamble, .. encoding.GetBytes(builder.ToString())];
    }

    private static (Encoding, byte[]) DetectEncoding(byte[] bytes)
    {
        Encoding[] encodings = [new UTF32Encoding(false, true, true), new UTF32Encoding(true, true, true),
            new UTF8Encoding(true, true), new UnicodeEncoding(false, true, true), new UnicodeEncoding(true, true, true)];
        foreach (var candidate in encodings)
        {
            var prefix = candidate.GetPreamble();
            if (bytes.AsSpan().StartsWith(prefix)) return (candidate, prefix);
        }
        var utf8 = new UTF8Encoding(false, true);
        try { utf8.GetString(bytes); return (utf8, []); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return (Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), []);
        }
    }
}
