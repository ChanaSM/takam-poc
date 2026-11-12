// Command line entry point.
//
//   dotnet run --project src/Takam.Cli -- rebuild          build the index from scratch
//   dotnet run --project src/Takam.Cli -- update           re-index only what changed on the site
//   dotnet run --project src/Takam.Cli -- stats            show what is indexed
//   dotnet run --project src/Takam.Cli -- search "חילוט ערבות"
//   dotnet run --project src/Takam.Cli -- parse file.docx  show the sections found in one file
using Takam.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var config = new TakamConfig();
var command = args.Length > 0 ? args[0] : "update";

switch (command)
{
    case "rebuild":
        if (File.Exists(config.IndexPath))
            File.Delete(config.IndexPath);
        return await Update();

    case "update":
        return await Update();

    case "stats":
    {
        var store = new IndexStore(config.IndexPath);
        Console.WriteLine(store.Stats());
        Console.WriteLine("עדכון אחרון: " + (store.LastSync()?.RunAt ?? "לא בוצע"));
        return 0;
    }

    case "search":
    {
        var query = string.Join(' ', args.Skip(1));
        var engine = new SearchEngine(new IndexStore(config.IndexPath).Snapshot());
        foreach (var hit in engine.Search(query, 10))
            Console.WriteLine($"{hit.Code} §{hit.Number} {hit.Title}  [{hit.Score:F2}]");
        Console.WriteLine("באתר (שם ומילות מפתח בלבד): " + string.Join(", ", engine.SiteStyleSearch(query)));
        return 0;
    }

    case "parse" when args.Length > 1:
        foreach (var s in DocxParser.Parse(args[1]))
            Console.WriteLine($"{s.Number,-8} | {Shorten(s.Title, 55)} | {s.Body.Length}");
        return 0;

    default:
        Console.WriteLine("פקודות: rebuild | update | stats | search <מילים> | parse <קובץ>");
        return 2;
}

async Task<int> Update()
{
    var store = new IndexStore(config.IndexPath);
    var counts = await SyncService.SyncFromSiteAsync(config, store);
    Console.WriteLine("סיכום: " + counts);
    Console.WriteLine(store.Stats());
    return counts.Failed > 0 ? 1 : 0;
}

static string Shorten(string s, int n) => s.Length <= n ? s : s[..n];
