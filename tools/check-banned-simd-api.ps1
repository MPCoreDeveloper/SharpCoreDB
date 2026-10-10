<#
.SYNOPSIS
    Fails when the banned portable SIMD API (System.Numerics.Vector<T>) reaches product code.

.DESCRIPTION
    .github/SIMD_STANDARDS.md bans System.Numerics.Vector<T> and requires System.Runtime.Intrinsics
    (Vector128/256/512 with an explicit ISA tier chain). This script is that rule's enforcement: nothing
    in the build fails today when someone reintroduces the portable API, which is how the rule came to be
    violated for as long as it was (the last real usage, Vector<byte>.Count in PlatformOptimizations,
    survived until 2026-09-26).

    It scans the product sources (default: src/**/*.cs, excluding obj/ and bin/) for:

      * Vector<T>               the portable generic      — `Vector<int> v`, `Vector<float>.Count`
      * Vector.<member>         the portable static class — `Vector.IsHardwareAccelerated`, `Vector.Sum(..)`
      * System.Numerics.Vector  either of the above, fully qualified or through `using static`

    `using System.Numerics;` on its own is NOT a violation: BitOperations legitimately lives in that
    namespace and every mask-extraction kernel needs it. Only Vector itself is banned. The same goes for
    System.Numerics.Vector2/3/4, which are ordinary structs and not the banned generic.

    Two of the three rules match the *unqualified* names, which a type of your own could also own. They
    are therefore gated on what the scanned file says about itself: an unqualified `Vector.x` is a
    violation only when the file imports System.Numerics and does not declare a member named `Vector`.
    Both facts are read from the file, and this repository has no `global using System.Numerics;`, so a
    real portable usage always carries the import. Everything the gate suppresses is still printed, as a
    `note:` line carrying the reason — visible in the log, never silently dropped.

    Comments and string/char literals are blanked before matching, so the ban annotation this repository
    carries (`using System.Numerics; // BitOperations only — System.Numerics.Vector<T> is banned …`) does
    not trip the guard. Text mentions are still reported, as `note:` lines that never fail the run.

    What it cannot check — these stay on the standard's human review checklist, because syntax cannot
    decide them: the tiered fallback chain, the always-executed scalar tail, FMA usage,
    [MethodImpl(AggressiveOptimization)] on hot paths, and the AVX-512 minimum-element thresholds.

.PARAMETER Root
    Repository root to scan. Defaults to this script's parent directory (tools/ -> repository root).

.PARAMETER Paths
    Directories to scan, relative to Root. Defaults to 'src'.

.PARAMETER SelfTest
    Runs the detector against inline samples with known expectations and fails if any expectation is
    wrong. The real tree is not scanned in this mode; run it after editing this script.

.EXAMPLE
    pwsh -NoProfile -File tools/check-banned-simd-api.ps1

.EXAMPLE
    pwsh -NoProfile -File tools/check-banned-simd-api.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [string]$Root = (Split-Path -Parent $PSScriptRoot),
    [string[]]$Paths = @('src'),
    [switch]$SelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The banned surface, exactly as .github/SIMD_STANDARDS.md defines it.
# - (?<![\w.>]) keeps longer identifiers out: Vector128/256/512, MyVector, IVector, foo.Vector.
# - (?!\d) on the third rule keeps the legitimate System.Numerics.Vector2/3/4 out.
# - RequiresNumericsImport marks the two rules whose *unqualified* form can only bind to the portable
#   API when the file imports that namespace. It is not a loophole: the namespace import is read from
#   the file being scanned, and this repository has no `global using System.Numerics;`, so a real usage
#   always carries the import. It exists because `Vector.Length` in a file that declares its own
#   `float[] Vector` member (src/SharpCoreDB.VectorSearch/Index/HnswNode.cs) is that member, not SIMD.
$BannedPatterns = @(
    [pscustomobject]@{ Rule = 'Vector<T> (portable generic)';       Pattern = [regex]'(?<![\w.>])Vector\s*<';        RequiresNumericsImport = $false }
    [pscustomobject]@{ Rule = 'Vector static member';               Pattern = [regex]'(?<![\w.>])Vector\s*\.\s*\w+';  RequiresNumericsImport = $true }
    [pscustomobject]@{ Rule = 'System.Numerics.Vector (qualified)'; Pattern = [regex]'System\.Numerics\.Vector(?!\d)'; RequiresNumericsImport = $false }
)

