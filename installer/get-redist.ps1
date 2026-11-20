$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot 'redist'
New-Item -ItemType Directory -Force $target | Out-Null
$file = Join-Path $target 'vc_redist.x64.exe'
if (Test-Path $file) { return }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest 'https://aka.ms/vs/17/release/vc_redist.x64.exe' -OutFile $file -UseBasicParsing
