// Runs every method marked [Test] in this assembly and prints pass or fail.
// Exit code is the number of failures, so it works in scripts and CI.
//
//   dotnet run --project tests/Takam.Tests
//   dotnet run --project tests/Takam.Tests -- Search     (only tests whose name contains "Search")
using System.Diagnostics;
using System.Reflection;
using Takam.Tests;

Console.OutputEncoding = System.Text.Encoding.UTF8;
var filter = args.FirstOrDefault() ?? "";
var tests = typeof(TestAttribute).Assembly.GetTypes()
    .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
    .Where(m => m.GetCustomAttribute<TestAttribute>() != null)
    .Where(m => $"{m.DeclaringType!.Name}.{m.Name}".Contains(filter, StringComparison.OrdinalIgnoreCase))
    .OrderBy(m => m.DeclaringType!.Name).ThenBy(m => m.Name)
    .ToList();

var failed = 0;
var clock = Stopwatch.StartNew();
foreach (var test in tests)
{
    var name = $"{test.DeclaringType!.Name}.{test.Name}";
    try
    {
        var result = test.Invoke(null, null);
        if (result is Task task)
            await task;
        Console.WriteLine($"  pass  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        var inner = ex is TargetInvocationException { InnerException: { } i } ? i : ex;
        Console.WriteLine($"  FAIL  {name}\n        {inner.Message}");
    }
}
Console.WriteLine($"\n{tests.Count - failed} passed, {failed} failed ({clock.Elapsed.TotalSeconds:F1}s)");
return failed;
