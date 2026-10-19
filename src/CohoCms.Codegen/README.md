# CohoCms.Codegen

Generates a typed content library from your Coho CMS content types: one C# record per content type, and a client whose reads return them.

It belongs in your content library, the project your sites and services reference, as a local tool beside a reference to `CohoCms.Delivery`.

```bash
dotnet new tool-manifest
dotnet tool install CohoCms.Codegen --prerelease
dotnet add package CohoCms.Delivery --prerelease
```

## Commands

```bash
dotnet coho-codegen init       # point the library at a Coho project, from a project token
dotnet coho-codegen pull       # fetch the type model for the configured ref into coho-ir.json
dotnet coho-codegen generate   # write Coho/ from coho-ir.json: a file per type, and Coho.cs
dotnet coho-codegen check      # fail if Coho/ differs from what generate would write
```

Commit `coho-ir.json`, `Coho/` and `.config/dotnet-tools.json`. `generate` removes the files of types no longer on the ref, and leaves anything it did not write. A schema change then shows up as a diff in review, and `check` in CI catches generated code that is out of date. `generate` and `check` never call the server.

`init` takes Coho's URL and a project token, and nothing else: a token belongs to one project, so Coho says which account and project the library is for. It writes them to `coho.config.json`, and the token to `.env`, which only you can read. In a terminal it asks for what you didn't pass, and doesn't show the token as you type it. Nothing is written if Coho refuses the token. The library only reads, so a viewer token is enough:

```bash
coho token create --role viewer --label "acme-content library"
dotnet coho-codegen init --url https://app.coho.example --ref v1.4.0
```

`pull` needs `COHO_TOKEN`: a project token with read access. It is read from the environment, or for local runs from the `.env` beside the config, and never from the config itself. Keep it in CI secrets and out of every app that references the library. `pull --ref v1.4.0` pulls another tag than the config names. An unchanged type model costs a `304`.

## Configuration

`coho.config.json`, next to your project file, the same file every Coho generator reads. Paths are relative to it.

```json
{
  "url": "https://app.coho.example",
  "account": "0192…",
  "project": "0192…",
  "ref": "v1.4.0",
  "typeModel": "coho-ir.json",
  "outDir": "Coho",
  "namespace": "Acme.Content"
}
```

`namespace` is optional: without it, the generated code takes the name of the project beside the config, as `dotnet new` names a project's root namespace. Generate from a tag for a release. A branch's types can change, and `generate` warns when the model came from one.

## What is generated

```csharp
using Acme.Content;

builder.Services.AddCoho(); // from the COHO_* values in configuration

// or, without a host:
var coho = new Coho(new ContentConfig
{
    Delivery = new ReleasedConfig { Url = "https://delivery.coho.example", Project = "0192…", Key = deliveryKey },
    Ref = "prod",
    Locale = "en-US",
});

var post = await coho.BlogPost.BySlugAsync("hello-world", new ReadOptions { Depth = 2 });
var latest = await coho.BlogPost.ListAsync(new() { Order = BlogPost.SortBy.PublishedAt.Descending, Limit = 3 });
```

- **One record per content type, and per component type, in its own file**, named from its slug: `blogPost` becomes `BlogPost`, in `BlogPost.cs`. `Coho.cs` holds the client, its `Shape`, its `Source`, and `AddCoho`. Each record's fields sit beside the system values `Id`, `Type`, `Slug` and `Locale` from `EntryBase`.
- **A field is nullable when delivery can leave it out**, which includes every localized field. A list is never null: it is empty instead. A value of the wrong kind is reported, read as null, and never throws.
- **A choice is a struct per field**, such as `BlogPost.LayoutChoice`, with a member for each value Coho offered, and any other value accepted, so new content never breaks the build.
- **References are typed by their targets**: `Linked<TeamMember>`, which is the entry, or a stub saying why it is not there. A field that may link to several types is a `Linked<EntryBase>`; match on the target's type.
- **An asset field is an `Asset?`**, or an `IReadOnlyList<Asset>` when it holds several: the file's metadata and its URLs, embedded by delivery. A single asset is nullable even when required, because an asset deleted on the ref is delivered as null; in a list it is left out. `asset.Image(new ImageOptions { Width = 800 })` and `asset.Srcset()` build resized URLs, and `asset.Download()` a link to download it.
- **A repeater holds typed items.** Each component type is a record of its own, `Citation` in `Citation.cs`, deriving from `ItemBase`, with `Id` and `Type` beside its fields, and a repeater is a list of them: `IReadOnlyList<Citation>`, or `IReadOnlyList<ItemBase>` when it holds several, which you narrow by matching on the record's type. An item of a component added after the code was generated is left out of the list and reported, so an item is never typed as something it is not. A repeater whose components are not on the ref holds `RepeaterItem`s, with their fields as JSON. Needs a type model of 1.2 or later.
- **A type that extends another derives from its record.** `ServicePage : Page`, declaring only the fields it adds; an abstract type is an `abstract record`, and a type others extend is not sealed. Delivery lists a type with the types that extend it, so `coho.Page` reads `Page` records of every kind, and a link to `page` is a `Linked<Page>`: match on the record's type to narrow either. A field a type tightens, such as one it makes required, keeps its parent's C# type, which holds every value the tighter rule allows. Needs a type model of 1.3 or later.
- **`Order` accepts only fields the type can be sorted by**, through its `SortBy`, so an unsortable order does not compile.
- **The untyped reads**, `BySlugAsync`, `ByIdAsync` and `ByIdsAsync`, return an `EntryBase`: match on its type to narrow it. `Coho.Source.Ref` names the tag the code was generated from.
- **The library reads no environment of its own.** The app passes its config to `Coho`, or lets `AddCoho` read the `COHO_*` values from the app's configuration.

If a type's name would collide with one the generated code uses, such as `Coho`, `Source`, `Linked` or `Task`, its record gets `Entry` after it. A field named like an entry's own members, such as `Id` or `Type`, gets `Field` after it. A type read like one of the client's own members, such as `Source`, is read as `coho.SourceType`. Two types whose files differ only in case are refused: they would be one file on macOS and Windows. `generate` says when any of these happens.

## License

Apache-2.0
