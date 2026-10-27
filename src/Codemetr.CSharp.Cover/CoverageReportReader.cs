using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Codemetr.CSharp.Cover;

/// <summary>Reads Cobertura and OpenCover files, as written by Coverlet, into <see cref="CoverageData"/> with report paths.</summary>
public static partial class CoverageReportReader
{
    /// <summary>Report formats, detected by the root element.</summary>
    public const string Cobertura = "cobertura", OpenCover = "opencover";

    /// <summary>Reads one report file. Source paths stay as the report wrote them (absolute where possible).</summary>
    public static (CoverageData Data, string Format) Read(string path)
    {
        XDocument document;
        try
        {
            using var stream = File.OpenRead(path);
            document = XDocument.Load(XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }));
        }
        catch (XmlException e) { throw new InvalidDataException($"Coverage report is not valid XML: {path}: {e.Message}"); }
        return document.Root?.Name.LocalName switch
        {
            "coverage" => (ReadCobertura(document.Root), Cobertura),
            "CoverageSession" => (ReadOpenCover(document.Root), OpenCover),
            _ => throw new InvalidDataException($"Unknown coverage report format (expected Cobertura or OpenCover): {path}"),
        };
    }

    static CoverageData ReadCobertura(XElement root)
    {
        var sources = root.Element("sources")?.Elements("source").Select(s => s.Value.Trim()).Where(s => s.Length > 0).ToArray() ?? [];
        var data = new CoverageData();
        foreach (var type in root.Descendants("class"))
        {
            var file = ResolveCoberturaPath((string?)type.Attribute("filename") ?? "", sources);
            if (file.Length == 0) continue;
            // Class-level lines repeat the method lines; reading only them counts every line once per class.
            foreach (var line in type.Element("lines")?.Elements("line") ?? [])
            {
                var number = (int?)line.Attribute("number") ?? 0;
                if (number <= 0) continue;
                var hits = ParseHits((string?)line.Attribute("hits"));
                var (covered, total) = ParseCondition((string?)line.Attribute("condition-coverage"));
                data.Add(file, number, new LineHit(hits, covered, total));
            }
        }
        return data;
    }

    // Coverlet writes <source>C:\</source> plus a filename relative to it; other writers use absolute filenames.
    static string ResolveCoberturaPath(string filename, string[] sources)
    {
        if (filename.Length == 0) return "";
        if (IsRooted(filename) || sources.Length == 0) return filename;
        foreach (var source in sources)
        {
            var candidate = Join(source, filename);
            if (File.Exists(candidate)) return candidate;
        }
        return Join(sources[0], filename);
    }

    static CoverageData ReadOpenCover(XElement root)
    {
        var data = new CoverageData();
        foreach (var module in root.Descendants("Module"))
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var file in module.Element("Files")?.Elements("File") ?? [])
                files[(string?)file.Attribute("uid") ?? ""] = (string?)file.Attribute("fullPath") ?? "";
            var lines = new Dictionary<(string File, int Line), (int Hits, int Covered, int Total)>();
            foreach (var method in module.Descendants("Method"))
            {
                var fileRef = (string?)method.Element("FileRef")?.Attribute("uid") ?? "";
                foreach (var point in method.Descendants("SequencePoint"))
                    if (Locate(point, files, fileRef) is { } key)
                    {
                        var current = lines.GetValueOrDefault(key);
                        lines[key] = (current.Hits + Math.Max(0, (int?)point.Attribute("vc") ?? 0), current.Covered, current.Total);
                    }
                foreach (var point in method.Descendants("BranchPoint"))
                    if (Locate(point, files, fileRef) is { } key)
                    {
                        var current = lines.GetValueOrDefault(key);
                        lines[key] = (current.Hits, current.Covered + (((int?)point.Attribute("vc") ?? 0) > 0 ? 1 : 0), current.Total + 1);
                    }
            }
            foreach (var ((file, line), (hits, covered, total)) in lines) data.Add(file, line, new LineHit(hits, covered, total));
        }
        return data;
    }

    static (string File, int Line)? Locate(XElement point, Dictionary<string, string> files, string fileRef)
    {
        var file = files.GetValueOrDefault((string?)point.Attribute("fileid") ?? fileRef, "");
        var line = (int?)point.Attribute("sl") ?? 0;
        return file.Length == 0 || line <= 0 ? null : (file, line);
    }

    static int ParseHits(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hits) ? (int)Math.Clamp(hits, 0, int.MaxValue) : 0;

    // "50% (1/2)"
    static (int Covered, int Total) ParseCondition(string? value)
    {
        if (value is null) return (0, 0);
        var match = ConditionPattern().Match(value);
        return match.Success ? (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)) : (0, 0);
    }

    [GeneratedRegex(@"\((\d+)/(\d+)\)")]
    private static partial Regex ConditionPattern();

    /// <summary>True for a path that is absolute on any operating system (reports may come from another one).</summary>
    internal static bool IsRooted(string path) =>
        path.StartsWith('/') || path.StartsWith('\\') || (path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':');

    static string Join(string directory, string file)
    {
        var separator = directory.Contains('\\') && !directory.Contains('/') ? '\\' : '/';
        return directory.TrimEnd('/', '\\') + separator + file.TrimStart('/', '\\');
    }
}
