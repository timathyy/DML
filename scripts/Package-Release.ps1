[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot 'src\Dml.Launcher.App\Dml.Launcher.App.csproj'
$artifactsDirectory = Join-Path $projectRoot 'artifacts'
$releaseDirectory = Join-Path $artifactsDirectory "releases\v$Version"
$stagingDirectory = Join-Path $artifactsDirectory "staging\v$Version"
$portableDirectory = Join-Path $stagingDirectory $Runtime
$portableArchive = Join-Path $releaseDirectory "DML-v$Version-$Runtime.zip"
$checksumFile = "$portableArchive.sha256"
$installerScript = Join-Path $projectRoot 'installer\Dml.iss'
$installerPath = Join-Path $releaseDirectory 'dml_installer.exe'
$installerChecksumFile = "$installerPath.sha256"

function Find-InnoSetupCompiler {
    $command = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $knownPaths = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )

    return $knownPaths | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $SkipTests) {
    & dotnet test (Join-Path $projectRoot 'Dml.Launcher.sln') --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw '单元测试失败，已停止打包。'
    }
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
if (Test-Path $portableDirectory) {
    Remove-Item -LiteralPath $portableDirectory -Recurse -Force
}

& dotnet publish $appProject --configuration Release --runtime $Runtime --self-contained true -p:Version=$Version -p:PublishSingleFile=false -p:DebugType=None --output $portableDirectory
if ($LASTEXITCODE -ne 0) {
    throw '便携版发布失败。'
}

Compress-Archive -Path (Join-Path $portableDirectory '*') -DestinationPath $portableArchive -CompressionLevel Optimal -Force
$hash = (Get-FileHash -LiteralPath $portableArchive -Algorithm SHA256).Hash
Set-Content -LiteralPath $checksumFile -Value "$hash  $(Split-Path -Leaf $portableArchive)" -Encoding ascii

$iscc = Find-InnoSetupCompiler
if ([string]::IsNullOrWhiteSpace($iscc)) {
    throw '未找到 Inno Setup 6。请安装它后重新运行此脚本，以生成 dml_installer.exe。便携版 ZIP 已生成。'
}

& $iscc "/DMyAppVersion=$Version" "/DSourceDir=$portableDirectory" "/DOutputDir=$releaseDirectory" $installerScript
if ($LASTEXITCODE -ne 0) {
    throw '安装器编译失败。'
}

if (-not (Test-Path $installerPath)) {
    throw '安装器编译未生成预期的 dml_installer.exe。'
}

$installerHash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
Set-Content -LiteralPath $installerChecksumFile -Value "$installerHash  $(Split-Path -Leaf $installerPath)" -Encoding ascii

Write-Host "便携版：$portableArchive"
Write-Host "便携版校验文件：$checksumFile"
Write-Host "安装程序：$installerPath"
Write-Host "安装程序校验文件：$installerChecksumFile"
