# coho-delivery-sdk-dotnet

.NET packages for reading content from Coho CMS.

| Package | What it is |
|---|---|
| [`CohoCms.Delivery`](src/CohoCms.Delivery) | The delivery client, and the runtime generated content libraries call |
| [`CohoCms.Codegen`](src/CohoCms.Codegen) | A dotnet tool, `coho-codegen`, that generates a typed content library from your project's content types |
| [`CohoCms.Templates`](src/CohoCms.Templates) | `dotnet new coho-library`: creates your content library from the [template](https://github.com/coho-cms/coho-template-dotnet) |

They are the .NET counterparts of [coho-delivery-sdk-typescript](https://github.com/coho-cms/coho-delivery-sdk-typescript)'s packages, and pass the same recordings.

## Development

The .NET 10 SDK. The library targets `net8.0` and `net10.0`; locally, the `net8.0` tests roll forward onto whichever runtime is installed, and CI runs each on its own.

```bash
dotnet test
dotnet format
dotnet pack -c Release -o artifacts
```

`test/CohoCms.Codegen.Tests/Expected/` holds what the generator writes for each type model fixture, compiled into the tests against the runtime. After an intended change to the output, rewrite it with `COHO_UPDATE_GOLDEN=1 dotnet test` and review the diff.

`dotnet format --verify-no-changes` is what CI checks. Allman braces, C#'s default: an opening brace goes on its own line.

`spec/` is a copy of [coho-sdk-spec](https://github.com/coho-cms/coho-sdk-spec): recorded delivery responses and type model fixtures, produced by the Coho server. The tests run against it, so these packages agree with the server. Don't edit it by hand.

## Publishing

The three packages are published to nuget.org together, at one version, as alphas for now: `CohoCms.Templates` pins every library it creates to that version of `CohoCms.Delivery` and `CohoCms.Codegen`, and carries the library template inside it.

1. Bump `<Version>` in `Directory.Build.props`, such as to `0.1.0-alpha.4`, and merge.
2. Tag [coho-template-dotnet](https://github.com/coho-cms/coho-template-dotnet) at the commit to ship.
3. Run the **Publish** workflow, naming that tag. It tests, bundles the template into `CohoCms.Templates`, and publishes all three.

A version cannot be published twice. The workflow signs in to nuget.org with Trusted Publishing: a policy on nuget.org, held by the `CohoCMS` organisation, trusts `publish.yml` in this repository, and the workflow needs only `NUGET_USER`, the nuget.org user who created the policy. No API key is stored.

To try the template from a checkout before publishing, bundle it, pack, and install the package into a template hive of its own:

```bash
dotnet run scripts/bundle-template.cs -- ../coho-template-dotnet
dotnet pack -c Release -o artifacts
dotnet new install artifacts/CohoCms.Templates.0.1.0-alpha.3.nupkg --debug:custom-hive /tmp/coho-hive
dotnet new coho-library -n Acme.Content --debug:custom-hive /tmp/coho-hive
```

## License

Apache-2.0