function Get-CodeLine {
    <#
        Returns one entry per input line: `Number`, `Code` (the line with comments and string/char
        literals blanked to spaces, so line and column numbers stay true) and `Raw` (the untouched line).

        The lexer is deliberately small: line comments, block comments, "…" with escapes, @"…" with ""
        escapes, """…""" raw strings, and 'c' char literals. Block comments, verbatim strings and raw
        strings carry their state across lines. Known limitation, stated rather than hidden: the *holes*
        of an interpolated string ({ … }) sit inside the literal and are not scanned, so a banned usage
        there would be missed — it would also be a bizarre place to write one.
    #>
    param([AllowEmptyCollection()][string[]]$Lines)

    $result = [System.Collections.Generic.List[object]]::new()
    $state = 'code'   # code | line | block | string | verbatim | raw | char
    $lineNo = 0

    foreach ($line in $Lines) {
        $lineNo++
        $chars = $line.ToCharArray()
        $blank = [bool[]]::new($chars.Length)

        for ($i = 0; $i -lt $chars.Length; $i++) {
            $c = $chars[$i]
            $next = if ($i + 1 -lt $chars.Length) { $chars[$i + 1] } else { [char]0 }

            if ($state -eq 'code') {
                if ($c -eq '/' -and $next -eq '/') {
                    $blank[$i] = $true; $blank[$i + 1] = $true; $i++; $state = 'line'
                }
                elseif ($c -eq '/' -and $next -eq '*') {
                    $blank[$i] = $true; $blank[$i + 1] = $true; $i++; $state = 'block'
                }
                elseif ($c -eq '"') {
                    $blank[$i] = $true
                    if ($next -eq '"' -and ($i + 2) -lt $chars.Length -and $chars[$i + 2] -eq '"') {
                        $blank[$i + 1] = $true; $blank[$i + 2] = $true; $i += 2; $state = 'raw'
                    }
                    elseif ($i -gt 0 -and $chars[$i - 1] -eq '@') {
                        $state = 'verbatim'
                    }
                    else {
                        $state = 'string'
                    }
                }
                elseif ($c -eq "'") {
                    $blank[$i] = $true; $state = 'char'
                }
            }
            elseif ($state -eq 'line') {
                $blank[$i] = $true
            }
            elseif ($state -eq 'block') {
                $blank[$i] = $true
                if ($c -eq '*' -and $next -eq '/') { $blank[$i + 1] = $true; $i++; $state = 'code' }
            }
            elseif ($state -eq 'string') {
                $blank[$i] = $true
                if ($c -eq '\') { if ($i + 1 -lt $chars.Length) { $blank[$i + 1] = $true; $i++ } }
                elseif ($c -eq '"') { $state = 'code' }
            }
            elseif ($state -eq 'verbatim') {
                $blank[$i] = $true
                if ($c -eq '"') {
                    if ($next -eq '"') { $blank[$i + 1] = $true; $i++ } else { $state = 'code' }
                }
            }
            elseif ($state -eq 'raw') {
                $blank[$i] = $true
                if ($c -eq '"' -and $next -eq '"' -and ($i + 2) -lt $chars.Length -and $chars[$i + 2] -eq '"') {
                    $blank[$i + 1] = $true; $blank[$i + 2] = $true; $i += 2; $state = 'code'
                }
            }
            elseif ($state -eq 'char') {
                $blank[$i] = $true
                if ($c -eq '\') { if ($i + 1 -lt $chars.Length) { $blank[$i + 1] = $true; $i++ } }
                elseif ($c -eq "'") { $state = 'code' }
            }
        }

        # A line comment ends at the newline; every other state survives into the next line.
        if ($state -eq 'line') { $state = 'code' }

        $codeChars = [char[]]::new($chars.Length)
        for ($j = 0; $j -lt $chars.Length; $j++) {
            $codeChars[$j] = if ($blank[$j]) { ' ' } else { $chars[$j] }
        }

        $result.Add([pscustomobject]@{ Number = $lineNo; Code = [string]::new($codeChars); Raw = $line })
    }

    return $result
}

function Find-BannedSimdApi {
    <#
        Returns every finding for one file's lines. At most one finding per line: a failure
        (`IsNote = $false`) when the banned API appears in code, otherwise a note (`IsNote = $true`)
        carrying the `Reason` it is not one. Notes never fail the run.
    #>
    param([AllowEmptyCollection()][string[]]$Lines)

    # Two facts about the file decide whether an unqualified `Vector.x` can be the portable static
    # class at all. Both are read from the file rather than guessed:
    #   * the namespace must be imported — without it the name binds to a member of the file's own type
    #     (`Vector.Length` in src/SharpCoreDB.VectorSearch/Index/HnswNode.cs is that node's float[]);
    #   * a file that declares its own member named `Vector` uses that name for the member.
    $importsNumerics = $false
    $declaresVectorMember = $false

    foreach ($raw in $Lines) {
        $stripped = $raw -replace '//.*$', ''
        if ($stripped -match '^\s*(?:global\s+)?using\s+(?:static\s+)?System\.Numerics\s*;') { $importsNumerics = $true }
        if ($stripped -match '[\w<>\[\],\.\?]\s+Vector\s*[;=]') { $declaresVectorMember = $true }
    }

    $found = [System.Collections.Generic.List[object]]::new()

    foreach ($entry in Get-CodeLine -Lines $Lines) {
        $decided = $false

        foreach ($rule in $BannedPatterns) {
            if ($decided) { break }

            $codeMatch = $rule.Pattern.Match($entry.Code)
            if ($codeMatch.Success) {
                $reason = $null
                if ($rule.RequiresNumericsImport -and -not $importsNumerics) {
                    $reason = 'no System.Numerics import in this file — the name binds to a member of its own type'
                }
                elseif ($rule.RequiresNumericsImport -and $declaresVectorMember) {
                    $reason = 'this file declares its own member named Vector'
                }

                if ($null -eq $reason) {
                    $found.Add([pscustomobject]@{
                        Number = $entry.Number; Rule = $rule.Rule; Match = $codeMatch.Value
                        Text = $entry.Raw.Trim(); IsNote = $false; Reason = ''
                    })
                    $decided = $true
                    continue
                }

                $found.Add([pscustomobject]@{
                    Number = $entry.Number; Rule = $rule.Rule; Match = $codeMatch.Value
                    Text = $entry.Raw.Trim(); IsNote = $true; Reason = $reason
                })
                $decided = $true
                continue
            }

            $rawMatch = $rule.Pattern.Match($entry.Raw)
            if ($rawMatch.Success) {
                $found.Add([pscustomobject]@{
                    Number = $entry.Number; Rule = $rule.Rule; Match = $rawMatch.Value
                    Text = $entry.Raw.Trim(); IsNote = $true; Reason = 'appears only in a comment or string'
                })
                $decided = $true
            }
        }
    }


    return $found
}

function Invoke-SelfTest {
    <#
        Proves the detector before it is trusted: positive samples must fail, and the forms that made
        this guard necessary (the repository's own ban annotations, string literals, and neighbouring
        identifiers such as Vector128 and Vector4) must not.
    #>
    $cases = @(
        # --- must NOT fail -------------------------------------------------
        [pscustomobject]@{ Expect = 'clean'; Why = 'plain import for BitOperations';        Lines = @('using System.Numerics;') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'BitOperations is permitted';            Lines = @('var n = System.Numerics.BitOperations.TrailingZeroCount(mask);') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'Vector128 is the required API';         Lines = @('Vector128<int> a = Vector128.LoadUnsafe(ref data);') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'Vector256 is the required API';         Lines = @('var total = Vector256.Sum(vAcc);') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'Vector512 is the required API';         Lines = @('Vector512<double> v = Avx512F.Add(a, b);') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'Vector4 is a different type';           Lines = @('var t = System.Numerics.Vector4.Zero;') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'identifier ends with Vector';           Lines = @('MyVector<int> v = new();') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'identifier starts with Vector';         Lines = @('IVector<int> v = factory.Create();') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'a Vector member on a value';            Lines = @('var v = q.Vector;') }
        [pscustomobject]@{ Expect = 'clean'; Why = 'string without the banned text';        Lines = @('var s = "Vector";') }
        # --- mentions: reported, never fatal -------------------------------
        [pscustomobject]@{ Expect = 'note';  Why = 'the repository ban annotation';         Lines = @('using System.Numerics; // BitOperations only — System.Numerics.Vector<T> is banned by .github/SIMD_STANDARDS.md') }
        [pscustomobject]@{ Expect = 'note';  Why = 'annotation after an intrinsics using';  Lines = @('using System.Runtime.Intrinsics.X86; // explicit tiers only — System.Numerics.Vector<T> must not come back') }
        [pscustomobject]@{ Expect = 'note';  Why = 'line-comment mention';                  Lines = @('// Vector<T> was the old form') }
        [pscustomobject]@{ Expect = 'note';  Why = 'block-comment mention';                 Lines = @('/* Vector<float>.Count was removed */') }
        [pscustomobject]@{ Expect = 'note';  Why = 'block comment over several lines';      Lines = @('/* starts here', 'Vector<int> v;', '*/') }
        [pscustomobject]@{ Expect = 'note';  Why = 'mention inside a string literal';       Lines = @('var msg = "Vector<T> is banned";') }
        [pscustomobject]@{ Expect = 'note';  Why = 'multi-line verbatim string';            Lines = @('var s = @"verbatim', 'Vector<int> inside";') }
        [pscustomobject]@{ Expect = 'note';  Why = 'multi-line raw string';                 Lines = @('var s = """', 'Vector<int> raw', '""";') }
        [pscustomobject]@{ Expect = 'note';  Why = 'own member Vector, no import';          Lines = @('namespace X;', 'internal sealed class N { internal readonly float[] Vector; internal long B => Vector.Length * 4; }') }
        [pscustomobject]@{ Expect = 'note';  Why = 'own member Vector with the import';     Lines = @('using System.Numerics;', 'internal sealed class N { private int[] Vector; internal int L => Vector.Length; }') }
        # --- must fail -----------------------------------------------------
        [pscustomobject]@{ Expect = 'fail';  Why = 'portable generic';                      Lines = @('Vector<int> v = default;') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'portable generic member';               Lines = @('var width = Vector<float>.Count;') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'portable static check with the import'; Lines = @('using System.Numerics;', 'if (Vector.IsHardwareAccelerated) { }') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'portable static reduction, imported';   Lines = @('using System.Numerics;', 'var total = Vector.Sum(vAcc);') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'portable cast';                         Lines = @('var vecs = MemoryMarshal.Cast<float, Vector<float>>(span);') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'fully qualified generic';               Lines = @('global::System.Numerics.Vector<double> v = default;') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'using static import';                   Lines = @('using static System.Numerics.Vector;') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'code after an escaped verbatim string'; Lines = @('var s = @"a "" b"; Vector<int> v = default;') }
        [pscustomobject]@{ Expect = 'fail';  Why = 'code after a char literal';             Lines = @("var c = 'x'; Vector<int> v = default;") }
    )

    $passed = 0
    $failed = 0

    foreach ($case in $cases) {
        $findings = @(Find-BannedSimdApi -Lines $case.Lines)
        $fails = @($findings | Where-Object { -not $_.IsNote })
        $notes = @($findings | Where-Object { $_.IsNote })
        $actual = if ($fails.Count -gt 0) { 'fail' } elseif ($notes.Count -gt 0) { 'note' } else { 'clean' }

        if ($actual -eq $case.Expect) { $passed++; continue }

        $failed++
        Write-Host ("SELFTEST FAIL  expected '{0}', detected '{1}' — {2}" -f $case.Expect, $actual, $case.Why)
        Write-Host ("               {0}" -f ($case.Lines -join ' / '))
    }

    $verdict = if ($failed -gt 0) { 'FAIL' } else { 'OK' }
    Write-Host ("SELFTEST {0} — {1} passed, {2} failed ({3} sample(s))" -f $verdict, $passed, $failed, $cases.Count)

    if ($failed -gt 0) { exit 1 }
}

function Invoke-Scan {
    <#
        Prints one line per finding — `path:line  [rule]  text` for a violation, `note: …` for a mere
        mention — then a verdict, and sets the exit code: 1 when the banned API is present, 0 when it is
        not, 2 when there was nothing to scan.
    #>
    param([string]$RepoRoot, [string[]]$ScanPaths)

    $files = @(
        foreach ($rel in $ScanPaths) {
            $dir = Join-Path $RepoRoot $rel
            if (-not (Test-Path -LiteralPath $dir)) {
                Write-Warning ("scan path not found, skipped: {0} (root {1})" -f $rel, $RepoRoot)
                continue
            }

            Get-ChildItem -LiteralPath $dir -Recurse -File -Filter *.cs |
                Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
        }
    )

    if ($files.Count -eq 0) {
        Write-Warning 'nothing to scan — check -Root and -Paths'
        exit 2
    }

    $violations = 0
    $notes = 0

    foreach ($file in $files) {
        $rel = [System.IO.Path]::GetRelativePath($RepoRoot, $file.FullName) -replace '\\', '/'
        $findings = @(Find-BannedSimdApi -Lines ([System.IO.File]::ReadAllLines($file.FullName)))

        foreach ($finding in $findings) {
            if ($finding.IsNote) {
                $notes++
                Write-Host ("note: {0}:{1}  '{2}' — {3} (not a violation)" -f $rel, $finding.Number, $finding.Match, $finding.Reason)
                continue
            }

            $violations++
            Write-Host ("{0}:{1}  [{2}]  {3}" -f $rel, $finding.Number, $finding.Rule, $finding.Text)
        }
    }

    Write-Host ''

    if ($violations -gt 0) {
        Write-Host ("FAIL — {0} banned portable-SIMD usage(s) in {1} scanned file(s)." -f $violations, $files.Count)
        Write-Host '       Migrate to System.Runtime.Intrinsics with the tiered chain required by .github/SIMD_STANDARDS.md.'
        exit 1
    }

    Write-Host ("OK — no System.Numerics.Vector<T> usage in {0} scanned file(s); {1} comment/string mention(s), listed above." -f $files.Count, $notes)
    exit 0
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$resolvedRoot = (Resolve-Path -LiteralPath $Root -ErrorAction Stop).Path
Write-Host ("Checking {0} under {1} for the banned portable SIMD API (.github/SIMD_STANDARDS.md)…" -f ($Paths -join ', '), $resolvedRoot)
Invoke-Scan -RepoRoot $resolvedRoot -ScanPaths $Paths
