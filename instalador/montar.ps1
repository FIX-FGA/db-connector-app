<#
  Monta o instalador MSI do DB Connector (o mesmo para qualquer organização: as conexões são cadastradas no aplicativo).

    ./instalador/montar.ps1 -Exe out/db-connector.exe -Versao 1.0.7 -Saida instalador/saida

  Precisa do WiX 5 (dotnet tool install --global wix --version 5.0.2) e da extensão de telas
  (wix extension add -g WixToolset.UI.wixext/5.0.2).
#>
param(
    [Parameter(Mandatory)] [string] $Exe,
    [Parameter(Mandatory)] [string] $Versao,
    [string] $Saida = (Join-Path $PSScriptRoot 'saida')
)
$ErrorActionPreference = 'Stop'

New-Item -ItemType Directory -Force $Saida | Out-Null
$msi = Join-Path $Saida 'db-connector-windows-x64.msi'
$raiz = Split-Path $PSScriptRoot
wix build (Join-Path $PSScriptRoot 'DbConnector.wxs') `
    -ext WixToolset.UI.wixext -culture pt-BR -arch x64 `
    -d "Versao=$Versao" `
    -d "Exe=$((Resolve-Path $Exe).Path)" `
    -d "Icone=$(Join-Path $raiz 'src/DbConnector.Desktop/Assets/db-connector.ico')" `
    -d "Licenca=$(Join-Path $PSScriptRoot 'licenca.rtf')" `
    -o $msi
if ($LASTEXITCODE -ne 0) { throw "wix build falhou ($LASTEXITCODE)" }
Write-Host "MSI: $msi ($([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB)"
