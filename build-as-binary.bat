rmdir /s /q bin
rmdir /s /q obj

dotnet restore

dotnet publish ./AIUsageMonitor.csproj ^
 -f net10.0-windows10.0.19041.0 ^
 -c Release ^
 -p:RuntimeIdentifierOverride=win-x64 ^
 --self-contained true ^
 -p:WindowsPackageType=None ^
 -p:PublishSingleFile=true ^
 -p:IncludeNativeLibrariesForSelfExtract=true ^
 -p:EnableCompressionInSingleFile=true ^
 -p:DebugType=None ^
 -p:DebugSymbols=false