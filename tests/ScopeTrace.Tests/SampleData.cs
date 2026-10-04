namespace ScopeTrace.Tests;

internal static class SampleData
{
    public static byte[] Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ScopeTrace.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new FileNotFoundException("Could not locate the repository root.");
        return File.ReadAllBytes(Path.Combine(dir.FullName, "samples", "54600_sample.plt"));
    }
}
