using System.Diagnostics;

namespace TheLithium.Imprint.Tool;

internal static class Program
{
    private const string Help = """
        TheLithium.Imprint process wrapper

        imprint run [--update verify|missing|all] [--test GLOB]
                 [--project-root PATH] [--config PATH]
                 [--read-only] [--allow-ci-update] -- COMMAND [ARGUMENTS]

        Examples:
          imprint run --update all -- dotnet test
          imprint run --update all --test '*InstallTests*' -- dotnet test
          imprint run --update verify -- ./My.Native.Tests

        --test controls update authorization, not runner test selection.
        Nonmatching tests verify. Pass test-selection flags after COMMAND.
        No baselines are accepted from stale received files by this tool.
        """;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine(Help);
            return 0;
        }
        try
        {
            if (args[0] != "run") throw new ArgumentException("The only command is 'run'.");
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            var separator = Array.IndexOf(args, "--");
            if (separator < 0 || separator == args.Length - 1)
                throw new ArgumentException("Separate the child command with '--'.");
            for (var i = 1; i < separator; i++)
            {
                string Value()
                {
                    if (++i >= separator) throw new ArgumentException("An option value is missing.");
                    return args[i];
                }
                switch (args[i])
                {
                    case "--update":
                        var update = Value().ToLowerInvariant();
                        if (update is not ("verify" or "missing" or "all"))
                            throw new ArgumentException("--update must be verify, missing or all.");
                        variables["IMPRINT_UPDATE"] = update;
                        break;
                    case "--test": variables["IMPRINT_TEST"] = Value(); break;
                    case "--project-root": variables["IMPRINT_PROJECT_ROOT"] = Path.GetFullPath(Value()); break;
                    case "--config": variables["IMPRINT_CONFIG"] = Path.GetFullPath(Value()); break;
                    case "--read-only": variables["IMPRINT_READ_ONLY"] = "true"; break;
                    case "--allow-ci-update": variables["IMPRINT_ALLOW_CI_UPDATE"] = "true"; break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (variables.ContainsKey("IMPRINT_TEST") && !variables.ContainsKey("IMPRINT_UPDATE"))
                throw new ArgumentException("--test requires an explicit --update policy.");
            var start = new ProcessStartInfo(args[separator + 1]) { UseShellExecute = false };
            for (var i = separator + 2; i < args.Length; i++) start.ArgumentList.Add(args[i]);
            // An explicit new policy without a selector applies to this entire child run.
            if (variables.ContainsKey("IMPRINT_UPDATE") && !variables.ContainsKey("IMPRINT_TEST"))
                start.Environment.Remove("IMPRINT_TEST");
            foreach (var variable in variables) start.Environment[variable.Key] = variable.Value;
            using var process = new Process { StartInfo = start };
            if (!process.Start()) throw new IOException("The test process could not be started.");
            var cancelled = 0;
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                Interlocked.Exchange(ref cancelled, 1);
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            };
            Console.CancelKeyPress += cancel;
            try { await process.WaitForExitAsync().ConfigureAwait(false); }
            finally { Console.CancelKeyPress -= cancel; }
            return Volatile.Read(ref cancelled) != 0 ? 130 : process.ExitCode;
        }
        catch (Exception error) when (error is ArgumentException or IOException
                   or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine("Run 'imprint --help' for usage.");
            return 2;
        }
    }
}
