$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'setup-tools.ps1')
$projectPath = Join-Path $repoPath 'src/ScreenRecorder/ScreenRecorder.csproj'
dotnet publish $projectPath -p:PublishProfile=Portable -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar o executável portátil.' }
