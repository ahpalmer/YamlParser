using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace YamlFileTreeBuilder;

class BatchRootsConfig
{
    public string[] RootFiles { get; set; } = Array.Empty<string>();
}

public class BatchAnalyzer
{
    private readonly string[] _basePaths;
    private readonly TreeBuilder _treeBuilder;

    public BatchAnalyzer(string[] basePaths)
    {
        _basePaths = basePaths;
        _treeBuilder = new TreeBuilder(basePaths);
    }

    public void Run()
    {
        string? batchJsonPath = FindBatchRootsJson();
        if (batchJsonPath == null)
        {
            Console.WriteLine("Error: BatchRoots.json not found. Place it in the YamlFileTreeBuilder project directory.");
            Console.WriteLine("  Expected format:");
            Console.WriteLine("  {");
            Console.WriteLine("    \"RootFiles\": [");
            Console.WriteLine("      \"/full/path/to/pipeline1.yml\",");
            Console.WriteLine("      \"/full/path/to/pipeline2.yml\"");
            Console.WriteLine("    ]");
            Console.WriteLine("  }");
            return;
        }

        Console.WriteLine($"Loading batch roots from: {batchJsonPath}");

        var rootFiles = LoadAndValidateRootFiles(batchJsonPath);
        if (rootFiles == null)
            return;

        Console.WriteLine($"Processing {rootFiles.Count} root files...\n");

        var referencedBy = CollectAllReferences(rootFiles);

        var ranked = referencedBy
            .Select(kvp => new RankedFile { File = kvp.Key, Count = kvp.Value.Count, Roots = kvp.Value })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.File)
            .ToList();

