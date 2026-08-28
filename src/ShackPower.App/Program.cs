// Shack Power — placeholder entry point so the solution builds during Phase 1/2.
// Replaced in Phase 3 by the real Avalonia bootstrap ported from w2-monitor-x.
namespace ShackPower.App;

internal static class Program
{
    public static void Main(string[] args)
    {
        Console.Error.WriteLine("Shack Power app shell arrives in Phase 3; run the tests instead.");
        Environment.ExitCode = 1;
    }
}
