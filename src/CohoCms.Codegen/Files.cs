using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CohoCms.Codegen;

internal static class Files
{
    /// <summary>The type model in <paramref name="path"/>, read and checked.</summary>
    public static TypeModel ReadTypeModel(string path)
    {
        if (!File.Exists(path))
        {
            throw new CodegenException($"No type model at {path}: run coho-codegen pull");
        }
        return TypeModels.Read(File.ReadAllText(path), path);
    }

    /// <summary>
    /// Fetches the type model for the configured ref and writes it to <paramref name="path"/>
    /// as it arrived, so the committed file is exactly what the server sent.
    /// </summary>
    /// <remarks>
    /// The server's ETag is the SHA-256 of the body, and the file is the body, so the file's
    /// own hash asks whether anything changed; an unchanged model costs a <c>304</c>. A model
    /// this generator cannot read is refused before anything is written.
    /// </remarks>
    /// <returns>Whether the file changed, and the model it now holds.</returns>
    public static async Task<(bool Changed, TypeModel Model)> PullAsync(CodegenConfig config, string path, string token, HttpMessageInvoker http, CancellationToken cancellationToken)
    {
        if (config.Url is not { } url || config.Account is not { } account || config.Project is not { } project || config.Ref is not { } @ref)
        {
            throw new CodegenException("pull needs url, account, project and ref in the config");
        }
        var endpoint = $"{url.TrimEnd('/')}/api/v1/accounts/{Uri.EscapeDataString(account)}/projects/" +
            $"{Uri.EscapeDataString(project)}/refs/{Uri.EscapeDataString(@ref)}/type-model";

        var existing = File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false) : null;
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (existing is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", $"\"{Convert.ToHexStringLower(SHA256.HashData(existing))}\"");
        }

        using var response = await Send(http, request, url, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && existing is not null)
        {
            return (false, TypeModels.Read(Encoding.UTF8.GetString(existing), path));
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new CodegenException(await FailureAsync(response, "Pulling the type model", cancellationToken).ConfigureAwait(false));
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var model = TypeModels.Read(Encoding.UTF8.GetString(body), endpoint);

        // Written beside the file and moved over it, so a failure never leaves half a model.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync($"{path}.tmp", body, cancellationToken).ConfigureAwait(false);
        File.Move($"{path}.tmp", path, overwrite: true);
        return (true, model);
    }

    /// <summary>
    /// Writes <paramref name="files"/> into <paramref name="outDir"/>, and removes the files an
    /// earlier run generated that this one does not, so a type taken off the ref takes its file
    /// with it. A file without the generated header is never removed.
    /// </summary>
    public static void Write(IReadOnlyList<GeneratedFile> files, string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(outDir, file.Path), file.Contents);
        }
        foreach (var path in Directory.GetFiles(outDir))
        {
            var name = Path.GetFileName(path);
            if (files.Any(file => file.Path == name))
            {
                continue;
            }
            if (File.ReadAllText(path).StartsWith(Generator.GeneratedMark, StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
    }

    /// <summary>
    /// How <paramref name="outDir"/> differs from <paramref name="files"/>: each file missing or
    /// different, and each file there that nothing generates. Empty when the generated code is
    /// up to date.
    /// </summary>
    public static List<string> Drift(IReadOnlyList<GeneratedFile> files, string outDir)
    {
        var problems = new List<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(outDir, file.Path);
            if (!File.Exists(path))
            {
                problems.Add($"{file.Path} is missing");
            }
            else if (File.ReadAllText(path) != file.Contents)
            {
                problems.Add($"{file.Path} differs from what the type model generates");
            }
        }
        if (Directory.Exists(outDir))
        {
            foreach (var path in Directory.GetFiles(outDir))
            {
                var name = Path.GetFileName(path);
                if (!files.Any(file => file.Path == name))
                {
                    problems.Add($"{name} is in the output directory, but nothing generates it");
                }
            }
        }
        return problems;
    }

    internal static async Task<HttpResponseMessage> Send(HttpMessageInvoker http, HttpRequestMessage request, string url, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new CodegenException($"Coho is not reachable at {url}: {exception.Message}");
        }
    }

    /// <summary>What a refusal says: its status, and the problem document's code and detail when there is one.</summary>
    internal static async Task<string> FailureAsync(HttpResponseMessage response, string what, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        var detail = "";
        try
        {
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            string? Text(string name) => problem.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            string?[] parts = [Text("code"), Text("detail") ?? Text("title")];
            detail = string.Join(": ", parts.Where(part => part is not null));
        }
        catch (JsonException)
        {
            // Not a problem document; the status says enough.
        }
        var hint = status is 401 or 403 ? " Check COHO_TOKEN: it needs read access to the project." : "";
        return $"{what} failed with {status}{(detail == "" ? "" : $" ({detail})")}.{hint}";
    }
}
