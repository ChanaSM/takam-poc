namespace Takam.Core;

/// <summary>Settings for the POC: which instructions are in the sample, where data lives, and download pacing.</summary>
public sealed record TakamConfig
{
    public string BaseUrl { get; init; } = "https://takam.mof.gov.il";
    public string UserAgent { get; init; } = "takam-poc/0.1 (research prototype)";
    public TimeSpan DownloadDelay { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan HttpTimeout { get; init; } = TimeSpan.FromSeconds(60);

    public string DataDir { get; init; } = DefaultDataDir();
    public string RawDir => Path.Combine(DataDir, "raw");
    public string IndexPath => Path.Combine(DataDir, "index.json");

    /// <summary>
    /// Instructions in the sample, by Takam number. Linked messages (tariff tables) are pulled in
    /// automatically. Scenario: a seminar day with an external lecturer and catering, plus 7.4.1
    /// (the verified case where the site's own search misses body text) and 7.3.3 (guarantees),
    /// the instruction the site does find for that query.
    /// </summary>
    public IReadOnlyList<string> SampleCodes { get; init; } =
        ["8.1.2", "2.6.1", "13.4.5", "15.4.3", "13.13.1", "7.4.1", "7.3.3"];

    /// <summary>
    /// Optional rule-based sample: every instruction whose number starts with one of these prefixes
    /// is included, so a brand-new instruction in those subchapters is picked up by `update`.
    /// </summary>
    public IReadOnlyList<string> SamplePrefixes { get; init; } = [];

    public static readonly IReadOnlySet<string> InstructionTypes =
        new HashSet<string> { "Instruction", "Temporary Instruction" };

    /// <summary>TAKAM_DATA_DIR if set, otherwise a "data" folder next to Takam.sln.</summary>
    private static string DefaultDataDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TAKAM_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Takam.sln")) || File.Exists(Path.Combine(dir.FullName, "Takam.slnx")))
                return Path.Combine(dir.FullName, "data");
        }
        return Path.Combine(Directory.GetCurrentDirectory(), "data");
    }
}
