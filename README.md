# coho-delivery-sdk-dotnet

.NET packages for reading content from Coho CMS.

| Package | What it is |
|---|---|
| [`CohoCms.Delivery`](src/CohoCms.Delivery) | The delivery client, and the runtime generated content libraries call |
| `CohoCms.Codegen` | Not yet built. A dotnet tool, `coho-codegen`, that generates a typed content library from your project's content types |
| `CohoCms.Templates` | Not yet built. `dotnet new coho-library`: creates your content library from the template |

They are the .NET counterparts of [coho-delivery-sdk-typescript](https://github.com/coho-cms/coho-delivery-sdk-typescript)'s packages, and pass the same recordings.

## Development

The .NET 10 SDK. The library targets `net8.0` and `net10.0`; locally, the `net8.0` tests roll forward onto whichever runtime is installed, and CI runs each on its own.

```bash
dotnet test
dotnet format
dotnet pack -c Release -o artifacts
```

`dotnet format --verify-no-changes` is what CI checks. Allman braces, C#'s default: an opening brace goes on its own line.

`spec/` is a copy of [coho-sdk-spec](https://github.com/coho-cms/coho-sdk-spec): recorded delivery responses and type model fixtures, produced by the Coho server. The tests run against it, so these packages agree with the server. Don't edit it by hand.

## License

Apache-2.0
