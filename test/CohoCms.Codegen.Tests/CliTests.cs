using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CohoCms.Delivery.Tests;
using static CohoCms.Codegen.Tests.Fixtures;

namespace CohoCms.Codegen.Tests;

public sealed class CliTests
{
    private const string Token = "coho_pt_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private static readonly CancellationToken None = TestContext.Current.CancellationToken;

    /// <summary>Answers each question from a script, and records what was asked.</summary>
    private sealed class Scripted(params string[] answers) : IPrompt
    {
        private readonly Queue<string> _answers = new(answers);

        public List<string> Asked { get; } = [];

        public string Ask(string question, string fallback)
        {
            Asked.Add(question);
            var answer = _answers.Count > 0 ? _answers.Dequeue() : "";
            return answer == "" ? fallback : answer;
        }

        public string AskSecret(string question)
        {
            Asked.Add(question);
            return _answers.Count > 0 ? _answers.Dequeue() : "";
        }
    }

    private sealed record Run(int Status, string Out, string Error);

    /// <summary>Runs the CLI in <paramref name="dir"/>, with only <paramref name="env"/> for an environment.</summary>
    private static async Task<Run> RunAsync(string dir, string[] args, Dictionary<string, string>? env = null, HttpMessageHandler? server = null, IPrompt? prompt = null)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var http = new HttpMessageInvoker(server ?? new Answering(request => throw new InvalidOperationException($"Nothing should be asked: {request.RequestUri}")));
        var context = new CliContext(dir, name => env?.GetValueOrDefault(name), output, error, http, prompt);
        var status = await Cli.RunAsync(args, context, None);
        return new Run(status, output.ToString(), error.ToString());
    }

    /// <summary>A library's directory: a config, a project and a pulled type model.</summary>
    private static string Library(string config = """{ "project": "p1", "ref": "v1.0.0" }""")
    {
        var dir = Temp();
        File.WriteAllText(Path.Combine(dir, "coho.config.json"), config);
        File.WriteAllText(Path.Combine(dir, "Golden.CliNextBlog.csproj"), "<Project />");
        File.Copy(ModelFile("cli-next-blog"), Path.Combine(dir, "coho-ir.json"));
        return dir;
    }

    [Fact]
    public async Task Generate_writes_the_files_and_check_passes_until_one_is_edited()
    {
        var dir = Library();
        var output = Path.Combine(dir, "Coho");

        var generated = await RunAsync(dir, ["generate"]);
        Assert.Equal(0, generated.Status);
        var expected = Expected("cli-next-blog");
        Assert.Equal(Directory.GetFiles(expected).Select(Path.GetFileName).Order(), Directory.GetFiles(output).Select(Path.GetFileName).Order());
        foreach (var file in Directory.GetFiles(expected))
        {
            Assert.Equal(File.ReadAllText(file), File.ReadAllText(Path.Combine(output, Path.GetFileName(file))));
        }
        Assert.Equal(0, (await RunAsync(dir, ["check"])).Status);

        File.AppendAllText(Path.Combine(output, "Coho.cs"), "// edited\n");
        var checkedAgain = await RunAsync(dir, ["check"]);
        Assert.Equal(1, checkedAgain.Status);
        Assert.Contains("Coho.cs differs", checkedAgain.Error, StringComparison.Ordinal);
        Assert.Contains("run dotnet coho-codegen generate", checkedAgain.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pull_without_COHO_TOKEN_is_refused_before_anything_is_asked()
    {
        var pulled = await RunAsync(Library(), ["pull"]);

        Assert.Equal(1, pulled.Status);
        Assert.Contains("COHO_TOKEN is not set", pulled.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pull_takes_the_token_from_a_dotenv_beside_the_config_when_the_environment_has_none()
    {
        var dir = Library();
        File.WriteAllText(Path.Combine(dir, ".env"), "COHO_TOKEN=coho_pt_from_file\n");

        // Past the token check, and stopped by the config, which names no server.
        var pulled = await RunAsync(dir, ["pull"]);

        Assert.Equal(1, pulled.Status);
        Assert.Contains("pull needs url, account, project and ref", pulled.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ref_pulls_another_ref_than_the_config_names()
    {
        var dir = Library("""{ "url": "https://app.test", "account": "a", "project": "p1", "ref": "v1.0.0" }""");
        var server = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(File.ReadAllText(ModelFile("cli-next-blog"))) });

        var pulled = await RunAsync(dir, ["pull", "--ref", "v2.0.0"], new() { ["COHO_TOKEN"] = "t" }, server);

        Assert.Equal(0, pulled.Status);
        Assert.Equal(["https://app.test/api/v1/accounts/a/projects/p1/refs/v2.0.0/type-model"], server.Calls.Select(call => call.Url));
    }

    [Fact]
    public async Task No_command_is_a_usage_error_and_help_is_not()
    {
        var dir = Library();

        Assert.Equal(1, (await RunAsync(dir, [])).Status);
        var help = await RunAsync(dir, ["--help"]);
        Assert.Equal(0, help.Status);
        Assert.Contains("Usage: dotnet coho-codegen <command>", help.Out, StringComparison.Ordinal);
        Assert.Equal(1, (await RunAsync(dir, ["generate", "--nope"])).Status);
    }

    /* INIT */
    /* INIT */
    /* INIT */

    private static Answering Describing(string role = "viewer")
    {
        var description = new JsonObject
        {
            ["account"] = new JsonObject { ["id"] = "acct-1", ["name"] = "Acme" },
            ["project"] = new JsonObject { ["id"] = "proj-1", ["name"] = "Marketing site" },
            ["token"] = new JsonObject { ["id"] = "tok-1", ["label"] = "acme-content library", ["role"] = role, ["expiresAt"] = "2027-01-01T00:00:00Z" },
        };
        return new Answering(_ => Answering.Json(description.ToJsonString()));
    }

    private static JsonObject Config(string dir) => JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "coho.config.json")))!.AsObject();

    [Fact]
    public async Task Init_asks_Coho_which_account_and_project_the_token_is_for_and_writes_them()
    {
        var dir = Library("""{ "url": "https://app.coho.example", "account": "your-account-id", "project": "your-project-id", "ref": "v1.0.0", "outDir": "Coho" }""");
        File.WriteAllText(Path.Combine(dir, ".env.example"), "# For pull only.\nCOHO_TOKEN=\n");
        var server = Describing();

        var init = await RunAsync(dir, ["init", "--url", "https://coho.test/", "--token", Token, "--ref", "v1.4.0"], server: server);

        Assert.Equal(0, init.Status);
        Assert.Equal("https://coho.test/api/v1/token", server.Calls[0].Url);
        Assert.Equal($"Bearer {Token}", server.Calls[0].Header("authorization"));
        var config = Config(dir);
        Assert.Equal(("https://coho.test/", "acct-1", "proj-1", "v1.4.0", "Coho"), ((string?)config["url"], (string?)config["account"], (string?)config["project"], (string?)config["ref"], (string?)config["outDir"]));
        Assert.Equal($"# For pull only.\nCOHO_TOKEN={Token}\n", File.ReadAllText(Path.Combine(dir, ".env")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(dir, ".env")));
        }
        Assert.Contains("It reads Marketing site, in Acme", init.Out, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, init.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Init_asks_in_a_terminal_for_what_was_not_given()
    {
        var dir = Library();
        var prompt = new Scripted("https://coho.test", Token, "v2.0.0");

        var init = await RunAsync(dir, ["init"], server: Describing("editor"), prompt: prompt);

        Assert.Equal(0, init.Status);
        Assert.Equal(3, prompt.Asked.Count);
        Assert.Equal("v2.0.0", (string?)Config(dir)["ref"]);
        Assert.Contains("a viewer token is enough", init.Out, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_token_Coho_refuses_leaves_the_library_as_it_was()
    {
        var dir = Library();
        var before = File.ReadAllText(Path.Combine(dir, "coho.config.json"));
        var server = new Answering(_ => Answering.Json(
            JsonSerializer.Serialize(new { code = "UNAUTHENTICATED", title = "Unauthenticated", status = 401 }),
            HttpStatusCode.Unauthorized,
            "application/problem+json"));

        var init = await RunAsync(dir, ["init", "--url", "https://coho.test", "--token", Token, "-y"], server: server);

        Assert.Equal(1, init.Status);
        Assert.Contains("Coho refused the token", init.Error, StringComparison.Ordinal);
        Assert.Contains("not revoked or expired", init.Error, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Path.Combine(dir, "coho.config.json")));
        Assert.False(File.Exists(Path.Combine(dir, ".env")));
    }

    [Fact]
    public async Task Init_refuses_what_is_not_a_project_token_without_asking_Coho()
    {
        var init = await RunAsync(Library(), ["init", "--url", "https://coho.test", "--token", "eyJhbGciOi", "-y"]);

        Assert.Equal(1, init.Status);
        Assert.Contains("A project token starts coho_pt_", init.Error, StringComparison.Ordinal);
    }
}
