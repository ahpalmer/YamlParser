using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace YamlFileTreeBuilder;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        // Parse arguments
        string? rootPath = null;
        DetailLevel detailLevel = DetailLevel.FilesOnly;
        string? outputFile = null;
        bool batchMode = false;
        bool dimVisited = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "-j":
                case "--jobs":
                    detailLevel = DetailLevel.WithJobs;
                    break;
                case "-t":
                case "--tasks":
                    detailLevel = DetailLevel.WithJobsAndTasks;
                    break;
                case "-o":
                case "--output":
                    if (i + 1 < args.Length)
                    {
                        outputFile = args[++i];
                    }
                    else
                    {
                        Console.WriteLine("Error: -o/--output requires a file path argument");
                        return;
                    }
                    break;
                case "-b":
                case "--batch":
                    batchMode = true;
                    break;
                case "-d":
                case "--dim-visited":
                    dimVisited = true;
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    return;
                default:
                    if (!args[i].StartsWith("-"))
                    {
                        rootPath = args[i];
                    }
                    else
                    {
                        Console.WriteLine($"Unknown option: {args[i]}");
                        PrintUsage();
                        return;
                    }
                    break;
            }
        }

        // Load configuration (needed for both modes)
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

        var basePaths = configuration.GetSection("BasePaths").Get<string[]>();
        if (basePaths == null || basePaths.Length == 0)
        {
            Console.WriteLine("Error: No BasePaths found in user secrets. Run 'dotnet user-secrets set' or manage user secrets in your IDE.");
            Console.WriteLine("  Expected format: { \"BasePaths\": [\"/path/one\", \"/path/two\"] }");
            return;
        }

        if (batchMode)
        {
            var batchAnalyzer = new BatchAnalyzer(basePaths);
            batchAnalyzer.Run();
            return;
        }

        // --- Normal single-file mode ---
        if (string.IsNullOrEmpty(rootPath))
        {
            Console.WriteLine("Error: No YAML file specified");
            PrintUsage();
            return;
        }

        rootPath = Path.GetFullPath(rootPath);
        if (!File.Exists(rootPath))
        {
            Console.WriteLine($"File not found: {rootPath}");
            return;
        }

        StreamWriter? fileWriter = null;
        if (!string.IsNullOrEmpty(outputFile))
        {
            string outputPath = Path.GetFullPath(outputFile);
            fileWriter = new StreamWriter(outputPath);
            Console.WriteLine($"Writing output to: {outputPath}");
        }

        try
        {
            var treeBuilder = new TreeBuilder(basePaths, detailLevel, fileWriter, dimVisited);

            string header = $"Dependency tree for: {rootPath}";
            string detailInfo = detailLevel switch
            {
                DetailLevel.WithJobs => " (showing jobs)",
                DetailLevel.WithJobsAndTasks => " (showing jobs and tasks)",
                _ => ""
            };
            header += detailInfo;

            treeBuilder.WriteLine(header);
            treeBuilder.WriteLine("");
            treeBuilder.PrintDependencyTree(rootPath, 0);

            if (fileWriter != null)
            {
                Console.WriteLine($"\nOutput saved to: {Path.GetFullPath(outputFile!)}");
            }
        }
        finally
        {
            fileWriter?.Dispose();
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("YamlFileTreeBuilder - Azure DevOps Pipeline Dependency Tree Viewer");
        Console.WriteLine();
        Console.WriteLine("Usage: YamlFileTreeBuilder [options] <yaml-file>");
        Console.WriteLine("       YamlFileTreeBuilder -b");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -j, --jobs      Show job names alongside template files");
        Console.WriteLine("  -t, --tasks     Show job names AND task/step names");
        Console.WriteLine("  -o, --output    Write output to a text file (in addition to console)");
        Console.WriteLine("  -b, --batch     Batch mode: process all root files from BatchRoots.json");
        Console.WriteLine("                  and display the most commonly referenced YML files");
        Console.WriteLine("  -d, --dim-visited  Grey out already-visited files (default: colorful)");
        Console.WriteLine("  -h, --help      Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  YamlFileTreeBuilder pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -j pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -t pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -t -o output.txt pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -b");
    }
}

public enum DetailLevel
{
    FilesOnly,
    WithJobs,
    WithJobsAndTasks
}
