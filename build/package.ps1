#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Publishes Calcy (the desktop app and calcy-cli) for one platform and packages it.

.DESCRIPTION
  Both programs are published self-contained into one folder (they share the .NET runtime), debug symbols are
  removed, and the folder is packaged for the platform:
    win-*    Calcy-<version>-<rid>.zip
    osx-*    Calcy-<version>-<rid>.zip and .dmg with Calcy.app (ad-hoc signed; the dmg needs macOS)
    linux-*  calcy-<version>-<rid>.tar.gz, calcy_<debversion>_<arch>.deb and calcy-<version>-<release>.<arch>.rpm
             (.deb needs dpkg-deb, .rpm needs rpmbuild; both are skipped with a warning when missing)

.EXAMPLE
  pwsh build/package.ps1 -Rid linux-x64 -Version 0.1.0-pre.7
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^(win|osx|linux)-(x64|arm64)$')] [string] $Rid,
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$')] [string] $Version,
    [string] $FileVersion,
    [string] $Commit = '',
    [string] $Output = 'artifacts'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$Output = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $Output))
$work = Join-Path $Output "work-$Rid"
$publish = Join-Path $work 'publish'
if (Test-Path $work) { Remove-Item -Recurse -Force -LiteralPath $work }
New-Item -ItemType Directory -Force -Path $publish, $Output | Out-Null

$base = $Version.Split('-')[0]
$pre = if ($Version.Contains('-')) { $Version.Substring($Version.IndexOf('-') + 1) } else { '' }
if (-not $FileVersion) { $FileVersion = "$base.0" }
$info = if ($Commit) { "$Version+$Commit" } else { $Version }
$os = $Rid.Split('-')[0]
$cpu = $Rid.Split('-')[1]

function Invoke-Native([string] $File, [string[]] $Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE" }
}

Write-Host "Publishing Calcy $Version for $Rid"
foreach ($project in 'src/Calcy.App/Calcy.App.csproj', 'src/Calcy.Cli/Calcy.Cli.csproj') {
    Invoke-Native dotnet @(
        'publish', (Join-Path $root $project), '-c', 'Release', '-r', $Rid, '--self-contained', 'true',
        '-o', $publish, '-nologo',
        "-p:Version=$Version", "-p:FileVersion=$FileVersion", "-p:InformationalVersion=$info",
        '-p:IncludeSourceRevisionInInformationalVersion=false', '-p:DebugType=none', '-p:DebugSymbols=false')
}
# Native libraries from NuGet bring their own symbols (about 100 MB for Skia on Windows); users do not need them.
Get-ChildItem -LiteralPath $publish -Recurse -Include '*.pdb', '*.dbg' -File | Remove-Item -Force
$appExe = if ($os -eq 'win') { 'Calcy.exe' } else { 'Calcy' }
$cliExe = if ($os -eq 'win') { 'calcy-cli.exe' } else { 'calcy-cli' }
foreach ($exe in $appExe, $cliExe) {
    if (-not (Test-Path (Join-Path $publish $exe))) { throw "The publish folder has no $exe" }
}

$icons = Join-Path $root 'build/icons'
$made = [System.Collections.Generic.List[string]]::new()

function New-TarGz([string] $SourceParent, [string] $Name, [string] $Target) {
    # tar keeps the executable bits that zip would lose on Linux and macOS.
    Invoke-Native tar @('-czf', $Target, '-C', $SourceParent, $Name)
}

