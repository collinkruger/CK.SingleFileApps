# Single File Apps

Just a bunch of single file .NET apps :)

To...
- Run: `dotnet run FILENAME`
- Build: `dotnet build FILENAME`
- Aggressive AOT Build: `dotnet publish -c Release -r ARCHITECTURE --self-contained true -p:PublishAot=true -p:InvariantGlobalization=true -p:TrimMode=full -p:StripSymbols=true FILENAME`

See [.NET RID Catalog](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog#known-rids) for your specific ARCHITECTURE