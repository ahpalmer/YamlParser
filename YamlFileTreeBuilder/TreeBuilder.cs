using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace YamlFileTreeBuilder;

public class TreeBuilder
{
    // Base paths to search for templates (order matters - first match wins)
    private readonly string[] _basePaths;

    private readonly HashSet<string> _visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly DetailLevel _detailLevel;
    private readonly StreamWriter? _fileWriter;

    // Regex patterns
    private static readonly Regex TemplateRegex = new Regex(
        @"^\s*-?\s*template:\s*([^\s#\r\n]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Job patterns - matches "- job: Name" or "job: Name" or template with Name parameter
    private static readonly Regex JobDirectRegex = new Regex(
        @"^\s*-?\s*job:\s*([^\s#\r\n]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex JobNameParamRegex = new Regex(
        @"^\s*Name:\s*['""]?([^'""#\r\n]+)['""]?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex DisplayNameRegex = new Regex(
        @"^\s*displayName:\s*['""]?([^'""#\r\n]+)['""]?",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Task/step patterns
    private static readonly Regex TaskRegex = new Regex(
        @"^\s*-?\s*task:\s*([^\s#@\r\n]+)",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex ScriptRegex = new Regex(
        @"^\s*-\s*script:\s*",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex PowershellRegex = new Regex(
        @"^\s*-\s*powershell:\s*",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex BashRegex = new Regex(
        @"^\s*-\s*bash:\s*",
        RegexOptions.Multiline | RegexOptions.Compiled);

    // Colors for different tree levels
    private static readonly ConsoleColor[] LevelColors = new[]
    {
        ConsoleColor.Cyan,
        ConsoleColor.Yellow,
        ConsoleColor.Green,
        ConsoleColor.Magenta,
        ConsoleColor.Blue,
        ConsoleColor.Red,
        ConsoleColor.White,
        ConsoleColor.DarkCyan,
        ConsoleColor.DarkYellow,
        ConsoleColor.DarkGreen
    };

    public TreeBuilder(string[] basePaths, DetailLevel detailLevel = DetailLevel.FilesOnly, StreamWriter? fileWriter = null)
    {
        _basePaths = basePaths;
        _detailLevel = detailLevel;
        _fileWriter = fileWriter;
    }

    public void WriteLine(string text)
    {
        Console.WriteLine(text);
        _fileWriter?.WriteLine(text);
    }

    private void WriteColored(string text, int level, bool dimmed = false, ConsoleColor? overrideColor = null)
    {
        var originalColor = Console.ForegroundColor;

        if (overrideColor.HasValue)
        {
            Console.ForegroundColor = overrideColor.Value;
        }
        else if (dimmed)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
        }
        else
        {
            Console.ForegroundColor = LevelColors[level % LevelColors.Length];
        }

        Console.WriteLine(text);
        Console.ForegroundColor = originalColor;

        // Write plain text to file
        _fileWriter?.WriteLine(text);
    }

    public void PrintDependencyTree(string filePath, int indent)
    {
        string absPath = Path.GetFullPath(filePath);
        string displayPath = GetDisplayPath(absPath);

        if (_visited.Contains(absPath))
        {
            WriteColored($"{new string(' ', indent * 2)}- {displayPath} (already visited)", indent, dimmed: true);
            return;
        }
        _visited.Add(absPath);

        // Build the display line with optional job/task info
        string content = File.ReadAllText(filePath);
        string fileInfo = BuildFileInfoLine(displayPath, content, indent);
        WriteColored(fileInfo, indent);

        // Show jobs and tasks if requested
        if (_detailLevel >= DetailLevel.WithJobs)
        {
            PrintJobsAndTasks(content, indent);
        }

        // Process child templates
        foreach (var templatePath in FindTemplateReferences(content))
        {
            string? resolvedPath = ResolveTemplatePath(templatePath, Path.GetDirectoryName(filePath) ?? "");
            if (resolvedPath != null)
            {
                PrintDependencyTree(resolvedPath, indent + 1);
            }
            else
            {
                WriteColored($"{new string(' ', (indent + 1) * 2)}- {templatePath} (NOT FOUND)", indent + 1, overrideColor: ConsoleColor.DarkRed);
            }
        }
    }

    private string BuildFileInfoLine(string displayPath, string content, int indent)
    {
        string indentStr = new string(' ', indent * 2);
        return $"{indentStr}- {displayPath}";
    }

    private void PrintJobsAndTasks(string content, int indent)
    {
        var jobs = ExtractJobs(content);
        string jobIndent = new string(' ', (indent + 1) * 2);

        foreach (var job in jobs)
        {
            // Print job with a different style
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"{jobIndent}[Job: {job.Name}]");
            Console.ForegroundColor = originalColor;
            _fileWriter?.WriteLine($"{jobIndent}[Job: {job.Name}]");

            // Print tasks if requested
            if (_detailLevel >= DetailLevel.WithJobsAndTasks && job.Tasks.Count > 0)
            {
                string taskIndent = new string(' ', (indent + 2) * 2);
                foreach (var task in job.Tasks)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"{taskIndent}> {task}");
                    Console.ForegroundColor = originalColor;
                    _fileWriter?.WriteLine($"{taskIndent}> {task}");
                }
            }
        }
    }

    private List<JobInfo> ExtractJobs(string content)
    {
        var jobs = new List<JobInfo>();
        var lines = content.Split('\n');

        JobInfo? currentJob = null;
        bool inStepsSection = false;
        int jobIndentLevel = -1;
        int stepsIndentLevel = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            int lineIndent = GetIndentLevel(line);
            string trimmedLine = line.TrimStart();

            // Check for job definition
            var jobMatch = JobDirectRegex.Match(line);
            if (jobMatch.Success)
            {
                if (currentJob != null)
                    jobs.Add(currentJob);

                string jobName = jobMatch.Groups[1].Value.Trim();
                // Handle parameter references like ${{ parameters.Name }}
                if (jobName.Contains("${{"))
                {
                    // Try to find displayName nearby
                    jobName = FindNearbyDisplayName(lines, i) ?? jobName;
                }
                currentJob = new JobInfo { Name = jobName };
                jobIndentLevel = lineIndent;
                inStepsSection = false;
                continue;
            }

            // Check for template with Name parameter (job template)
            if (trimmedLine.StartsWith("- template:") && currentJob == null)
            {
                // Look for Name parameter in the following lines
                string? templateJobName = FindNameParameter(lines, i);
                if (templateJobName != null)
                {
                    currentJob = new JobInfo { Name = templateJobName };
                    jobIndentLevel = lineIndent;
                    inStepsSection = false;
                }
            }

            // Check for Name: parameter that might define a job name
            var nameParamMatch = JobNameParamRegex.Match(line);
            if (nameParamMatch.Success && currentJob == null)
            {
                string name = nameParamMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(name) && !name.Contains("${{"))
                {
                    currentJob = new JobInfo { Name = name };
                    jobIndentLevel = lineIndent;
                }
            }

            // Check if we're entering steps section
            if (trimmedLine.StartsWith("steps:") && currentJob != null)
            {
                inStepsSection = true;
                stepsIndentLevel = lineIndent;
                continue;
            }

            // If we're in a steps section, look for tasks
            if (inStepsSection && currentJob != null && _detailLevel >= DetailLevel.WithJobsAndTasks)
            {
                // Check if we've exited the steps section (lower indent)
                if (lineIndent <= stepsIndentLevel && !string.IsNullOrWhiteSpace(trimmedLine) && !trimmedLine.StartsWith("-"))
                {
                    inStepsSection = false;
                    continue;
                }

                // Look for task definitions
                string? taskName = ExtractTaskName(line, lines, i);
                if (taskName != null)
                {
                    currentJob.Tasks.Add(taskName);
                }
            }

            // Check if we've moved to a new job (indent level change)
            if (currentJob != null && lineIndent <= jobIndentLevel && !string.IsNullOrWhiteSpace(trimmedLine))
            {
                if (trimmedLine.StartsWith("- job:") || trimmedLine.StartsWith("- template:") || trimmedLine.StartsWith("jobs:"))
                {
                    jobs.Add(currentJob);
                    currentJob = null;
                    inStepsSection = false;
                }
            }
        }

