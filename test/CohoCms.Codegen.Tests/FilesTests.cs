using System.Net;
using System.Security.Cryptography;
using System.Text;
using CohoCms.Delivery.Tests;
using static CohoCms.Codegen.Tests.Fixtures;

namespace CohoCms.Codegen.Tests;

public sealed class FilesTests
{
    private static readonly CancellationToken None = TestContext.Current.CancellationToken;
    private static readonly byte[] Blog = File.ReadAllBytes(ModelFile("cli-next-blog"));
    private static readonly CodegenConfig Config = new() { Url = "https://app.test/", Account = "acme", Project = "p1", Ref = "release/1" };

    private static HttpMessageInvoker Server(Answering answering) => new(answering);

    /* PULL */
    /* PULL */
    /* PULL */

    [Fact]
    public async Task Pull_asks_for_the_refs_type_model_with_the_token_and_writes_it_as_it_arrived()
    {
        var path = Path.Combine(Temp(), "coho-ir.json");
        var server = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Blog) });

        var (changed, _) = await Files.PullAsync(Config, path, "coho_pt_x", Server(server), None);

        Assert.Equal("https://app.test/api/v1/accounts/acme/projects/p1/refs/release%2F1/type-model", server.Calls[0].Url);
        Assert.Equal("Bearer coho_pt_x", server.Calls[0].Header("authorization"));
        Assert.Null(server.Calls[0].Header("if-none-match"));
        Assert.True(changed);
        Assert.Equal(Blog, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Pull_asks_whether_the_model_changed_with_the_files_hash_and_a_304_leaves_it_alone()
    {
        var path = Path.Combine(Temp(), "coho-ir.json");
        File.WriteAllBytes(path, Blog);
        var server = new Answering(_ => new HttpResponseMessage(HttpStatusCode.NotModified));

        var (changed, _) = await Files.PullAsync(Config, path, "t", Server(server), None);

        Assert.Equal($"\"{Convert.ToHexStringLower(SHA256.HashData(Blog))}\"", server.Calls[0].Header("if-none-match"));
        Assert.False(changed);
        Assert.Equal(Blog, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task A_refused_pull_says_why_and_points_at_the_token()
    {
        var problem = """{ "code": "FORBIDDEN", "title": "Forbidden", "status": 403, "detail": "Requires 'content:read'" }""";
        var server = new Answering(_ => Answering.Json(problem, HttpStatusCode.Forbidden, "application/problem+json"));

        var refused = await Assert.ThrowsAsync<CodegenException>(() => Files.PullAsync(Config, Path.Combine(Temp(), "coho-ir.json"), "t", Server(server), None));

        Assert.Contains("403 (FORBIDDEN: Requires 'content:read'). Check COHO_TOKEN", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_model_this_generator_cannot_read_is_refused_before_anything_is_written()
    {
        var path = Path.Combine(Temp(), "coho-ir.json");
        File.WriteAllBytes(path, Blog);
        var server = new Answering(_ => Answering.Json("""{ "irVersion": "2.0", "source": {}, "types": {} }"""));

        await Assert.ThrowsAsync<CodegenException>(() => Files.PullAsync(Config, path, "t", Server(server), None));

        Assert.Equal(Blog, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Pull_needs_the_whole_address_in_the_config()
    {
        var refused = await Assert.ThrowsAsync<CodegenException>(() => Files.PullAsync(new CodegenConfig { Project = "p1" }, Path.Combine(Temp(), "m.json"), "t", Server(Answering.Ok()), None));

        Assert.Contains("needs url, account, project and ref", refused.Message, StringComparison.Ordinal);
    }

    /* GENERATED FILES */
    /* GENERATED FILES */
    /* GENERATED FILES */

    [Fact]
    public void Drift_is_empty_for_what_was_written_and_names_each_kind_of_difference()
    {
        var output = Path.Combine(Temp(), "Coho");
        GeneratedFile[] files = [new("Coho.cs", "class Coho;\n")];

        Files.Write(files, output);
        Assert.Empty(Files.Drift(files, output));

        File.WriteAllText(Path.Combine(output, "Coho.cs"), "class Edited;\n");
        File.WriteAllText(Path.Combine(output, "Extra.cs"), "");
        Assert.Equal(
            ["Coho.cs differs from what the type model generates", "Extra.cs is in the output directory, but nothing generates it"],
            Files.Drift(files, output).Order(StringComparer.Ordinal));

        Assert.Equal(["Coho.cs is missing"], Files.Drift(files, Path.Combine(Temp(), "nowhere")));
    }

    [Fact]
    public void Write_removes_the_files_an_earlier_run_generated_and_nothing_else()
    {
        var output = Path.Combine(Temp(), "Coho");
        var header = Generator.GeneratedMark + " Do not edit.\n";
        Files.Write([new("Coho.cs", header), new("Gone.cs", header)], output);
        File.WriteAllText(Path.Combine(output, "Notes.md"), "mine\n");

        Files.Write([new("Coho.cs", header)], output);

        Assert.False(File.Exists(Path.Combine(output, "Gone.cs")));
        Assert.Equal("mine\n", File.ReadAllText(Path.Combine(output, "Notes.md")));
    }

    [Fact]
    public void A_missing_type_model_says_to_pull_it()
    {
        var missing = Assert.Throws<CodegenException>(() => Files.ReadTypeModel(Path.Combine(Temp(), "coho-ir.json")));

        Assert.Contains("run coho-codegen pull", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_configs_paths_are_relative_to_it_with_defaults()
    {
        var dir = Temp();
        Directory.CreateDirectory(Path.Combine(dir, "lib"));
        File.WriteAllText(Path.Combine(dir, "lib", "coho.config.json"), """{ "project": "p1", "outDir": "Generated" }""");

        var loaded = Loaded.From(Path.Combine(dir, "lib", "coho.config.json"));

        Assert.Equal(Path.Combine(dir, "lib", "coho-ir.json"), loaded.TypeModel);
        Assert.Equal(Path.Combine(dir, "lib", "Generated"), loaded.OutDir);
        var bare = Temp();
        Assert.Equal(Path.Combine(bare, "Coho"), Loaded.From(WriteConfig(bare, "{}")).OutDir);
    }

    [Fact]
    public void The_namespace_is_the_configs_or_the_name_of_the_project_beside_it()
    {
        var named = Loaded.From(WriteConfig(Temp(), """{ "namespace": "Acme.Content" }"""));
        Assert.Equal("Acme.Content", named.Namespace());

        var dir = Temp();
        File.WriteAllText(Path.Combine(dir, "Acme.Site.Content.csproj"), "<Project />");
        Assert.Equal("Acme.Site.Content", Loaded.From(WriteConfig(dir, "{}")).Namespace());

        var neither = Assert.Throws<CodegenException>(() => Loaded.From(WriteConfig(Temp(), "{}")).Namespace());
        Assert.Contains("set \"namespace\"", neither.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dotenv_file_is_read_as_the_TypeScript_generator_reads_it()
    {
        var path = Path.Combine(Temp(), ".env");
        File.WriteAllText(path, "# a comment\nCOHO_TOKEN=\"coho_pt_quoted\"\nexport OTHER = plain \n\nBROKEN\n", Encoding.UTF8);

        var values = DotEnv.Read(path);

        Assert.Equal("coho_pt_quoted", values["COHO_TOKEN"]);
        Assert.Equal("plain", values["OTHER"]);
        Assert.Equal(2, values.Count);
    }

    private static string WriteConfig(string dir, string json)
    {
        var path = Path.Combine(dir, "coho.config.json");
        File.WriteAllText(path, json);
        return path;
    }
}
