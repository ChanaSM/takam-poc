using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Takam.Core;

/// <summary>
/// Works out, from the live site, which files should be in the index right now: one entry per
/// instruction in the sample and one per linked, still-valid message. Comparing this list with
/// what is already indexed is how `update` finds new instructions, new editions and expired documents.
/// </summary>
public static class Catalog
{
    public static async Task<(List<DocMeta> Plan, List<string> Warnings)> BuildPlanAsync(
        TakamSiteApi api, TakamConfig config, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        var plan = new List<DocMeta>();
        var seenMessages = new HashSet<string>();

        var allDocs = (await api.ListDocumentsAsync(ct)).OfType<JsonObject>().ToList();
        var bySfid = allDocs.Where(d => Str(d, "sfid") != null).ToDictionary(d => Str(d, "sfid")!);

        var instructions = allDocs
            .Where(d => TakamConfig.InstructionTypes.Contains(Str(d, "documenttype") ?? "")
                        && InSample(Str(d, "namecode"), config)
                        && !Bool(d, "isdeleted")
                        && IsCurrent(Str(d, "datevalidto")))
            .OrderBy(d => Str(d, "namecode"), StringComparer.Ordinal)
            .ToList();

        var found = instructions.Select(d => Str(d, "namecode")!).ToHashSet();
        foreach (var missing in config.SampleCodes.Where(c => !found.Contains(c)).Order())
            warnings.Add($"הוראה {missing} לא נמצאה באתר או שאינה בתוקף");

        foreach (var d in instructions)
        {
            var code = Str(d, "namecode")!;
            var sfid = Str(d, "sfid")!;
            var current = CurrentVersion(await api.ListVersionsAsync(sfid, ct));
            if (current is null)
            {
                warnings.Add($"להוראה {code} אין גרסאות");
                continue;
            }

            var codeText = Str(d, "documentcodetext")!;
            var contentVersionId = Str(current, "rtn_contentversionid__c")!;
            var instruction = new DocMeta
            {
                Code = code,
                CodeText = codeText,
                DocType = "Instruction",
                Sfid = sfid,
                Name = Str(d, "name") ?? "",
                Chapter = Str(d, "firstnamechapter") ?? "",
                Subchapter = Str(d, "secondnamechapter") ?? "",
                Version = Int(current, "rtn_versionnumber__c"),
                ValidFrom = DatePart(Str(current, "rtn_validfrom__c")),
                ContentVersionId = contentVersionId,
                Keywords = Str(d, "keywords") ?? "",
                SourceUrl = api.DownloadUrl(contentVersionId, codeText),
            };

            foreach (var rel in (await api.ListRelatedAsync(sfid, ct)).OfType<JsonObject>())
            {
                if (!IsCurrent(Str(rel, "validto")))
                    continue;
                var relCv = Str(rel, "rtn_contentversionid__c");
                if (relCv is null)
                    continue;
                var relType = Str(rel, "documenttype");
                if (relType == "Form")
                {
                    instruction.Forms.Add(new FormLink(Str(rel, "docname") ?? "", api.DownloadUrl(relCv, "Form")));
                }
                else if (relType == "Message")
                {
                    var msgSfid = Str(rel, "sfid") ?? Str(rel, "document1");
                    if (msgSfid is null || !bySfid.TryGetValue(msgSfid, out var msg))
                        continue;
                    var msgCode = Str(msg, "documentcodetext");
                    if (msgCode is null || !seenMessages.Add(msgCode))
                        continue;
                    plan.Add(new DocMeta
                    {
                        Code = msgCode,
                        CodeText = msgCode,
                        DocType = "Message",
                        Sfid = msgSfid,
                        Name = Str(rel, "docname") ?? Str(msg, "name") ?? "",
                        Chapter = instruction.Chapter,
                        Subchapter = instruction.Subchapter,
                        Version = Int(rel, "versionnumber"),
                        ValidFrom = DatePart(Str(rel, "validfrom")),
                        ContentVersionId = relCv,
                        ParentCode = code,
                        Keywords = Str(msg, "keywords") ?? "",
                        SourceUrl = api.DownloadUrl(relCv, msgCode),
                    });
                }
            }
            plan.Add(instruction);
        }
        return (plan, warnings);
    }

    private static bool InSample(string? nameCode, TakamConfig config) =>
        nameCode != null
        && (config.SampleCodes.Contains(nameCode) || config.SamplePrefixes.Any(p => nameCode.StartsWith(p, StringComparison.Ordinal)));

    private static bool IsCurrent(string? validTo) =>
        string.IsNullOrEmpty(validTo)
        || DateTimeOffset.Parse(validTo, CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow;

    /// <summary>The edition with no end date; if none is open-ended, the highest edition number.</summary>
    private static JsonObject? CurrentVersion(JsonArray versions)
    {
        var all = versions.OfType<JsonObject>().ToList();
        if (all.Count == 0)
            return null;
        var openEnded = all.Where(v => string.IsNullOrEmpty(Str(v, "rtn_validto__c"))).ToList();
        return (openEnded.Count > 0 ? openEnded : all).MaxBy(v => Int(v, "rtn_versionnumber__c"));
    }

    private static string DatePart(string? iso) => string.IsNullOrEmpty(iso) ? "" : iso[..Math.Min(10, iso.Length)];

    private static string? Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;

    private static int Int(JsonObject o, string key) =>
        o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? (int)v.GetValue<double>() : 0;

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.GetValueKind() == JsonValueKind.True;
}
