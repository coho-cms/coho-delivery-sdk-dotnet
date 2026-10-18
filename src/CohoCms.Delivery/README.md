# CohoCms.Delivery

The Coho CMS delivery client, and the runtime your generated content library calls.

Most projects don't reference this directly. They reference their own content library, generated from their content types, which depends on this package and gives every content type its own typed reads. Use this package directly for untyped access, or for anything the generated reads don't cover.

```bash
dotnet add package CohoCms.Delivery --prerelease
```

## Untyped reads

```csharp
using CohoCms.Delivery;

var delivery = new DeliveryClient(new ReleasedConfig
{
    Url = "https://delivery.coho.example",
    Project = "0192…",
    Key = Environment.GetEnvironmentVariable("COHO_DELIVERY_KEY"),
});

var home = await delivery.EntryBySlugAsync("prod", "home-page", new ReadOptions { Depth = 2, Locale = "en-US" });
// Roots, Entries by id, and Unresolved by id ("depth" or "absent"); or null
```

`EntriesAsync`, `EntryAsync`, `EntryBySlugAsync` and `EntriesByIdAsync` return the delivery API's response as it was sent: the entries you asked for in `Roots`, everything they reference within `Depth` hops in `Entries`, and anything referenced but not returned in `Unresolved`. Responses are immutable, so the client's cache can share them.

## Hydrated reads

`ContentClient` returns entries with their references replaced by the entries they point at. It is what generated libraries are built on.

```csharp
var content = new ContentClient(
    new ContentConfig { Delivery = config, Ref = "prod", Locale = "en-US" },
    shape: Shapes.All); // what a generated library declares for you

var post = await content.Type<BlogPost, BlogPostOrder>("blogPost").BySlugAsync("hello-world", new ReadOptions { Depth = 2 });
if (post?.Author is { IsResolved: true } author)
{
    Console.WriteLine(author.Value.FullName);
}
```

- **Entries are records** deriving from `EntryBase`, with the system values `Id`, `Type`, `Slug` and `Locale` beside their fields. An entry of a type nobody declared is a `HydratedEntry`, holding its fields as JSON.
- **A reference is a `Linked<T>`**: its target, or a stub whose `Unresolved` says why it is not there. `depth` when it is beyond the requested depth, `absent` when it is not on the ref, `cycle` where following it would loop back, and `type` where the target is of a type the field does not link to.
- **Problems are reported, not thrown.** An entry missing a field its type always has, which happens to older entries after a field becomes required, or holding a value of the wrong kind, is passed to `ClientOptions.OnIssue`, once per entry and field. The default is a warning on the client's logger.

## Released and unreleased content

What a client reads depends on its config.

| | `ReleasedConfig` | `PreviewConfig` |
|---|---|---|
| Refs | Tags, and environments pointing at one, such as `qa` and `prod` | Branches, and environments pointing at one, such as `dev` |
| Served by | Delivery, cached at the edge | Coho, live, never cached |
| `Url` | Delivery's, such as `https://delivery.coho.example` | Coho's own, where you sign in, such as `https://api.coho.example` |
| Credential | `Key`: a delivery key | `Token`: a project token, `coho_pt_…`, with the viewer role, plus the `Account` |

Both answer the same requests in the same shapes, so a preview runs the code production does. Create a token for previews with `coho token create --role viewer`.

**Keep both on the server.** A credential is sent as `Authorization: Bearer`, never in a URL, and a config's `ToString()` never prints it. A client given a token refuses to start in a browser, such as Blazor WebAssembly.

### From configuration

`AddCohoContent` registers one `ContentClient` for the app, configured from `COHO_*` values in its configuration: environment variables, user secrets or `appsettings.json` alike.

```csharp
builder.Services.AddCohoContent(Shapes.All);
```

| Value | |
|---|---|
| `COHO_PROJECT`, `COHO_REF` | Always |
| `COHO_LOCALE` | Optional |
| `COHO_DELIVERY_URL`, `COHO_DELIVERY_KEY` | Released content. The key may be left out for a public environment |
| `COHO_PREVIEW_TOKEN`, `COHO_URL`, `COHO_ACCOUNT` | A preview |

The names are the same in every Coho SDK. Setting a preview token and a delivery key together is refused, since they read different content. `CohoEnvironment.FromEnvironment()` reads the same values without a host.

`AddCohoContent` returns the `IHttpClientBuilder` of the client's `HttpClient`, so you can add resilience handlers to it.

## Assets

An asset field is read as an `Asset`: its `File` (media type, size, and for images width and height), `Alt`, `FocalPoint`, and the URLs the asset CDN serves it at. They build resized URLs with no request and no secret:

```csharp
post.Hero?.Image(new ImageOptions { Width = 800, Format = ImageFormat.Auto });       // resized at the edge
post.Hero?.Image(new ImageOptions { Width = 400, Height = 400, Fit = ImageFit.Fill }); // cropped around its focal point
post.Hero?.Srcset();                                                                   // 640w … up to the original
post.Hero?.Srcset(new ImageOptions { Width = 300 }, densities: [1, 2]);                // 1x, 2x
```

- **Only raster images resize** (`IsImage`). An SVG, a PDF or any other file has only its original, which `Image` and `Srcset` return whatever the options.
- **The edge snaps widths up** to `Asset.ImageWidths`, so a `Srcset` built from them lands on cached images.
- **An asset is as public as its URL**, drafts included, and deleting it does not stop the URL working.

Delivery does not serve asset fields yet (coho-data doc 40 §14.6); the shape is the one it will embed.

## Caching

Each client keeps a private cache of responses, following `Cache-Control` and `ETag`. A tag named directly is fetched once. An environment is reused for its short `max-age`, then revalidated, which costs a `304` when nothing was promoted. A preview is fetched every time. Set `ClientOptions.MaxCachedResponses` to 0 to turn it off.

## Errors

A refusal throws `CohoDeliveryException`. Branch on its `Code`: `REF_NOT_FOUND`, `KEY_REQUIRED`, `PROJECT_NOT_FOUND`, `INVALID_QUERY` or `RESOLUTION_LIMIT` for the request; `ARTIFACT_UNREADABLE` or `POOL_UNAVAILABLE` for the server; or `UNEXPECTED_RESPONSE` for an answer that isn't a problem document. A preview adds `UNAUTHENTICATED`, for a token that has expired or been revoked, and `ACCOUNT_SUSPENDED`, `ACCOUNT_CLOSED` and `USER_SUSPENDED`. A missing entry is `null`, not an exception.

## License

Apache-2.0
