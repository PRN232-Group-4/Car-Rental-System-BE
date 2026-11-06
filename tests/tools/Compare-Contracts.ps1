<#
.SYNOPSIS
    Compares the two committed contract goldens against a base git revision, allowing exactly the two
    differences a slice extraction is allowed to produce.

.DESCRIPTION
    Moving controller actions from one controller to another legitimately changes two goldens:

      * swagger.v1.json          Swashbuckle groups operations by controller name ("tags").
      * success-messages.v1.txt  every line carries a "Controller.Action" column.

    Nothing else may change. This script reads both goldens at a base revision and in the working tree and
    checks exactly that:

      swagger   both documents are parsed and compared node by node; every path, method, parameter request
                body, response and every component schema must be identical once the operation "tags"
                arrays are ignored. Operations whose tags changed are printed. Any other difference fails
                the script and the first one is printed.
      messages  every line is compared with the "Controller.Action" column ignored. The changed
                Controller.Action pairs are printed. Any other difference (message, route, verb, status,
                query, missing or extra line) fails the script.

    Exit code 0 means "only tags and Controller.Action changed"; 1 means "the contract changed further"
    or the comparison could not run.

.EXAMPLE
    .\tests\tools\Compare-Contracts.ps1 -BaseRevision 6841820
#>
[CmdletBinding()]
param(
    # Git revision to compare the working tree against, e.g. a commit SHA, a tag or HEAD.
    [Parameter(Mandatory = $true)]
    [string] $BaseRevision,

    # Repository root. Defaults to two levels above this script (tests/tools -> tests -> repo root).
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# $PSScriptRoot is not populated yet while parameter defaults are evaluated, so the default is resolved here.
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
}
$RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path

$SwaggerPath = 'tests/VehicleRental.Integration.Tests/Contracts/swagger.v1.json'
$MessagesPath = 'tests/VehicleRental.Integration.Tests/Contracts/success-messages.v1.txt'

$OperationMethods = @('get', 'put', 'post', 'delete', 'options', 'head', 'patch', 'trace')

# -------------------------------------------------------------------------------------------------
# Reading files: the working tree as bytes, the base revision straight out of git as bytes.
# -------------------------------------------------------------------------------------------------

function Read-WorkingTreeFile {
    param([string] $RelativePath)

    $full = Join-Path $RepositoryRoot $RelativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $full)) { throw "Missing working-tree file: $RelativePath" }
    return [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
}

<#
    Reads a blob at a revision. git's stdout is captured as a byte stream and decoded as UTF-8 on purpose:
    piping it through PowerShell would re-encode the text with the console code page and quietly corrupt
    the non-ASCII descriptions the swagger document contains.
#>
function Read-GitFile {
    param([string] $Revision, [string] $RelativePath)

    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = 'git'
    $start.Arguments = "show `"$Revision`":`"$RelativePath`""
    $start.WorkingDirectory = $RepositoryRoot
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::Start($start)
    try {
        $buffer = New-Object System.IO.MemoryStream
        $process.StandardOutput.BaseStream.CopyTo($buffer)
        $bytes = $buffer.ToArray()
        $errors = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "git show ${Revision}:${RelativePath} failed ($($process.ExitCode)): $errors"
        }
        return [System.Text.Encoding]::UTF8.GetString($bytes)
    }
    finally {
        $process.Dispose()
    }
}

# -------------------------------------------------------------------------------------------------
# Swagger: a canonical, order-insensitive rendering of a JSON document, with operation tags removed.
# -------------------------------------------------------------------------------------------------

<#
    Emits one "path<separator>value" line per leaf. Object properties are visited in ordinal order and
    array elements keep their index, so two documents are equal exactly when the sorted line sets match.
    -Operation strips an operation's own "tags" array, which is the only difference a controller split
    is allowed to make.
