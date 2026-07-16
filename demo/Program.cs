using MV10.DotnetUptime.Lib;

namespace demo;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length != 1)
        {
            Help();
            return;
        }

        switch (args[0].ToLowerInvariant())
        {
            case "list":
            {
                var procs = new Dictionary<int, DiagProcess>();
                var handler = new ProcessHandler();
                _ = handler.Scan(procs);
                foreach(var kvp in procs)
                    Console.WriteLine($"{kvp.Key}: {kvp.Value.CommandLine}");
            }
                break;
            
            default:
                Help();
                break;
        }
    }

    static void Help()
    {
        Console.WriteLine(@"
demo list     outputs all processes exposing a .NET diagnostics port
");
    }
}