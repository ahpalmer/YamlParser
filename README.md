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
- `-b, --batch` — Run batch analysis mode (see below)
- `-h, --help` — Show help message

Examples:
```
dotnet run --project YamlFileTreeBuilder -- pipeline.yml
dotnet run --project YamlFileTreeBuilder -- -t pipeline.yml
dotnet run --project YamlFileTreeBuilder -- -t -o output.txt pipeline.yml
```

### Batch Analyzer

Batch mode processes multiple root pipeline files and ranks all referenced YAML templates by how many root pipelines use them. This is useful for identifying the most commonly shared templates across your pipelines.

#### Setup

1. Create a `BatchRoots.json` file in the `YamlFileTreeBuilder` project directory. See `BatchRoots.example.json` for the expected format:

   ```json
   {
     "RootFiles": [
       "/full/path/to/pipeline1.yml",
       "/full/path/to/pipeline2.yml"
     ]
   }
   ```

2. Each entry in `RootFiles` should be the full path to a root pipeline YAML file.

#### Running

```
dotnet run --project YamlFileTreeBuilder -- -b
```

Or equivalently:
```
dotnet run --project YamlFileTreeBuilder -- --batch
```

#### Output

- The top 30 most referenced files are printed to the console, color-coded by rank.
- A full `BatchTreeOutput.txt` file is written to the same directory as `BatchRoots.json`, listing every referenced file with its reference count and which root pipelines reference it.