#>
function Add-CanonicalLines {
    param(
        $Node,
        [string] $Path,
        [System.Collections.Generic.List[string]] $Lines,
        [switch] $Operation
    )

    if ($null -eq $Node) { $Lines.Add("$Path = <null>"); return }

    if ($Node -is [string]) {
        $escaped = $Node.Replace('\', '\\').Replace("`r", '\r').Replace("`n", '\n')
        $Lines.Add("$Path = `"$escaped`"")
        return
    }

    if ($Node -is [bool]) { $Lines.Add("$Path = $($Node.ToString().ToLowerInvariant())"); return }

    if ($Node -is [ValueType]) {
        $Lines.Add("$Path = " + [System.Convert]::ToString($Node, [System.Globalization.CultureInfo]::InvariantCulture))
        return
    }

    if ($Node -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Node) {
            Add-CanonicalLines -Node $item -Path "$Path[$index]" -Lines $Lines
            $index++
        }
        if ($index -eq 0) { $Lines.Add("$Path = <empty array>") }
        return
    }

    $names = @($Node.PSObject.Properties | ForEach-Object { $_.Name } | Sort-Object -CaseSensitive)
    if ($names.Count -eq 0) { $Lines.Add("$Path = <empty object>"); return }

    foreach ($name in $names) {
        if ($Operation -and $name -ceq 'tags') { continue }
        Add-CanonicalLines -Node $Node.$name -Path "$Path.$name" -Lines $Lines
    }
}

function Add-DocumentLines {
    param($Document, [System.Collections.Generic.List[string]] $Lines)

    $root = $Document.PSObject.Properties
    foreach ($property in ($root | Sort-Object Name -CaseSensitive)) {
        if ($property.Name -ceq 'paths' -and $null -ne $property.Value) {
            $paths = $property.Value.PSObject.Properties | Sort-Object Name -CaseSensitive
            foreach ($path in $paths) {
                $methods = @($path.Value.PSObject.Properties | ForEach-Object { $_.Name })
                $operations = @($methods | Where-Object { $OperationMethods -contains $_ })
                if ($operations.Count -eq 0) {
                    Add-CanonicalLines -Node $path.Value -Path "`$.paths.$($path.Name)" -Lines $Lines
                    continue
                }
                foreach ($method in ($methods | Sort-Object -CaseSensitive)) {
                    $operation = $OperationMethods -contains $method
                    Add-CanonicalLines -Node $path.Value.$method -Path "`$.paths.$($path.Name).$method" -Lines $Lines -Operation:$operation
                }
            }
            continue
        }
        Add-CanonicalLines -Node $property.Value -Path "`$.$($property.Name)" -Lines $Lines
    }
}

<#
    The tags each operation declares, keyed by "METHOD path". Used only for reporting: a changed tag is
    expected when actions move between controllers.
#>
function Get-OperationTags {
    param($Document)

    $tags = @{}
    if ($null -eq $Document.paths) { return $tags }

    foreach ($path in ($Document.paths.PSObject.Properties | Sort-Object Name -CaseSensitive)) {
        foreach ($method in ($path.Value.PSObject.Properties.Name | Where-Object { $OperationMethods -contains $_ } | Sort-Object -CaseSensitive)) {
            $declared = $path.Value.$method.tags
            $key = "$($method.ToUpperInvariant()) $($path.Name)"
            $tags[$key] = if ($null -eq $declared) { '<none>' } else { ($declared -join ',') }
        }
    }
    return $tags
}

function Compare-Swagger {
    param([string] $BaseText, [string] $WorkingText)

    try { $baseDocument = $BaseText | ConvertFrom-Json }
    catch { throw "The swagger golden at the base revision is not valid JSON: $($_.Exception.Message)" }
    try { $workingDocument = $WorkingText | ConvertFrom-Json }
    catch { throw "The swagger golden in the working tree is not valid JSON: $($_.Exception.Message)" }

    $baseLines = New-Object 'System.Collections.Generic.List[string]'
    $workingLines = New-Object 'System.Collections.Generic.List[string]'
    Add-DocumentLines -Document $baseDocument -Lines $baseLines
    Add-DocumentLines -Document $workingDocument -Lines $workingLines
    $baseLines.Sort([System.StringComparer]::Ordinal)
    $workingLines.Sort([System.StringComparer]::Ordinal)

    $differences = @()
    $index = 0
    while ($index -lt $baseLines.Count -or $index -lt $workingLines.Count) {
        $base = if ($index -lt $baseLines.Count) { $baseLines[$index] } else { '<end of document>' }
        $working = if ($index -lt $workingLines.Count) { $workingLines[$index] } else { '<end of document>' }
        if ($base -cne $working) {
            $differences += [pscustomobject]@{ Base = $base; Working = $working }
            if ($differences.Count -ge 25) { break }
        }
        $index++
    }

    $baseTags = Get-OperationTags -Document $baseDocument
    $workingTags = Get-OperationTags -Document $workingDocument
    $tagChanges = @()
    foreach ($key in ($baseTags.Keys | Sort-Object -CaseSensitive)) {
        if (-not $workingTags.ContainsKey($key)) { continue }
        if ($baseTags[$key] -cne $workingTags[$key]) {
            $tagChanges += [pscustomobject]@{ Operation = $key; Before = $baseTags[$key]; After = $workingTags[$key] }
        }
    }

    [pscustomobject]@{
        Paths      = @($workingDocument.paths.PSObject.Properties).Count
        Schemas    = if ($null -eq $workingDocument.components) { 0 } else { @($workingDocument.components.schemas.PSObject.Properties).Count }
        Lines      = $workingLines.Count
        Differences = $differences
        TagChanges = $tagChanges
    }
}