        PrintConsoleResults(ranked, rootFiles);
        WriteOutputFile(ranked, rootFiles, Path.GetDirectoryName(batchJsonPath)!);
    }

    // Looks for the BatchRoots.json file starting from the bin / net (executable directory)
    private string? FindBatchRootsJson()
    {
        // Walk up from the executable directory
        string? path = FindFileUpwards("BatchRoots.json", AppContext.BaseDirectory);
        if (path != null)
            return path;

        // Also try current working directory
        string cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "BatchRoots.json");
        if (File.Exists(cwdPath))
            return cwdPath;

        return null;
    }

    // Starts in bin folder and steps up folders one at a time until it finds BatchRoots.json
    private static string? FindFileUpwards(string fileName, string startDir)
    {
        string? dir = startDir;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private List<string>? LoadAndValidateRootFiles(string batchJsonPath)
    {
        string json = File.ReadAllText(batchJsonPath);
        var batchConfig = JsonSerializer.Deserialize<BatchRootsConfig>(json);
        if (batchConfig?.RootFiles == null || batchConfig.RootFiles.Length == 0)
        {
            Console.WriteLine("Error: No RootFiles found in BatchRoots.json");
            return null;
        }

        var rootFiles = new List<string>();
        foreach (var rf in batchConfig.RootFiles)
        {
            string fullPath = Path.GetFullPath(rf);
            if (!File.Exists(fullPath))
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"  Warning: Root file not found, skipping: {fullPath}");
                Console.ResetColor();
            }
            else
            {
                rootFiles.Add(fullPath);
            }
        }

        if (rootFiles.Count == 0)
        {
            Console.WriteLine("Error: None of the specified root files were found.");
            return null;
        }

        return rootFiles;
    }

    // Main flow for this stuff
    private Dictionary<string, HashSet<string>> CollectAllReferences(List<string> rootFiles)
    {
        var referencedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < rootFiles.Count; i++)
        {
            string rootFile = rootFiles[i];
            Console.Write($"\r  Scanning root {i + 1}/{rootFiles.Count}...");

            var dependencies = CollectDependencies(rootFile);

            foreach (var dep in dependencies)
            {
                if (!referencedBy.ContainsKey(dep))
                    referencedBy[dep] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                referencedBy[dep].Add(rootFile);
            }
        }

        Console.WriteLine("\r  Scanning complete.          ");
        return referencedBy;
    }

    // Recursively collects all template dependencies for a given file, avoiding cycles and duplicates
    // I'm not sure if this is what I want.  I need to know the most common ancestors
    private HashSet<string> CollectDependencies(string filePath)
    {
        var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectDependenciesRecursive(filePath, visited, dependencies);
        return dependencies;
    }

    private void CollectDependenciesRecursive(string filePath, HashSet<string> visited, HashSet<string> dependencies)
    {
        string absPath = Path.GetFullPath(filePath);

        if (visited.Contains(absPath))
            return;
        visited.Add(absPath);

        if (!File.Exists(absPath))
            return;

        string content = File.ReadAllText(absPath);

        foreach (var templatePath in _treeBuilder.FindTemplateReferences(content))
        {
            string? resolvedPath = _treeBuilder.ResolveTemplatePath(templatePath, Path.GetDirectoryName(absPath) ?? "");
            if (resolvedPath != null)
            {
                dependencies.Add(resolvedPath);
                CollectDependenciesRecursive(resolvedPath, visited, dependencies);
            }
        }
    }

    private void PrintConsoleResults(List<RankedFile> ranked, List<string> rootFiles)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Most Common YML Files (Top 30 of {ranked.Count} total) ===");
        Console.WriteLine($"{"Rank",-6} {"Count",-7} File");
        Console.WriteLine(new string('-', 80));

        int displayCount = Math.Min(30, ranked.Count);
        for (int i = 0; i < displayCount; i++)
        {
            var entry = ranked[i];
            string displayPath = GetDisplayPath(entry.File);
            bool isRoot = rootFiles.Contains(entry.File, StringComparer.OrdinalIgnoreCase);
            string rootMarker = isRoot ? " *ROOT*" : "";

            var color = i < 10 ? ConsoleColor.Cyan : i < 20 ? ConsoleColor.Yellow : ConsoleColor.Green;
            Console.ForegroundColor = color;
            Console.WriteLine($"{i + 1,-6} {entry.Count,-7} {displayPath}{rootMarker}");
            Console.ResetColor();
        }

        Console.WriteLine();
        Console.WriteLine($"Total unique files referenced: {ranked.Count}");
        Console.WriteLine($"Root files processed: {rootFiles.Count}");
    }

    private void WriteOutputFile(List<RankedFile> ranked, List<string> rootFiles, string outputDir)
    {
        string outputPath = Path.Combine(outputDir, "BatchTreeOutput.txt");

        using (var writer = new StreamWriter(outputPath))
        {
            writer.WriteLine($"Batch Analysis - Most Commonly Referenced YML Files");
            writer.WriteLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            writer.WriteLine($"Root files processed: {rootFiles.Count}");
            writer.WriteLine($"Total unique files referenced: {ranked.Count}");
            writer.WriteLine();

            for (int i = 0; i < ranked.Count; i++)
            {
                var entry = ranked[i];
                string displayPath = GetDisplayPath(entry.File);
                bool isRoot = rootFiles.Contains(entry.File, StringComparer.OrdinalIgnoreCase);
                string rootMarker = isRoot ? " *ROOT*" : "";

                writer.WriteLine($"{i + 1}. [{entry.Count} references] {displayPath}{rootMarker}");

                foreach (var rootFile in entry.Roots.OrderBy(r => r))
                {
                    writer.WriteLine($"\t{GetDisplayPath(rootFile)}");
                }

                writer.WriteLine();
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Full results written to: {outputPath}");
        Console.ResetColor();
    }

    private string GetDisplayPath(string fullPath)
    {
        foreach (var basePath in _basePaths)
        {
            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePart = fullPath.Substring(basePath.Length).TrimStart(Path.DirectorySeparatorChar);
                string folderName = Path.GetFileName(basePath);
                return $"[{folderName}] {relativePart}";
            }
        }
        return fullPath;
    }

    private class RankedFile
    {
        public string File { get; set; } = "";
        public int Count { get; set; }
        public HashSet<string> Roots { get; set; } = new();
    }
}