switch ($os) {
    'win' {
        $zip = Join-Path $Output "Calcy-$Version-$Rid.zip"
        $folder = Join-Path $work "Calcy-$Version"
        Copy-Item -Recurse -LiteralPath $publish -Destination $folder
        Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $folder
        if (Test-Path $zip) { Remove-Item -Force -LiteralPath $zip }
        Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
        $made.Add($zip)
    }
    'osx' {
        $app = Join-Path $work 'Calcy.app'
        $contents = Join-Path $app 'Contents'
        $macos = Join-Path $contents 'MacOS'
        $resources = Join-Path $contents 'Resources'
        New-Item -ItemType Directory -Force -Path $macos, $resources | Out-Null
        Copy-Item -Recurse -Path (Join-Path $publish '*') -Destination $macos
        Copy-Item (Join-Path $icons 'calcy.icns') (Join-Path $resources 'calcy.icns')
        Copy-Item (Join-Path $root 'LICENSE') (Join-Path $resources 'LICENSE')
        $plist = (Get-Content -Raw (Join-Path $PSScriptRoot 'macos/Info.plist')).
            Replace('$(VERSION)', $Version).Replace('$(SHORT_VERSION)', $base).Replace('$(BUILD_VERSION)', $FileVersion)
        Set-Content -LiteralPath (Join-Path $contents 'Info.plist') -Value $plist -NoNewline -Encoding utf8NoBOM
        if ($IsMacOS) {
            Invoke-Native chmod @('+x', (Join-Path $macos 'Calcy'), (Join-Path $macos 'calcy-cli'))
            # Apple silicon only runs signed code; an ad-hoc signature is enough for a downloaded app the user opens.
            Invoke-Native codesign @('--force', '--deep', '--sign', '-', $app)
            $zip = Join-Path $Output "Calcy-$Version-$Rid.zip"
            if (Test-Path $zip) { Remove-Item -Force -LiteralPath $zip }
            Invoke-Native ditto @('-c', '-k', '--sequesterRsrc', '--keepParent', $app, $zip)
            $made.Add($zip)
            $dmgRoot = Join-Path $work 'dmg'
            New-Item -ItemType Directory -Force -Path $dmgRoot | Out-Null
            Invoke-Native ditto @($app, (Join-Path $dmgRoot 'Calcy.app'))
            New-Item -ItemType SymbolicLink -Path (Join-Path $dmgRoot 'Applications') -Target '/Applications' | Out-Null
            $dmg = Join-Path $Output "Calcy-$Version-$Rid.dmg"
            if (Test-Path $dmg) { Remove-Item -Force -LiteralPath $dmg }
            Invoke-Native hdiutil @('create', '-volname', "Calcy $Version", '-srcfolder', $dmgRoot, '-ov', '-format', 'UDZO', $dmg)
            $made.Add($dmg)
        }
        else {
            Write-Warning 'Not on macOS: the bundle is packed as tar.gz without signing or a dmg.'
            $tgz = Join-Path $Output "Calcy-$Version-$Rid.tar.gz"
            New-TarGz $work 'Calcy.app' $tgz
            $made.Add($tgz)
        }
    }
    'linux' {
        $debArch = @{ x64 = 'amd64'; arm64 = 'arm64' }[$cpu]
        $rpmArch = @{ x64 = 'x86_64'; arm64 = 'aarch64' }[$cpu]
        # The installed layout, shared by every Linux package: /opt/calcy, commands in /usr/bin, desktop entry and icons.
        $stage = Join-Path $work 'root'
        $opt = Join-Path $stage 'opt/calcy'
        New-Item -ItemType Directory -Force -Path $opt, (Join-Path $stage 'usr/bin'), (Join-Path $stage 'usr/share/applications') | Out-Null
        Copy-Item -Recurse -Path (Join-Path $publish '*') -Destination $opt
        Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $opt
        # Licence where each distribution looks for it: Debian's copyright file and the FHS licenses folder.
        foreach ($dir in 'usr/share/doc/calcy', 'usr/share/licenses/calcy') { New-Item -ItemType Directory -Force -Path (Join-Path $stage $dir) | Out-Null }
        Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'usr/share/doc/calcy/copyright')
        Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'usr/share/licenses/calcy/LICENSE')

        Copy-Item (Join-Path $PSScriptRoot 'linux/calcy.desktop') (Join-Path $stage 'usr/share/applications/calcy.desktop')
        foreach ($size in 16, 24, 32, 48, 64, 128, 256, 512) {
            $dir = Join-Path $stage "usr/share/icons/hicolor/${size}x${size}/apps"
            New-Item -ItemType Directory -Force -Path $dir | Out-Null
            Copy-Item (Join-Path $icons "calcy-$size.png") (Join-Path $dir 'calcy.png')
        }
        if ($IsWindows) { throw 'Linux packages must be built on Linux (symlinks and file modes).' }
        # Plain files 644 and folders 755, the two programs 755; the commands are relative symlinks into /opt/calcy.
        Invoke-Native find @($stage, '-type', 'd', '-exec', 'chmod', '755', '{}', '+')
        Invoke-Native find @($stage, '-type', 'f', '-exec', 'chmod', '644', '{}', '+')
        Invoke-Native chmod @('755', (Join-Path $opt 'Calcy'), (Join-Path $opt 'calcy-cli'))
        Invoke-Native ln @('-s', '../../opt/calcy/Calcy', (Join-Path $stage 'usr/bin/calcy'))
        Invoke-Native ln @('-s', '../../opt/calcy/calcy-cli', (Join-Path $stage 'usr/bin/calcy-cli'))

        # Portable archive for any glibc distribution: unpack anywhere and run calcy/Calcy.
        $tgzName = "calcy-$Version-$Rid"
        $tgzDir = Join-Path $work $tgzName
        Invoke-Native cp @('-a', $opt, $tgzDir)
        $tgz = Join-Path $Output "$tgzName.tar.gz"
        New-TarGz $work $tgzName $tgz
        $made.Add($tgz)

        # Debian, Ubuntu, Mint and friends.
        if (Get-Command dpkg-deb -ErrorAction SilentlyContinue) {
            $debVersion = if ($pre) { "$base~$pre" } else { $base }
            $debRoot = Join-Path $work 'deb'
            Invoke-Native cp @('-a', $stage, $debRoot)
            $debian = Join-Path $debRoot 'DEBIAN'
            New-Item -ItemType Directory -Force -Path $debian | Out-Null
            $kb = [math]::Ceiling(((Get-ChildItem -LiteralPath $debRoot -Recurse -File | Measure-Object Length -Sum).Sum) / 1KB)
            $control = (Get-Content -Raw (Join-Path $PSScriptRoot 'linux/deb-control')).
                Replace('$(VERSION)', $debVersion).Replace('$(ARCH)', $debArch).Replace('$(SIZE_KB)', "$kb")
            # dpkg needs the last field to end with a newline.
            Set-Content -LiteralPath (Join-Path $debian 'control') -Value ($control.TrimEnd() + "`n") -NoNewline -Encoding utf8NoBOM
            $deb = Join-Path $Output "calcy_${debVersion}_$debArch.deb"
            Invoke-Native dpkg-deb @('--build', '--root-owner-group', $debRoot, $deb)
            $made.Add($deb)
        }
        else { Write-Warning 'dpkg-deb not found: skipping the .deb package.' }

        # Fedora, Red Hat Enterprise Linux, AlmaLinux, Rocky Linux, CentOS Stream.
        if (Get-Command rpmbuild -ErrorAction SilentlyContinue) {
            $rpmRelease = if ($pre) { "0.$pre" } else { '1' }
            $top = Join-Path $work 'rpm'
            foreach ($d in 'BUILD', 'RPMS', 'SOURCES', 'SPECS', 'SRPMS', 'BUILDROOT') { New-Item -ItemType Directory -Force -Path (Join-Path $top $d) | Out-Null }
            $spec = (Get-Content -Raw (Join-Path $PSScriptRoot 'linux/calcy.spec')).
                Replace('$(VERSION)', $base).Replace('$(RELEASE)', $rpmRelease).Replace('$(STAGE)', $stage)
            $specFile = Join-Path $top 'SPECS/calcy.spec'
            Set-Content -LiteralPath $specFile -Value $spec -NoNewline -Encoding utf8NoBOM
            Invoke-Native rpmbuild @('-bb', '--target', $rpmArch, '--define', "_topdir $top", $specFile)
            $rpm = Get-ChildItem -LiteralPath (Join-Path $top 'RPMS') -Recurse -Filter '*.rpm' | Select-Object -First 1
            if (-not $rpm) { throw 'rpmbuild produced no package' }
            $target = Join-Path $Output $rpm.Name
            Copy-Item -LiteralPath $rpm.FullName -Destination $target -Force
            $made.Add($target)
        }
        else { Write-Warning 'rpmbuild not found: skipping the .rpm package.' }
    }
}

Write-Host 'Packages:'
foreach ($m in $made) { Write-Host ("  {0} ({1:N1} MB)" -f (Split-Path -Leaf $m), ((Get-Item $m).Length / 1MB)) }
