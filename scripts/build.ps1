$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\DualSound\DualSound.csproj'
$output = Join-Path $projectRoot 'artifacts\win-x64'

dotnet restore $project
dotnet publish $project -c Release -r win-x64 --self-contained true -o $output

$exe = Join-Path $output 'DualSound.exe'
if (-not (Test-Path $exe)) {
    throw "Publish completed without producing $exe"
}

Write-Host "Built: $exe"
