$solutionPath = split-path $MyInvocation.MyCommand.Definition
$getDotNet = join-path $solutionPath "tools\install.ps1"

$env:DOTNET_INSTALL_DIR="$(Convert-Path "$PSScriptRoot")\.dotnet\win7-x64"
if (!(Test-Path $env:DOTNET_INSTALL_DIR))
{
    mkdir $env:DOTNET_INSTALL_DIR | Out-Null
}
    
& $getDotNet

$env:PATH = "$env:DOTNET_INSTALL_DIR;$env:PATH"

$autoGeneratedVersion = $false

# Generate version number if not set
if ($env:BuildSemanticVersion -eq $null) {
    $autoVersion = [math]::floor((New-TimeSpan $(Get-Date) $(Get-Date -month 1 -day 1 -year 2016 -hour 0 -minute 0 -second 0)).TotalMinutes * -1).ToString() + "-" + (Get-Date).ToString("ss")
    $env:BuildSemanticVersion = "rc2-" + $autoVersion
    $autoGeneratedVersion = $true
    
    Write-Host "Set version to $autoVersion"
}

ls */*/project.json | foreach { echo $_.FullName} |
foreach {
    $content = get-content "$_"
    $content = $content.Replace("99.99.99-rc2", "1.0.0-$env:BuildSemanticVersion")
    set-content "$_" $content -encoding UTF8
}

# Restore packages and build product
& dotnet restore "src\dotnet-test-xunit" --infer-runtimes
if ($LASTEXITCODE -ne 0)
{
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

& dotnet pack "src\dotnet-test-xunit" --configuration Release --output "artifacts\packages"

#restore, compile, and run tests
& dotnet restore "test" -f "artifacts\packages" --infer-runtimes
dir "test" | where {$_.PsIsContainer} |
foreach {
    pushd "test\$_"
    & dotnet build
    & dotnet test
    popd
}

ls */*/project.json | foreach { echo $_.FullName} |
foreach {
    $content = get-content "$_"
    $content = $content.Replace("1.0.0-$env:BuildSemanticVersion", "99.99.99-rc2")
    set-content "$_" $content -encoding UTF8
}

if ($autoGeneratedVersion){
    $env:BuildSemanticVersion = $null
}