namespace CohoCms.Codegen;

/// <summary>Questions for whoever runs a command, when it runs in a terminal.</summary>
internal interface IPrompt
{
    /// <summary>The answer, or <paramref name="fallback"/> for a blank one.</summary>
    string Ask(string question, string fallback);

    /// <summary>An answer that is not shown as it is typed.</summary>
    string AskSecret(string question);
}

/// <summary>Where a command runs: its directory, its environment, its output, and how it reaches Coho.</summary>
internal sealed record CliContext(
    string WorkingDirectory,
    Func<string, string?> Environment,
    TextWriter Out,
    TextWriter Error,
    HttpMessageInvoker Http,
    IPrompt? Prompt);

internal static class Cli
{
    public const string Usage = """
        Usage: dotnet coho-codegen <command> [--config coho.config.json]

        Commands:
          init      Point this library at a Coho project: asks Coho which account and project
                    a project token is for, and writes them to the config and the token to .env.
                    --url <url>, --token <token> (or COHO_TOKEN), --ref <tag>, -y to ask nothing.
          pull      Fetch the type model for the configured ref into the type model file.
                    Needs COHO_TOKEN, from the environment or a .env beside the config.
                    --ref <ref> pulls another tag or branch than the config names.
          generate  Write C# for the type model into the output directory.
          check     Fail if the output directory differs from what generate would write.
        """;

