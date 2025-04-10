param(
  [string]$out
)
$version = "3.0.0-beta-0002"
$location = [string](Get-Location)
$root = "$location\build\Release"

#Copy-Item -Path ".\build\Release\*.exe" -Destination ".\build\Publish\"

& "C:\\Program Files\\7-Zip\\7z.exe" a "SquirrelTools-$version.zip" -tzip -aoa -y -mmt ".\build\Release\net8.0-windows\*.exe"
& "C:\\Program Files\\7-Zip\\7z.exe" a "SquirrelTools-$version.zip" -tzip -aoa -y -mmt ".\build\Release\*.nupkg"

Copy-Item -Path "SquirrelTools-$version.zip" -Destination "$out"