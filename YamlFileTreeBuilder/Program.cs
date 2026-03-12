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

        // Set up output file if specified
        StreamWriter? fileWriter = null;
        if (!string.IsNullOrEmpty(outputFile))
        {
            string outputPath = Path.GetFullPath(outputFile);
            fileWriter = new StreamWriter(outputPath);
            Console.WriteLine($"Writing output to: {outputPath}");
        }

        try
        {
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

            var treeBuilder = new TreeBuilder(basePaths, detailLevel, fileWriter);

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
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -j, --jobs      Show job names alongside template files");
        Console.WriteLine("  -t, --tasks     Show job names AND task/step names");
        Console.WriteLine("  -o, --output    Write output to a text file (in addition to console)");
        Console.WriteLine("  -h, --help      Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  YamlFileTreeBuilder pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -j pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -t pipeline.yml");
        Console.WriteLine("  YamlFileTreeBuilder -t -o output.txt pipeline.yml");
    }
}

public enum DetailLevel
{
    FilesOnly,
    WithJobs,
    WithJobsAndTasks
}
