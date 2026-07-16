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
                var procs = new Dictionary<int, DiagnosticProcess>();
                var handler = new ProcessHandler();
                _ = handler.Scan(procs);
                foreach(var kvp in procs)
                {
                    var p = kvp.Value;
                    Console.WriteLine($"PID={p.PID}  File={p.Filename}");
                    Console.WriteLine($"  Cookie={p.RuntimeInstanceCookie}");
                    Console.WriteLine($"  Arch={p.ProcessArchitecture}");
                    Console.WriteLine($"  Entry={p.ManagedEntrypointAssemblyName}");
                    Console.WriteLine($"  CLR={p.ClrProductVersionString}");
                    Console.WriteLine($"  RID={p.PortableRuntimeIdentifier}");
                    Console.WriteLine($"  Cmd={p.CommandLine}");
                    Console.WriteLine();
                }
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