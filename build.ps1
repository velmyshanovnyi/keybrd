# Збирає програму і кладе готовий exe у папку dist.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
dotnet build "$root\src\Keybrd\Keybrd.csproj" -c Release -nologo
New-Item -ItemType Directory -Force "$root\dist" | Out-Null
Copy-Item "$root\src\Keybrd\bin\Release\net48\Keybrd.exe" "$root\dist\Keybrd.exe" -Force
# Номер версії поруч з exe: за цим файлом програма дізнається про оновлення на GitHub.
$version = ([xml](Get-Content "$root\src\Keybrd\Keybrd.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
Set-Content "$root\dist\version.txt" $version -Encoding ascii -NoNewline
Write-Host "Готово: $root\dist\Keybrd.exe"
