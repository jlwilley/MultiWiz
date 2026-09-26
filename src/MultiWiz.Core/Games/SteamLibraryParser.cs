using System.Globalization;
using System.Text;

namespace MultiWiz.Core.Games;

/// <summary>
/// Reads Steam's KeyValues text files (<c>steamapps\libraryfolders.vdf</c> and <c>steamapps\appmanifest_&lt;id&gt;.acf</c>).
/// Pure and tolerant: malformed input yields empty results instead of exceptions.
/// </summary>
public static class SteamLibraryParser
{
    /// <summary>
    /// Library root folders listed in <c>libraryfolders.vdf</c>, in file order, without duplicates. Supports the
    /// modern form (<c>"0" { "path" "C:\\Steam" ... }</c>) and the legacy form (<c>"1" "D:\\SteamLibrary"</c>).
    /// Legacy files do not list the Steam install folder itself, which is always a library too.
    /// </summary>
    public static IReadOnlyList<string> ParseLibraryFolders(string vdfText)
    {
        var root = FirstBlock(Parse(vdfText));
        if (root is null)
        {
            return [];
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in root)
        {
            if (!int.TryParse(entry.Key, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            {
                continue;
            }

            var path = (entry.Children is { } children ? GetValue(children, "path") : entry.Value)?.Trim();
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (seen.Add(path.TrimEnd('\\', '/')))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>The app id and install folder name from an <c>appmanifest_&lt;id&gt;.acf</c> file, or null if either is missing.</summary>
    public static (string AppId, string InstallDir)? ParseAppManifest(string acfText)
    {
        var root = FirstBlock(Parse(acfText));
        if (root is null)
        {
            return null;
        }

        var appId = GetValue(root, "appid")?.Trim();
        var installDir = GetValue(root, "installdir")?.Trim();
        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(installDir))
        {
            return null;
        }

        return (appId, installDir);
    }

    private static List<Entry>? FirstBlock(List<Entry> entries) =>
        entries.FirstOrDefault(entry => entry.Children is not null)?.Children;

    private static string? GetValue(List<Entry> entries, string key) =>
        entries.FirstOrDefault(entry => entry.Children is null && string.Equals(entry.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;

    private static List<Entry> Parse(string? text)
    {
        var tokens = Tokenize(text ?? string.Empty);
        var position = 0;
        return ParseEntries(tokens, ref position, nested: false);
    }

    private static List<Entry> ParseEntries(List<Token> tokens, ref int position, bool nested)
    {
        var entries = new List<Entry>();
        while (position < tokens.Count)
        {
            var token = tokens[position++];
            switch (token.Kind)
            {
                case TokenKind.Close when nested:
                    return entries;
                case TokenKind.Close:
                    // A stray closing brace at the top level: ignore it.
                    continue;
                case TokenKind.Open:
                    // A block without a key: parse it to stay in sync, then drop it.
                    ParseEntries(tokens, ref position, nested: true);
                    continue;
            }

            if (position >= tokens.Count)
            {
                break;
            }

            var next = tokens[position];
            if (next.Kind == TokenKind.Open)
            {
                position++;
                entries.Add(new Entry(token.Text, null, ParseEntries(tokens, ref position, nested: true)));
            }
            else if (next.Kind == TokenKind.Text)
            {
                position++;
                entries.Add(new Entry(token.Text, next.Text, null));
            }

            // Otherwise the key has no value (the next token closes the block); the loop handles that token.
        }

        return entries;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '{')
            {
                tokens.Add(new Token(TokenKind.Open, "{"));
                i++;
                continue;
            }

            if (c == '}')
            {
                tokens.Add(new Token(TokenKind.Close, "}"));
                i++;
                continue;
            }

            if (c == '"')
            {
                i++;
                var builder = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    // Steam escapes backslashes and quotes. Any other backslash is kept as-is so that
                    // hand-edited paths such as "D:\Games" survive.
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] is '\\' or '"')
                    {
                        builder.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }

                    builder.Append(text[i]);
                    i++;
                }

                i++; // Closing quote (or end of text for an unterminated string).
                tokens.Add(new Token(TokenKind.Text, builder.ToString()));
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('"' or '{' or '}'))
            {
                i++;
            }

            var word = text[start..i];

            // Conditional tags such as [$WIN32] only qualify the preceding pair; they are not keys or values.
            if (!word.StartsWith('['))
            {
                tokens.Add(new Token(TokenKind.Text, word));
            }
        }

        return tokens;
    }

    private enum TokenKind
    {
        Text,
        Open,
        Close,
    }

    private readonly record struct Token(TokenKind Kind, string Text);

    private sealed record Entry(string Key, string? Value, List<Entry>? Children);
}
