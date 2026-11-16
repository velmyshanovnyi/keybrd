# Збирає програму і кладе готовий exe у папку dist.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet build "$root\src\Keybrd\Keybrd.csproj" -c Release -nologo
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Copy-Item "$root\src\Keybrd\bin\Release\net48\Keybrd.exe" "$root\dist\Keybrd.exe" -Force
Write-Host "Готово: $root\dist\Keybrd.exe"
