cd SolisManager

dotnet publish SolisManager.csproj --self-contained true -r osx-x64 -c Release 

dotnet publish SolisManager.csproj --self-contained true -r linux-x64 -c Release 

dotnet publish SolisManager.csproj --self-contained true -r win-x64 -c Release 

