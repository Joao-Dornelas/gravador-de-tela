$ErrorActionPreference = 'Stop'
$repoPath = Split-Path $PSScriptRoot -Parent
$metadata = Get-Content (Join-Path $repoPath 'docs/ffmpeg-source.json') -Raw | ConvertFrom-Json
$toolsPath = Join-Path $repoPath 'tools'
$ffmpegPath = Join-Path $toolsPath 'ffmpeg'
$runtimePath = Join-Path $toolsPath 'vc-runtime'
New-Item -ItemType Directory -Force -Path $ffmpegPath,$runtimePath | Out-Null
if (!(Test-Path (Join-Path $ffmpegPath 'ffmpeg.exe'))) {
    $archivePath = Join-Path $env:TEMP ('screen-recorder-' + [guid]::NewGuid() + '.zip')
    Invoke-WebRequest $metadata.url -OutFile $archivePath
    if ((Get-FileHash $archivePath -Algorithm SHA256).Hash -ne $metadata.sha256) { throw 'O SHA256 do FFmpeg não corresponde à versão registrada.' }
    $extractPath = Join-Path $env:TEMP ('screen-recorder-tools-' + [guid]::NewGuid())
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractPath
    $distribution = Get-ChildItem $extractPath -Directory | Select-Object -First 1
    Copy-Item (Join-Path $distribution.FullName 'bin/ffmpeg.exe') $ffmpegPath
    Copy-Item (Join-Path $distribution.FullName 'bin/ffprobe.exe') $ffmpegPath
    Get-ChildItem $distribution.FullName -File | Copy-Item -Destination $ffmpegPath
    Copy-Item (Join-Path $repoPath 'docs/ffmpeg-source.json') (Join-Path $ffmpegPath 'source.json')
}
if (!(Test-Path (Join-Path $runtimePath 'vcruntime140.dll'))) {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (!(Test-Path $vswherePath)) { throw 'Instale as ferramentas C++ do Visual Studio para obter o runtime redistribuível de desenvolvimento.' }
    $installPath = & $vswherePath -latest -products '*' -property installationPath
    $redistPath = Join-Path $installPath 'VC/Redist/MSVC'
    $crt = Get-ChildItem $redistPath -Filter 'Microsoft.VC*.CRT' -Recurse -Directory |
        Where-Object { $_.FullName -match '\\x64\\' -and $_.FullName -notmatch 'onecore|debug_nonredist' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (!$crt) { throw 'Runtime C++ x64 redistribuível não encontrado no Visual Studio.' }
    Copy-Item (Join-Path $crt.FullName '*.dll') $runtimePath
}
Write-Output 'Ferramentas portáteis preparadas.'
