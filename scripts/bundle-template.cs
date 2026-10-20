// `dotnet run scripts/bundle-template.cs -- <template checkout> [version]`: bundles a checkout
// of coho-template-dotnet into CohoCms.Templates, as the Publish workflow does before it packs.
// The version is the one being released, which the Publish workflow takes from its tag; left
// out, it is Directory.Build.props's.
//
// What must not travel is left behind: a checkout's history, what building leaves, and above
// all a developer's own .env, which holds their token. The library is pinned to this SDK's
// version, CohoCms.Delivery and CohoCms.Codegen both, so a version of CohoCms.Templates always
// creates the same library.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

if (args.Length is not (1 or 2) || !File.Exists(Path.Combine(args[0], ".template.config", "template.json")))
{
    Console.Error.WriteLine("Usage: dotnet run scripts/bundle-template.cs -- <template folder> [version], a checkout of coho-template-dotnet");
    return 1;
}

var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "scripts", ".."));
var from = Path.GetFullPath(args[0]);
var to = Path.Combine(root, "src", "CohoCms.Templates", "content", "coho-library");
var version = args.Length == 2
    ? args[1]
    : Regex.Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;
if (version == "")
{
    Console.Error.WriteLine("Directory.Build.props names no <Version>");
    return 1;
}

HashSet<string> leftBehind = [".git", "bin", "obj", ".env", ".idea", ".vs", "artifacts", "coho-ir.json.tmp"];
if (Directory.Exists(to))
{
    Directory.Delete(to, recursive: true);
}
Copy(new DirectoryInfo(from), to);

// The runtime, by the version central package management names.
var packages = Path.Combine(to, "Directory.Packages.props");
var pinned = Regex.Replace(File.ReadAllText(packages), "(<PackageVersion Include=\"CohoCms\\.Delivery\" Version=\")[^\"]*(\")", $"${{1}}{version}$2");
if (!pinned.Contains($"Version=\"{version}\"", StringComparison.Ordinal))
{
    Console.Error.WriteLine("The template's Directory.Packages.props has no CohoCms.Delivery to pin");
    return 1;
}
File.WriteAllText(packages, pinned);

// The generator, by the version the tool manifest names.
var manifest = Path.Combine(to, ".config", "dotnet-tools.json");
var tools = JsonNode.Parse(File.ReadAllText(manifest))!;
if (tools["tools"]?["cohocms.codegen"] is not JsonObject codegen)
{
    Console.Error.WriteLine("The template's tool manifest has no cohocms.codegen to pin");
    return 1;
}
codegen["version"] = version;
File.WriteAllText(manifest, tools.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, IndentSize = 2 }) + "\n");

Console.WriteLine($"Bundled {from} into {Path.GetRelativePath(root, to)}, on CohoCms {version}.");
return 0;

void Copy(DirectoryInfo source, string target)
{
    Directory.CreateDirectory(target);
    foreach (var file in source.GetFiles().Where(file => !leftBehind.Contains(file.Name)))
    {
        file.CopyTo(Path.Combine(target, file.Name));
    }
    foreach (var directory in source.GetDirectories().Where(directory => !leftBehind.Contains(directory.Name)))
    {
        Copy(directory, Path.Combine(target, directory.Name));
    }
}
