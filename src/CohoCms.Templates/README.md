# CohoCms.Templates

`dotnet new coho-library`: creates your content library for Coho CMS, a .NET library generated from your project's content types, that your sites and services reference.

```bash
dotnet new install CohoCms.Templates::0.1.0-alpha.3
dotnet new coho-library -n Acme.Content
cd Acme.Content
dotnet tool restore
dotnet coho-codegen init --url https://app.coho.example --ref v1.4.0
dotnet coho-codegen pull
dotnet coho-codegen generate
```

It copies the [library template](https://github.com/coho-cms/coho-template-dotnet) and names it: the solution, the project, its namespace and `coho.config.json` all take the name you give. `coho-codegen init` then points it at your Coho project from a project token: Coho says which account and project the token is for, so there are no ids to copy, and the token goes in the library's `.env`, which is in its `.gitignore`. The library only reads, so a viewer token is enough:

```bash
coho token create --role viewer --label "acme-content library"
```

**A version of CohoCms.Templates always creates the same library.** It carries the template it was released with, and pins the library to `CohoCms.Delivery` and `CohoCms.Codegen` at its own version: `CohoCms.Templates` `0.1.0-alpha.3` creates libraries on `0.1.0-alpha.3`.

## License

Apache-2.0
