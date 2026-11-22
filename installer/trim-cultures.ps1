param([Parameter(Mandatory)][string]$Path, [string]$Keep = 'en|en-us|pt|pt-br|es|es-es|zh|zh-cn|zh-hans|zh-hans-cn')
$Path = $Path.TrimEnd('\')
$keepSet = $Keep.ToLowerInvariant().Split('|')
Get-ChildItem -LiteralPath $Path -Directory | Where-Object {
    $_.Name -match '^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$' -and $keepSet -notcontains $_.Name.ToLowerInvariant()
} | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

$unused = @(
    'onnxruntime*.dll', 'Microsoft.ML.OnnxRuntime.dll', 'DirectML*.dll', 'NPUDetect.dll', 'System.Numerics.Tensors.dll',
    'Microsoft.Windows.AI.*', 'Microsoft.Windows.Vision*', 'Microsoft.Windows.VisionInternal*', 'Microsoft.Windows.Internal.Vision*',
    'Microsoft.Windows.SemanticSearch*', 'Microsoft.Windows.Workloads*', 'Microsoft.Windows.Private.Workloads*', 'workloads*.json',
    'Microsoft.Graphics.Imaging*', 'Microsoft.Graphics.ImagingInternal*', 'Microsoft.Graphics.Internal.Imaging*',
    'Microsoft.Security.Authentication.OAuth*', 'Microsoft.Windows.Widgets*',
    'Microsoft.Windows.PushNotifications*', 'PushNotificationsLongRunningTask*', 'Microsoft.Windows.BadgeNotifications*',
    'Microsoft.Windows.ApplicationModel.Background*', 'Microsoft.Windows.Media.Capture*', 'Microsoft.Windows.Management.Deployment*',
    'Microsoft.Windows.Storage.Pickers*', 'Microsoft.Windows.Security.AccessControl*',
    'WindowsAppSdk.AppxDeploymentExtensions*', 'WindowsAppRuntime.DeploymentExtensions*', 'RestartAgent.exe', 'map.html',
    'createdump.exe', 'mscordbi.dll', 'mscordaccore*.dll', 'Microsoft.DiaSymReader.Native*.dll',
    'System.Data.Common.dll', 'System.Data.dll', 'System.Data.DataSetExtensions.dll', 'System.Net.Mail.dll', 'System.Net.HttpListener.dll',
    'System.ServiceModel.Web.dll', 'System.ServiceProcess.dll', 'System.Web.dll', 'System.Web.HttpUtility.dll', 'System.Windows.dll',
    'Microsoft.VisualBasic.dll', 'Microsoft.VisualBasic.Core.dll', 'System.Formats.Tar.dll', 'System.Runtime.InteropServices.JavaScript.dll'
)
$junkExtensions = '.pdb', '.ilk', '.exp', '.lib'

$deps = Join-Path $Path 'RedControlUtility.deps.json'
$json = $null
try { $json = Get-Content -LiteralPath $deps -Raw | ConvertFrom-Json } catch { Write-Warning ('Could not read RedControlUtility.deps.json, unused files were kept: ' + $_.Exception.Message) }

$removed = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$freed = 0
Get-ChildItem -LiteralPath $Path -File -Recurse | ForEach-Object {
    $name = $_.Name
    if ($null -eq $json -and $junkExtensions -notcontains $_.Extension) { return }
    $match = $junkExtensions -contains $_.Extension
    if (-not $match) { foreach ($pattern in $unused) { if ($name -like $pattern) { $match = $true; break } } }
    if ($match) {
        $freed += $_.Length
        [void]$removed.Add($name)
        Remove-Item -LiteralPath $_.FullName -Force
    }
}
Write-Host ('Removed {0} unused files ({1:N1} MB)' -f $removed.Count, ($freed / 1MB))

if ($null -ne $json -and $removed.Count -gt 0) {
    $dropped = 0
    foreach ($target in @($json.targets.PSObject.Properties)) {
        foreach ($package in @($target.Value.PSObject.Properties)) {
            foreach ($section in 'runtime', 'native', 'runtimeTargets') {
                $node = $package.Value.$section
                if ($null -eq $node) { continue }
                foreach ($entry in @($node.PSObject.Properties)) {
                    $leaf = ($entry.Name -split '/')[-1]
                    if ($removed.Contains($leaf)) { [void]$node.PSObject.Properties.Remove($entry.Name); $dropped++ }
                }
            }
        }
    }
    $text = $json | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText($deps, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ('Updated RedControlUtility.deps.json ({0} entries removed)' -f $dropped)
}

foreach ($required in 'RedControlUtility.exe', 'RedControlUtility.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'Microsoft.WinUI.dll', 'Microsoft.ui.xaml.dll', 'Microsoft.WindowsAppRuntime.dll') {
    if (-not (Test-Path -LiteralPath (Join-Path $Path $required))) { Write-Warning "Required file missing after trimming: $required" }
}

$all = Get-ChildItem -LiteralPath $Path -File -Recurse
Write-Host ('Published size: {0:N1} MB in {1} files' -f (($all | Measure-Object Length -Sum).Sum / 1MB), $all.Count)
$all | Sort-Object Length -Descending | Select-Object -First 15 | ForEach-Object {
    Write-Host ('  {0,8:N2} MB  {1}' -f ($_.Length / 1MB), $_.FullName.Substring($Path.Length).TrimStart('\'))
}
