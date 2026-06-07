using System.Diagnostics;
using System.Text;

namespace PulsationEventManager;

public sealed class GitPublisher
{
    public async Task<GitPublishResult> PublishAsync(string siteRoot, IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        var result = new GitPublishResult();
        var paths = relativePaths
            .Select(path => path.Replace('\\', '/').Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            result.Messages.Add("Aucun fichier à publier.");
            return result;
        }

        var status = await RunGitAsync(siteRoot, ["status", "--porcelain", "--", .. paths], cancellationToken);
        result.Messages.Add(status.Summary);

        if (string.IsNullOrWhiteSpace(status.Output))
        {
            result.Messages.Add("Aucun changement détecté dans les fichiers d'événements.");
            result.Success = true;
            return result;
        }

        var add = await RunGitAsync(siteRoot, ["add", "--", .. paths], cancellationToken);
        result.Messages.Add(add.Summary);
        if (add.ExitCode != 0)
        {
            return result;
        }

        var diff = await RunGitAsync(siteRoot, ["diff", "--cached", "--quiet"], cancellationToken);
        if (diff.ExitCode == 0)
        {
            result.Messages.Add("Aucun changement stagé après git add.");
            result.Success = true;
            return result;
        }

        if (diff.ExitCode > 1)
        {
            result.Messages.Add(diff.Summary);
            return result;
        }

        var commit = await RunGitAsync(siteRoot, ["commit", "-m", "Mettre a jour les evenements Facebook"], cancellationToken);
        result.Messages.Add(commit.Summary);
        if (commit.ExitCode != 0)
        {
            return result;
        }

        var branch = await RunGitAsync(siteRoot, ["branch", "--show-current"], cancellationToken);
        var branchName = string.IsNullOrWhiteSpace(branch.Output) ? "main" : branch.Output.Trim();
        var push = await RunGitAsync(siteRoot, ["push", "origin", branchName], cancellationToken);
        result.Messages.Add(push.Summary);
        result.Success = push.ExitCode == 0;
        return result;
    }

    private static async Task<ProcessResult> RunGitAsync(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                error.AppendLine(args.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(process.ExitCode, output.ToString().Trim(), error.ToString().Trim(), $"git {string.Join(" ", arguments)}");
    }

    private sealed record ProcessResult(int ExitCode, string Output, string Error, string Command)
    {
        public string Summary
        {
            get
            {
                var details = string.Join(Environment.NewLine, new[] { Output, Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
                return string.IsNullOrWhiteSpace(details)
                    ? $"{Command}: code {ExitCode}"
                    : $"{Command}: code {ExitCode}{Environment.NewLine}{details}";
            }
        }
    }
}

public sealed class GitPublishResult
{
    public bool Success { get; set; }
    public List<string> Messages { get; } = [];
}
