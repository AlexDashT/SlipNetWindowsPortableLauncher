param(
    [string]$PublishDir = ".\buildbin\Release\net8.0-windows\win-x64\publish",
    [string]$OutputZip = ".\release\SlipNetWindowsPortableLauncher-win-x64-v0.1.0.zip"
)

$publishPath = Resolve-Path -LiteralPath $PublishDir
$outputDirectory = Split-Path -Path $OutputZip -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

if (Test-Path -LiteralPath $OutputZip) {
    Remove-Item -LiteralPath $OutputZip -Force
}

Compress-Archive -Path (Join-Path $publishPath "*") -DestinationPath $OutputZip -CompressionLevel Optimal