    public static async Task<int> RunAsync(string[] args, CliContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await RunCommandAsync(args, context, cancellationToken).ConfigureAwait(false);
        }
        catch (CodegenException exception)
        {
            // Something to fix, not a bug: the message says what.
            await context.Error.WriteLineAsync($"error: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }

    private static async Task<int> RunCommandAsync(string[] args, CliContext context, CancellationToken cancellationToken)
    {
        var options = Options.Parse(args);
        if (options.Help)
        {
            await context.Out.WriteLineAsync(Usage).ConfigureAwait(false);
            return 0;
        }
        if (options.Command is null)
        {
            await context.Error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 1;
        }

        var configFile = Path.GetFullPath(options.Config, context.WorkingDirectory);
        string Shown(string path)
        {
            var relative = Path.GetRelativePath(context.WorkingDirectory, path);
            return relative == "" ? "." : relative;
        }

        switch (options.Command)
        {
            case "init":
                return await InitAsync(options, configFile, context, cancellationToken).ConfigureAwait(false);
            case "pull":
            {
                var loaded = Loaded.From(configFile);

                // A .env beside the config may hold the token for local runs. CI sets the variable,
                // which wins: a value already in the environment is never replaced.
                var token = Value(context.Environment("COHO_TOKEN"))
                    ?? Value(DotEnv.Read(Path.Combine(Path.GetDirectoryName(configFile)!, ".env")).GetValueOrDefault("COHO_TOKEN"))
                    ?? throw new CodegenException("COHO_TOKEN is not set: pull needs a project token with read access");
                var config = options.Ref is null ? loaded.Config : loaded.Config with { Ref = options.Ref };
                var (changed, model) = await Files.PullAsync(config, loaded.TypeModel, token, context.Http, cancellationToken).ConfigureAwait(false);
                await context.Out.WriteLineAsync(changed ? $"Updated {Shown(loaded.TypeModel)} from {model.Source.Ref}." : $"{Shown(loaded.TypeModel)} is up to date.").ConfigureAwait(false);
                return 0;
            }
            case "generate":
            {
                var loaded = Loaded.From(configFile);
                var generated = Generator.Generate(Files.ReadTypeModel(loaded.TypeModel), loaded.Namespace());
                Files.Write(generated.Files, loaded.OutDir);
                foreach (var warning in generated.Warnings)
                {
                    await context.Error.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
                }
                var count = generated.Files.Count;
                await context.Out.WriteLineAsync($"Wrote {count} {(count == 1 ? "file" : "files")} to {Shown(loaded.OutDir)}.").ConfigureAwait(false);
                return 0;
            }
            case "check":
            {
                var loaded = Loaded.From(configFile);
                var problems = Files.Drift(Generator.Generate(Files.ReadTypeModel(loaded.TypeModel), loaded.Namespace()).Files, loaded.OutDir);
                if (problems.Count == 0)
                {
                    await context.Out.WriteLineAsync("Generated code is up to date.").ConfigureAwait(false);
                    return 0;
                }
                foreach (var problem in problems)
                {
                    await context.Error.WriteLineAsync($"{Shown(loaded.OutDir)}: {problem}").ConfigureAwait(false);
                }
                await context.Error.WriteLineAsync("Generated code is out of date: run dotnet coho-codegen generate.").ConfigureAwait(false);
                return 1;
            }
            default:
                await context.Error.WriteLineAsync($"Unknown command '{options.Command}'.\n\n{Usage}").ConfigureAwait(false);
                return 1;
        }
    }

    /// <summary>
    /// Asks for what was not given, in a terminal, and checks the token with Coho before
    /// anything is written: a token Coho refuses leaves the library as it was.
    /// </summary>
    private static async Task<int> InitAsync(Options options, string configFile, CliContext context, CancellationToken cancellationToken)
    {
        var prompt = options.Yes ? null : context.Prompt;
        var existing = File.Exists(configFile) ? Loaded.From(configFile).Config : new CodegenConfig();

        var url = options.Url ?? prompt?.Ask("Coho URL, as `coho status` shows it", existing.Url ?? "") ?? existing.Url ?? "";

        // COHO_TOKEN only with a URL to ask: the coho CLI reads the same variable, so it may be set for that.
        var token = options.Token
            ?? (url == "" ? null : Value(context.Environment("COHO_TOKEN")))
            ?? (url == "" ? null : Value(prompt?.AskSecret("Project token, coho_pt_… (blank to set it later)")))
            ?? "";
        if (token != "" && url == "")
        {
            throw new CodegenException("A token needs the Coho URL to ask: pass --url");
        }
        var described = token == "" ? null : await Init.DescribeTokenAsync(url, token, context.Http, cancellationToken).ConfigureAwait(false);
        var @ref = options.Ref ?? prompt?.Ask("Tag to generate from", existing.Ref ?? "") ?? "";

        Init.EditConfig(configFile, new Dictionary<string, string?>
        {
            ["url"] = url,
            ["account"] = described?.Account.Id,
            ["project"] = described?.Project.Id,
            ["ref"] = @ref,
        });
        var directory = Path.GetDirectoryName(configFile)!;
        if (token != "")
        {
            DotEnv.Write(Path.Combine(directory, ".env"), Path.Combine(directory, ".env.example"), "COHO_TOKEN", token);
        }

        await context.Out.WriteLineAsync($"Updated {Path.GetFileName(configFile)}.").ConfigureAwait(false);
        if (described is not null)
        {
            await context.Out.WriteLineAsync($"It reads {described.Project.Name ?? described.Project.Id}, in {described.Account.Name ?? described.Account.Id}, with the token in .env.").ConfigureAwait(false);

            // The library only reads types; a token that can do more is more to lose if it leaks.
            if (described.Token.Role != "viewer")
            {
                await context.Out.WriteLineAsync($"That token is a {described.Token.Role}. The library only reads, so a viewer token is enough: coho token create --role viewer").ConfigureAwait(false);
            }
        }

        var config = Loaded.From(configFile).Config;
        var unset = new[] { ("url", config.Url), ("account", config.Account), ("project", config.Project), ("ref", config.Ref) }
            .Where(each => string.IsNullOrEmpty(each.Item2))
            .Select(each => each.Item1)
            .ToList();
        await context.Out.WriteLineAsync("\nNext:").ConfigureAwait(false);
        if (unset.Count > 0)
        {
            await context.Out.WriteLineAsync($"  set {string.Join(", ", unset)} in {Path.GetFileName(configFile)}").ConfigureAwait(false);
        }
        if (token == "")
        {
            await context.Out.WriteLineAsync("  cp .env.example .env, and set COHO_TOKEN: a project token with read access").ConfigureAwait(false);
        }
        await context.Out.WriteLineAsync("  dotnet coho-codegen pull").ConfigureAwait(false);
        await context.Out.WriteLineAsync("  dotnet coho-codegen generate").ConfigureAwait(false);
        return 0;
    }

    /// <summary>An empty value, such as <c>COHO_TOKEN=</c>, counts as unset.</summary>
    private static string? Value(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Options(string? Command, string Config, string? Ref, string? Url, string? Token, bool Yes, bool Help)
    {
        public static Options Parse(string[] args)
        {
            string? command = null;
            var named = new Dictionary<string, string>(StringComparer.Ordinal);
            var yes = false;
            var help = false;
            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "-h" or "--help":
                        help = true;
                        break;
                    case "-y" or "--yes":
                        yes = true;
                        break;
                    case "-c" or "--config" or "--ref" or "--url" or "--token":
                        if (i + 1 >= args.Length)
                        {
                            throw new CodegenException($"{arg} needs a value");
                        }
                        named[arg == "-c" ? "--config" : arg] = args[++i];
                        break;
                    default:
                        if (arg.StartsWith('-'))
                        {
                            throw new CodegenException($"Unknown option '{arg}'. See dotnet coho-codegen --help.");
                        }
                        if (command is not null)
                        {
                            throw new CodegenException($"One command at a time: '{command}', then '{arg}'");
                        }
                        command = arg;
                        break;
                }
            }
            return new Options(
                command,
                named.GetValueOrDefault("--config") ?? "coho.config.json",
                named.GetValueOrDefault("--ref"),
                named.GetValueOrDefault("--url"),
                named.GetValueOrDefault("--token"),
                yes,
                help);
        }
    }
}
