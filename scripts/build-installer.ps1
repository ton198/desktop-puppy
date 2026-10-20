param(
    [string]$Version = '0.1.2',
    [string]$IsccPath = ''
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { throw 'Version must contain three or four numeric parts.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $IsccPath) {
    $compilerCandidates = @(
        (Join-Path $projectRoot 'artifacts/tools/inno/ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe"
    )
    $IsccPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup or pass -IsccPath with the compiler path.'
}
$publishDirectory = Join-Path $projectRoot 'artifacts/installer/publish'
# Use a clean payload so a previous publish cannot leak obsolete files into Setup.
if (Test-Path -LiteralPath $publishDirectory) {
    $resolvedPublish = (Resolve-Path -LiteralPath $publishDirectory).Path
    $expectedPublish = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/installer/publish'))
    if ($resolvedPublish -ne $expectedPublish) { throw 'Unexpected publish directory.' }
    Remove-Item -LiteralPath $resolvedPublish -Recurse -Force
}
& dotnet publish (Join-Path $projectRoot 'src/DesktopPuppy/DesktopPuppy.csproj') -c Release -r win-x64 --self-contained true -o $publishDirectory -m:1 -nodeReuse:false -p:UseSharedCompilation=false "-p:Version=$Version" --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publishDirectory" (Join-Path $projectRoot 'packaging/DesktopPuppy.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$setupFile = Join-Path $projectRoot "artifacts/installer/DesktopPuppy-$Version-win-x64-Setup.exe"
$checksum = Get-FileHash -LiteralPath $setupFile -Algorithm SHA256
"$($checksum.Hash)  $([IO.Path]::GetFileName($setupFile))" | Set-Content -LiteralPath "$setupFile.sha256" -Encoding ascii
Get-Item -LiteralPath $setupFile
