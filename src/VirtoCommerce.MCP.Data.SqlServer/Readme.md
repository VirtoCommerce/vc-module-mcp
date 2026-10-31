## Package manager
```
Add-Migration Initial -Context VirtoCommerce.MCP.Data.Repositories.McpDbContext -Project VirtoCommerce.MCP.Data.SqlServer -StartupProject VirtoCommerce.MCP.Data.SqlServer -OutputDir Migrations -Verbose -Debug
```

### Entity Framework Core Commands
```
dotnet tool install --global dotnet-ef --version 10.0.13
```

**Generate Migrations**
```
dotnet ef migrations add Initial
dotnet ef migrations add Update1
dotnet ef migrations add Update2
```
etc..

**Apply Migrations**
```
dotnet ef database update -- "{connection string}"
```