# -------------------------------------------------------------------------------------------------
# Success messages: one "route | Controller.Action | VERB | status | query | message" line per action.
# -------------------------------------------------------------------------------------------------

function Read-MessageLines {
    param([string] $Text)

    $parsed = @{}
    $duplicates = @()
    $malformed = @()

    foreach ($line in ($Text -split "`n")) {
        $trimmed = $line.TrimEnd("`r")
        if ([string]::IsNullOrWhiteSpace($trimmed)) { continue }

        $match = [regex]::Match($trimmed, '^(?<route>.*?) \| (?<action>.*?) \| (?<verb>.*?) \| (?<status>.*?) \| (?<query>.*?) \| (?<message>.*)$')
        if (-not $match.Success) { $malformed += $trimmed; continue }

        $key = '{0} | {1} | {2} | {3}' -f $match.Groups['route'].Value, $match.Groups['verb'].Value,
            $match.Groups['status'].Value, $match.Groups['query'].Value
        if ($parsed.ContainsKey($key)) { $duplicates += $key; continue }

        $parsed[$key] = [pscustomobject]@{
            Action  = $match.Groups['action'].Value
            Message = $match.Groups['message'].Value
        }
    }

    [pscustomobject]@{ Lines = $parsed; Duplicates = $duplicates; Malformed = $malformed }
}

function Compare-Messages {
    param([string] $BaseText, [string] $WorkingText)

    $base = Read-MessageLines -Text $BaseText
    $working = Read-MessageLines -Text $WorkingText

    if ($base.Malformed.Count -gt 0) { throw "The base message golden has $($base.Malformed.Count) line(s) that are not 'route | action | verb | status | query | message': $($base.Malformed[0])" }
    if ($working.Malformed.Count -gt 0) { throw "The working-tree message golden has $($working.Malformed.Count) line(s) that are not 'route | action | verb | status | query | message': $($working.Malformed[0])" }
    if ($base.Duplicates.Count -gt 0) { throw "The base message golden repeats the key '$($base.Duplicates[0])'; the comparison needs one line per route/verb/status/query." }
    if ($working.Duplicates.Count -gt 0) { throw "The working-tree message golden repeats the key '$($working.Duplicates[0])'; the comparison needs one line per route/verb/status/query." }

    $differences = @()
    foreach ($key in ($base.Lines.Keys | Sort-Object -CaseSensitive)) {
        if (-not $working.Lines.ContainsKey($key)) {
            $differences += [pscustomobject]@{ Key = $key; Base = $base.Lines[$key].Message; Working = '<line removed>' }
        }
    }
    foreach ($key in ($working.Lines.Keys | Sort-Object -CaseSensitive)) {
        if (-not $base.Lines.ContainsKey($key)) {
            $differences += [pscustomobject]@{ Key = $key; Base = '<line added>'; Working = $working.Lines[$key].Message }
        }
    }
    foreach ($key in ($base.Lines.Keys | Sort-Object -CaseSensitive)) {
        if (-not $working.Lines.ContainsKey($key)) { continue }
        if ($base.Lines[$key].Message -cne $working.Lines[$key].Message) {
            $differences += [pscustomobject]@{ Key = $key; Base = $base.Lines[$key].Message; Working = $working.Lines[$key].Message }
        }
    }

    $actionChanges = @()
    foreach ($key in ($base.Lines.Keys | Sort-Object -CaseSensitive)) {
        if (-not $working.Lines.ContainsKey($key)) { continue }
        if ($base.Lines[$key].Action -cne $working.Lines[$key].Action) {
            $actionChanges += [pscustomobject]@{
                Key    = $key
                Before = $base.Lines[$key].Action
                After  = $working.Lines[$key].Action
            }
        }
    }

    [pscustomobject]@{
        BaseLines     = $base.Lines.Count
        WorkingLines  = $working.Lines.Count
        Differences   = $differences
        ActionChanges = $actionChanges
    }
}

