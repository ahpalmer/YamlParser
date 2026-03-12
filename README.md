## YamlFileTreeBuilder

Azure DevOps Pipeline Dependency Tree Viewer.

### Setup

This project uses [.NET User Secrets](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets) to store base paths for resolving YAML template references. You need to configure these before running the tool.

Primary method: 
In order to set up the secrets.json file, right click on the YamlFileTreeBuilder project, select "Manage User Secrets", and edit that secrets.json file to follow the example in the `secrets.example.json` file in the `YamlFileTreeBuilder` directory.

Secondary method:
Alternatively you can use the command line:
1. Navigate to the project directory:
   ```
   cd YamlFileTreeBuilder
   ```

2. Initialize user secrets (already done if `UserSecretsId` exists in the `.csproj`):
   ```
   dotnet user-secrets init
   ```

3. Set your `BasePaths` — these are the root directories where the tool will search for referenced YAML templates:
   ```
   dotnet user-secrets set "BasePaths:0" "/path/to/your/first/repo"
   dotnet user-secrets set "BasePaths:1" "/path/to/your/second/repo"
   ```

   Alternatively, you can edit right click on the project icon, select "Manage User Secrets" and edit that secrets.json file

   See `secrets.example.json` in the `YamlFileTreeBuilder` directory for the expected format.

### Usage

```
dotnet run --project YamlFileTreeBuilder -- [options] <path/to/pipeline.yml>
```

Options:
- `-j, --jobs` — Show job names alongside template files
- `-t, --tasks` — Show job names and task/step names
- `-o, --output <file>` — Write output to a text file (in addition to console)
- `-h, --help` — Show help message

Examples:
```
dotnet run --project YamlFileTreeBuilder -- pipeline.yml
dotnet run --project YamlFileTreeBuilder -- -t pipeline.yml
dotnet run --project YamlFileTreeBuilder -- -t -o output.txt pipeline.yml
```