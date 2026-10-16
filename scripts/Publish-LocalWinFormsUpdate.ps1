[CmdletBinding()]
param([Parameter(Mandatory = $true)][version]$Version)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $repoRoot 'outputs\winforms-publish'
& (Join-Path $PSScriptRoot 'Publish-WinFormsUpdate.ps1') -Version $Version -PublishDirectory $publishDirectory
