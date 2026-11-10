using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace EditWebView
{
    /// <summary>
    /// Says what a run changed: the upstream versions it used, the files that now differ from the last commit in each
    /// regenerated folder, and what happened to the Web.JS patches. The whole diff goes to a file beside the solution.
    /// </summary>
    /// <remarks>
    /// Uses the git command line, not LibGit2Sharp, so the numbers are the ones 'git status' and 'git diff' show
    /// (line-ending settings included).
    /// </remarks>
    public static class ChangeReport
    {
        public const string DiffFileName = "EditWebView.diff";
        private const int NamesShownPerArea = 20;

        /// <summary>The folders a run regenerates, as paths from the repository root.</summary>
        public static readonly (string Name, string Path)[] Areas =
        [
            ("WinForms", "src/RemoteBlazorWebView.WinForms"),
            ("Wpf", "src/RemoteBlazorWebView.Wpf"),
            ("SharedSource", "src/SharedSource"),
            ("Web.JS", "src/RemoteWebView.Blazor.JS/Web.JS"),
        ];

        public enum Kind { Added, Modified, Deleted }

        /// <param name="Ignored">A new file that a .gitignore rule hides, so 'git add' and 'git status' pass over it.</param>
        public sealed record Change(string Path, Kind Kind, int LinesAdded, int LinesRemoved, bool Binary, bool Ignored = false);

        /// <summary>Results of this repository's own changes to the upstream source, in the order they ran. Filled in by Program and Editor.</summary>
        public static List<(string File, string Result, bool Problem)> Patches { get; } = [];

        /// <summary>Paths in the regenerated folders that already differ from the last commit. Call before the run changes anything.</summary>
        public static HashSet<string> ChangedBefore(string repoPath)
        {
            try
            {
                return [.. Changes(repoPath).Select(c => c.Path)];
            }
            catch (Exception)
            {
                return [];
            }
        }

        /// <summary>Prints the summary and writes the diff file. Returns the number of files that differ from the last commit.</summary>
        public static int Print(string repoPath, string aspNetCoreTag, string mauiTag, IReadOnlySet<string> changedBefore)
        {
            Console.WriteLine();
            Heading("EditWebView summary");

            PrintVersions(repoPath, aspNetCoreTag, mauiTag);
            PrintPatches();

            List<Change> changes;
            try
            {
                changes = Changes(repoPath);
            }
            catch (Exception ex)
            {
                Warn($"Could not read the changes from git: {ex.Message}");
                Warn("Run 'git status' and 'git diff' in the repository to see them.");
                return -1;
            }

            Console.WriteLine();
            Console.WriteLine("Changes since the last commit:");
            foreach (var (name, path) in Areas)
            {
                var inArea = changes.Where(c => c.Path.StartsWith(path + "/", StringComparison.Ordinal)).ToList();
                PrintArea(name, path, inArea);
            }

            int alreadyChanged = changes.Count(c => changedBefore.Contains(c.Path));
            if (alreadyChanged > 0)
            {
                Console.WriteLine();
                Warn($"{alreadyChanged} of these files already differed from the last commit before this run, so not every change above is from upstream.");
            }

            Console.WriteLine();
            if (changes.Count == 0)
            {
                Good("No changes: the upstream source gives exactly what is committed.");
            }
            else
            {
                string diffFile = Path.Combine(repoPath, DiffFileName);
                try
                {
                    File.WriteAllText(diffFile, FullDiff(repoPath, changes), new UTF8Encoding(false));
                    Console.WriteLine($"Full diff: {diffFile}");
                }
                catch (Exception ex)
                {
                    Warn($"Could not write {diffFile}: {ex.Message}");
                }

                Warn($"{changes.Count} file(s) changed: review, then rebuild and run the tests before committing.");
            }

            return changes.Count;
        }

        private static void PrintVersions(string repoPath, string aspNetCoreTag, string mauiTag)
        {
            string? aspNetCoreNow = PackageVersion(repoPath, "src/RemoteBlazorWebView/RemoteBlazorWebView.csproj", "Microsoft.AspNetCore.Components.Web");
            string? mauiNow = PackageVersion(repoPath, "src/RemoteBlazorWebView.Wpf/RemoteBlazorWebView.Wpf.csproj", "Microsoft.AspNetCore.Components.WebView.Wpf");

            Console.WriteLine("Upstream versions used:");
            PrintVersion("aspnetcore", aspNetCoreTag.TrimStart('v'), aspNetCoreNow,
                "the Microsoft.AspNetCore.* package references need the same version.");
            PrintVersion("maui", mauiTag, mauiNow,
                "update Microsoft.AspNetCore.Components.WebView.Wpf and .WindowsForms, and the \"Based on maui ...\" text in the package release notes.");
        }

        private static void PrintVersion(string name, string upstream, string? referenced, string whatToDo)
        {
            if (referenced == null)
            {
                Console.WriteLine($"  {name,-11} {upstream}   (could not read the version the packages reference)");
            }
            else if (referenced == upstream)
            {
                Console.WriteLine($"  {name,-11} {upstream}   (same as the packages reference)");
            }
            else
            {
                Warn($"  {name,-11} {upstream}   the packages reference {referenced}: {whatToDo}");
            }
        }

        private static void PrintPatches()
        {
            if (Patches.Count == 0) return;

            Console.WriteLine();
            Console.WriteLine("Changes of this repository's own, made again on the upstream source:");
            foreach (var (file, result, problem) in Patches)
            {
                if (problem) Warn($"  {file}: {result}");
                else Console.WriteLine($"  {file}: {result}");
            }
        }

        private static void PrintArea(string name, string areaPath, List<Change> changes)
        {
            if (changes.Count == 0)
            {
                Console.WriteLine($"  {name}: no changes");
                return;
            }

            int added = changes.Count(c => c.Kind == Kind.Added);
            int modified = changes.Count(c => c.Kind == Kind.Modified);
            int deleted = changes.Count(c => c.Kind == Kind.Deleted);
            Console.WriteLine($"  {name}: {modified} modified, {added} added, {deleted} deleted");

            foreach (var change in changes.Take(NamesShownPerArea))
            {
                string letter = change.Kind switch { Kind.Added => "A", Kind.Deleted => "D", _ => "M" };
                string lines = change.Binary ? "(binary)" : $"(+{change.LinesAdded} -{change.LinesRemoved})";
                Console.WriteLine($"    {letter}  {change.Path[(areaPath.Length + 1)..]} {lines}{(change.Ignored ? "  [git-ignored]" : "")}");
            }

            if (changes.Count > NamesShownPerArea)
            {
                Console.WriteLine($"    ...and {changes.Count - NamesShownPerArea} more (all of them are in {DiffFileName})");
            }

            var hidden = changes.Where(c => c.Ignored).ToList();
            if (hidden.Count > 0)
            {
                Warn($"    {hidden.Count} new file(s) here are hidden by .gitignore, so 'git add .' will skip them and the build elsewhere will miss them. Add them with:");
                Warn($"      git add -f {string.Join(' ', hidden.Select(c => '"' + c.Path + '"'))}");
            }
        }

        /// <summary>Every file in the regenerated folders that differs from the last commit, new files included.</summary>
        public static List<Change> Changes(string repoPath)
        {
            string[] paths = [.. Areas.Select(a => a.Path)];

            // "XY path", NUL-terminated. Without renames, so a moved file is one deleted and one added.
            var kinds = new SortedDictionary<string, Kind>(StringComparer.Ordinal);
            var ignored = new HashSet<string>(StringComparer.Ordinal);
            foreach (string entry in Git(repoPath, ["status", "--porcelain", "-z", "--untracked-files=all", "--ignored=matching", "--no-renames", "--", .. paths])
                .Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (entry.Length < 4) continue;
                if (entry.Contains("/node_modules/", StringComparison.Ordinal)) continue; // installed packages, never upstream source
                string status = entry[..2];
                if (status == "!!")
                {
                    // Web.JS/src and Web.JS/test are git-ignored, yet their files are committed. A file upstream adds there
                    // is one the build needs and git will not offer, so it is reported. Build output stays out.
                    if (!IsIgnoredUpstreamFile(entry[3..])) continue;
                    ignored.Add(entry[3..]);
                    status = "??";
                }

                kinds[entry[3..]] = status == "??" || status.Contains('A') ? Kind.Added
                    : status.Contains('D') ? Kind.Deleted
                    : Kind.Modified;
            }

            // "added<TAB>removed<TAB>path", NUL-terminated; "-" for both counts on a binary file. New files are not in it.
            var lines = new Dictionary<string, (int Added, int Removed, bool Binary)>(StringComparer.Ordinal);
            foreach (string entry in Git(repoPath, ["diff", "HEAD", "--numstat", "-z", "--no-renames", "--", .. paths])
                .Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split('\t', 3);
                if (parts.Length != 3) continue;
                bool binary = parts[0] == "-";
                lines[parts[2]] = binary ? (0, 0, true) : (int.Parse(parts[0]), int.Parse(parts[1]), false);
            }

            var changes = new List<Change>();
            foreach (var (path, kind) in kinds)
            {
                if (lines.TryGetValue(path, out var count))
                {
                    changes.Add(new Change(path, kind, count.Added, count.Removed, count.Binary));
                }
                else if (kind == Kind.Added)
                {
                    var (lineCount, binary) = CountLines(Path.Combine(repoPath, path));
                    changes.Add(new Change(path, kind, lineCount, 0, binary, ignored.Contains(path)));
                }
                else
                {
                    // In the status but with no line changes: a difference git does not count (file mode, for one).
                    changes.Add(new Change(path, kind, 0, 0, false));
                }
            }

            return changes;
        }

        private static bool IsIgnoredUpstreamFile(string path)
        {
            const string webJS = "src/RemoteWebView.Blazor.JS/Web.JS/";
            return (path.StartsWith(webJS + "src/", StringComparison.Ordinal) || path.StartsWith(webJS + "test/", StringComparison.Ordinal))
                && !path.EndsWith('/');
        }

        private static string FullDiff(string repoPath, List<Change> changes)
        {
            string[] paths = [.. Areas.Select(a => a.Path)];
            var diff = new StringBuilder(Git(repoPath, ["diff", "HEAD", "--no-renames", "--", .. paths]));

            // 'git diff HEAD' leaves out files git has never seen; show those as additions too.
            var tracked = new HashSet<string>(
                Git(repoPath, ["ls-files", "-z", "--", .. paths]).Split('\0', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            foreach (var change in changes.Where(c => c.Kind == Kind.Added && !tracked.Contains(c.Path)))
            {
                // Exit code 1 only says the two differ, which they always do here.
                diff.Append(Git(repoPath, ["diff", "--no-index", "--", "/dev/null", change.Path], okExitCodes: [0, 1]));
            }

            return diff.ToString();
        }

        private static (int Lines, bool Binary) CountLines(string file)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                if (Array.IndexOf(bytes, (byte)0) >= 0) return (0, true);
                if (bytes.Length == 0) return (0, false);

                int lines = bytes.Count(b => b == (byte)'\n');
                return (bytes[^1] == (byte)'\n' ? lines : lines + 1, false);
            }
            catch (Exception)
            {
                return (0, false);
            }
        }

        private static string? PackageVersion(string repoPath, string project, string package)
        {
            try
            {
                string text = File.ReadAllText(Path.Combine(repoPath, project));
                var match = Regex.Match(text, $"Include=\"{Regex.Escape(package)}\"\\s+Version=\"([^\"]+)\"");
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The tag in an archive address such as https://github.com/dotnet/maui/archive/refs/tags/10.0.120.zip.</summary>
        public static string TagFromUrl(string url)
        {
            string name = url[(url.LastIndexOf('/') + 1)..];
            return name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }

        /// <summary>What a git command writes, byte for byte (a file's content from 'git show').</summary>
        public static byte[] GitBytes(string repoPath, string[] arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);

            using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
            var errors = git.StandardError.ReadToEndAsync();
            using var output = new MemoryStream();
            git.StandardOutput.BaseStream.CopyTo(output);
            git.WaitForExit();

            if (git.ExitCode != 0)
            {
                throw new InvalidOperationException($"git {arguments[0]} failed ({git.ExitCode}): {errors.Result.Trim()}");
            }

            return output.ToArray();
        }

        private static string Git(string repoPath, string[] arguments, int[]? okExitCodes = null)
        {
            var start = new ProcessStartInfo("git")
            {
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Paths as they are, not quoted and escaped, and no pager or colour in the output.
            foreach (string argument in (string[])["-c", "core.quotepath=false", "-c", "color.ui=false", "--no-pager", .. arguments])
            {
                start.ArgumentList.Add(argument);
            }

            using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start.");
            var output = git.StandardOutput.ReadToEndAsync();
            var errors = git.StandardError.ReadToEndAsync();
            git.WaitForExit();

            if (!(okExitCodes ?? [0]).Contains(git.ExitCode))
            {
                throw new InvalidOperationException($"git {arguments[0]} failed ({git.ExitCode}): {errors.Result.Trim()}");
            }

            return output.Result;
        }

        private static void Heading(string text)
        {
            Console.WriteLine(text);
            Console.WriteLine(new string('=', text.Length));
        }

        private static void Warn(string text) => Colored(ConsoleColor.Yellow, text);

        private static void Good(string text) => Colored(ConsoleColor.Green, text);

        private static void Colored(ConsoleColor color, string text)
        {
            var before = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = before;
        }
    }
}