# -------------------------------------------------------------------------------------------------
# Report
# -------------------------------------------------------------------------------------------------

try {
    $baseSwagger = Read-GitFile -Revision $BaseRevision -RelativePath $SwaggerPath
    $workingSwagger = Read-WorkingTreeFile -RelativePath $SwaggerPath
    $baseMessages = Read-GitFile -Revision $BaseRevision -RelativePath $MessagesPath
    $workingMessages = Read-WorkingTreeFile -RelativePath $MessagesPath
}
catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$failed = $false

Write-Host "Base revision : $BaseRevision"
Write-Host "Repository    : $RepositoryRoot"
Write-Host ''

try { $swagger = Compare-Swagger -BaseText $baseSwagger -WorkingText $workingSwagger }
catch {
    Write-Host "FAIL (swagger): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ("Swagger       : {0} paths, {1} component schemas, {2} canonical lines compared" -f $swagger.Paths, $swagger.Schemas, $swagger.Lines)
if ($swagger.TagChanges.Count -eq 0) {
    Write-Host '  tags          : unchanged'
}
else {
    Write-Host ("  tags          : {0} operation(s) changed controller group" -f $swagger.TagChanges.Count)
    foreach ($change in $swagger.TagChanges) {
        Write-Host ("    {0}  {1} -> {2}" -f $change.Operation, $change.Before, $change.After)
    }
}
if ($swagger.Differences.Count -gt 0) {
    $failed = $true
    Write-Host ("  DIFFERENCE    : {0} canonical line(s) differ outside the operation tags" -f $swagger.Differences.Count) -ForegroundColor Red
    $first = $swagger.Differences[0]
    Write-Host "    first difference" -ForegroundColor Red
    Write-Host "      base   : $($first.Base)" -ForegroundColor Red
    Write-Host "      working: $($first.Working)" -ForegroundColor Red
}
Write-Host ''

try { $messages = Compare-Messages -BaseText $baseMessages -WorkingText $workingMessages }
catch {
    Write-Host "FAIL (success messages): $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ("Success messages: {0} base lines, {1} working-tree lines, {2} Controller.Action column(s) compared" -f $messages.BaseLines, $messages.WorkingLines, $messages.BaseLines)
if ($messages.ActionChanges.Count -eq 0) {
    Write-Host '  Controller.Action: unchanged'
}
else {
    Write-Host ("  Controller.Action: {0} changed" -f $messages.ActionChanges.Count)
    foreach ($change in $messages.ActionChanges) {
        Write-Host ("    {0}  {1} -> {2}" -f $change.Key, $change.Before, $change.After)
    }
}
if ($messages.Differences.Count -gt 0) {
    $failed = $true
    Write-Host ("  DIFFERENCE    : {0} line(s) differ outside the Controller.Action column" -f $messages.Differences.Count) -ForegroundColor Red
    $first = $messages.Differences[0]
    Write-Host "    first difference" -ForegroundColor Red
    Write-Host "      key    : $($first.Key)" -ForegroundColor Red
    Write-Host "      base   : $($first.Base)" -ForegroundColor Red
    Write-Host "      working: $($first.Working)" -ForegroundColor Red
}
Write-Host ''

if ($failed) {
    Write-Host 'FAIL: the contract changed beyond swagger tags and the Controller.Action column.' -ForegroundColor Red
    exit 1
}

Write-Host 'PASS: nothing changed except swagger operation tags and the Controller.Action column.' -ForegroundColor Green
exit 0