        if (currentJob != null)
            jobs.Add(currentJob);

        return jobs;
    }

    private string? FindNearbyDisplayName(string[] lines, int startIndex)
    {
        // Look at next few lines for displayName
        for (int i = startIndex + 1; i < Math.Min(startIndex + 5, lines.Length); i++)
        {
            var match = DisplayNameRegex.Match(lines[i]);
            if (match.Success)
                return match.Groups[1].Value.Trim();
        }
        return null;
    }

    private string? FindNameParameter(string[] lines, int startIndex)
    {
        // Look at next several lines for Name: parameter
        int baseIndent = GetIndentLevel(lines[startIndex]);
        for (int i = startIndex + 1; i < Math.Min(startIndex + 20, lines.Length); i++)
        {
            int lineIndent = GetIndentLevel(lines[i]);
            if (lineIndent <= baseIndent && !string.IsNullOrWhiteSpace(lines[i]))
                break;

            var match = JobNameParamRegex.Match(lines[i]);
            if (match.Success)
            {
                string name = match.Groups[1].Value.Trim();
                if (!name.Contains("${{"))
                    return name;
            }
        }
        return null;
    }

    private string? ExtractTaskName(string line, string[] lines, int index)
    {
        string trimmed = line.TrimStart();

        // Check for task:
        var taskMatch = TaskRegex.Match(line);
        if (taskMatch.Success)
        {
            string taskType = taskMatch.Groups[1].Value;
            string? displayName = FindNearbyDisplayName(lines, index);
            return displayName ?? $"Task: {taskType}";
        }

        // Check for script:
        if (ScriptRegex.IsMatch(line))
        {
            string? displayName = FindNearbyDisplayName(lines, index);
            return displayName ?? "Script";
        }

        // Check for powershell:
        if (PowershellRegex.IsMatch(line))
        {
            string? displayName = FindNearbyDisplayName(lines, index);
            return displayName ?? "PowerShell";
        }

        // Check for bash:
        if (BashRegex.IsMatch(line))
        {
            string? displayName = FindNearbyDisplayName(lines, index);
            return displayName ?? "Bash";
        }

        // Check for checkout:
        if (trimmed.StartsWith("- checkout:"))
        {
            return "Checkout";
        }

        // Check for template: within steps (sub-template)
        if (trimmed.StartsWith("- template:"))
        {
            var match = TemplateRegex.Match(line);
            if (match.Success)
            {
                string templatePath = match.Groups[1].Value;
                int atIndex = templatePath.IndexOf('@');
                if (atIndex > 0) templatePath = templatePath.Substring(0, atIndex);
                return $"Template: {Path.GetFileName(templatePath)}";
            }
        }

        return null;
    }

    private int GetIndentLevel(string line)
    {
        int spaces = 0;
        foreach (char c in line)
        {
            if (c == ' ') spaces++;
            else if (c == '\t') spaces += 2;
            else break;
        }
        return spaces;
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

    public IEnumerable<string> FindTemplateReferences(string content)
    {
        var references = new List<string>();

        foreach (Match match in TemplateRegex.Matches(content))
        {
            string templatePath = match.Groups[1].Value.Trim();

            if (templatePath.Contains("${{") || templatePath.Contains("$("))
                continue;

            templatePath = templatePath.Trim('\'', '"');

            int atIndex = templatePath.IndexOf('@');
            if (atIndex > 0)
            {
                templatePath = templatePath.Substring(0, atIndex);
            }

            if (!string.IsNullOrEmpty(templatePath))
            {
                references.Add(templatePath);
            }
        }

        return references.Distinct();
    }

    public string? ResolveTemplatePath(string templatePath, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(templatePath)) return null;

        string[] exts = new[] { ".yml", ".yaml" };
        var candidates = new List<string>();

        if (templatePath.StartsWith("/"))
        {
            string relativePath = templatePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            foreach (var basePath in _basePaths)
            {
                candidates.Add(Path.Combine(basePath, relativePath));
            }
        }
        else if (Path.IsPathRooted(templatePath))
        {
            candidates.Add(templatePath);
        }
        else
        {
            candidates.Add(Path.Combine(baseDir, templatePath.Replace('/', Path.DirectorySeparatorChar)));

            string relativePath = templatePath.Replace('/', Path.DirectorySeparatorChar);
            foreach (var basePath in _basePaths)
            {
                candidates.Add(Path.Combine(basePath, relativePath));
            }
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);

            foreach (var ext in exts)
            {
                string withExt = candidate.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? candidate : candidate + ext;
                if (File.Exists(withExt))
                    return Path.GetFullPath(withExt);
            }
        }

        return null;
    }
}

public class JobInfo
{
    public string Name { get; set; } = "";
    public List<string> Tasks { get; set; } = new List<string>();
}
