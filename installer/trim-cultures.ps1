param([Parameter(Mandatory)][string]$Path, [string]$Keep = 'en|en-us|pt|pt-br|es|es-es|zh|zh-cn|zh-hans|zh-hans-cn')
$keepSet = $Keep.ToLowerInvariant().Split('|')
Get-ChildItem -LiteralPath $Path -Directory | Where-Object {
    $_.Name -match '^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$' -and $keepSet -notcontains $_.Name.ToLowerInvariant()
} | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
