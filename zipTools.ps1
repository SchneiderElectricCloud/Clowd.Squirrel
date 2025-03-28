param(
  [string]$out
)
$version = "3.0.0"
<#Copy-Item -Path "$PSScriptRoot\vendor\7zip\*" -Destination "$BinOut" -Recurse
Copy-Item -Path "$PSScriptRoot\vendor\wix\*" -Destination "$BinOut" -Recurse
Copy-Item "$In\Win32\Setup.exe" -Destination "$BinOut"
Copy-Item "$In\Win32\StubExecutable.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\nuget.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\rcedit.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\signtool.exe" -Destination "$BinOut"
Copy-Item "$PSScriptRoot\vendor\singlefilehost.exe" -Destination "$BinOut"#>

#Copy-Item -Path ".\build\Release\*.exe" -Destination ".\build\Publish\"

& "C:\\Program Files\\7-Zip\\7z.exe" a "SquirrelTools-$version.zip" -tzip -aoa -y -mmt ".\build\Release\net8.0-windows\*.exe"
& "C:\\Program Files\\7-Zip\\7z.exe" a "SquirrelTools-$version.zip" -tzip -aoa -y -mmt ".\build\Release\*.nupkg"

Copy-Item -Path "SquirrelTools-$version.zip" -Destination "$out"