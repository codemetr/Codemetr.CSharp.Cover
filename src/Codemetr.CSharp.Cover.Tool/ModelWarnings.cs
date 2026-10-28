using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Conditions of a coverage model that limit what its numbers prove.</summary>
internal static class ModelWarnings
{
    public static IEnumerable<string> For(CoverageModel model, Evidence evidence, string unreported)
    {
        if (model.Uninstrumented.Count > 0)
            yield return ($"{model.Uninstrumented.Count} local function(s) have no coverage data although their enclosing member has (coverage engines such as "
                + "coverlet.MTP skip compiler-generated code); they are not counted.");
        // coverlet.MTP never reports compiler-generated code; merged with a VSTest report that counts it uncovered, it stays uncovered.
        if (evidence.Projects.Any(p => p.Platform == TestProject.Mtp) && evidence.Projects.Any(p => p.Platform == TestProject.VSTest)
            && model.Members.Any(m => m.Member.Kind == "localFunction" && m.Count.CoveredLines < m.Count.CoverableLines))
            yield return ("Microsoft.Testing.Platform and VSTest projects are merged: coverlet.MTP does not report local functions and lambda bodies, "
                + "so their coverage by MTP tests is missing and may show as uncovered.");
        if (model.Unmapped > 0)
            yield return ($"{model.Unmapped} covered line(s) lie outside any member or type of the current sources; sources and coverage may not match.");
        var unreportedFiles = model.Files.Count(f => !f.Reported && f.Count.CoverableLines > 0);
        if (unreportedFiles > 0 && unreported == UnreportedMode.Zero)
            yield return ($"{unreportedFiles} file(s) have no coverage data and count as 0% (no test project covers them); see 'unreported'.");
    }
}
