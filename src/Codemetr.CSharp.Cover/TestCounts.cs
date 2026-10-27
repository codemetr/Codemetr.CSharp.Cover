using System.Xml;
using System.Xml.Linq;

namespace Codemetr.CSharp.Cover;

/// <summary>Outcome counts of one test run.</summary>
/// <param name="Total">Tests found.</param>
/// <param name="Passed">Tests that passed.</param>
/// <param name="Failed">Tests that failed, errored, timed out or were aborted.</param>
/// <param name="Skipped">Tests that did not run (skipped, ignored, inconclusive).</param>
public sealed record TestCounts(int Total, int Passed, int Failed, int Skipped)
{
    /// <summary>No tests.</summary>
    public static TestCounts None { get; } = new(0, 0, 0, 0);

    /// <summary>Sum of two runs.</summary>
    public static TestCounts operator +(TestCounts a, TestCounts b) => new(a.Total + b.Total, a.Passed + b.Passed, a.Failed + b.Failed, a.Skipped + b.Skipped);

    /// <summary>Reads the counters of a TRX file written by VSTest or Microsoft.Testing.Platform.</summary>
    public static TestCounts ReadTrx(string path)
    {
        XDocument document;
        try
        {
            using var stream = File.OpenRead(path);
            document = XDocument.Load(XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }));
        }
        catch (XmlException e) { throw new InvalidDataException($"Test result file is not valid XML: {path}: {e.Message}"); }
        var counters = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Counters")
            ?? throw new InvalidDataException($"Test result file has no counters: {path}");
        int Count(string name) => (int?)counters.Attribute(name) ?? 0;
        var failed = Count("failed") + Count("error") + Count("timeout") + Count("aborted");
        var skipped = Count("notExecuted") + Count("inconclusive") + Count("notRunnable") + Count("disconnected") + Count("pending");
        var total = Math.Max(Count("total"), Count("passed") + failed + skipped);
        return new TestCounts(total, Count("passed"), failed, skipped);
    }
}
