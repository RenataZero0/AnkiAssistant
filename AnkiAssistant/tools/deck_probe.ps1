$ErrorActionPreference = 'Stop'
$curl = 'C:\WINDOWS\system32\curl.exe'
$bodyFile = Join-Path $env:TEMP 'ac_body.json'
$model = 'AnkiAssistant \u81ea\u68c0\u5361'
$flds = '{"\u5355\u8bcd":"deckprobe","\u97f3\u6807":"x","\u4e2d\u6587":"y"}'

function Raw([string]$action, [string]$paramsJson) {
    $b = '{"action":"' + $action + '","version":6,"params":' + $paramsJson + '}'
    [System.IO.File]::WriteAllText($bodyFile, $b, (New-Object System.Text.UTF8Encoding($false)))
    return (& $curl -s -X POST -H "Content-Type: application/json; charset=utf-8" --data-binary "@$bodyFile" 'http://127.0.0.1:8765')
}
function Parse([string]$json) { return ($json | ConvertFrom-Json) }

function AddTest([string]$deck) {
    $p = '{"note":{"deckName":"' + $deck + '","modelName":"' + $model + '","fields":' + $flds + ',"tags":[],"options":{"allowDuplicate":true,"duplicateScope":"deck"}}}'
    $r = Parse (Raw 'addNote' $p)
    if ($r.error) { Write-Host ('  addNote error: ' + $r.error); return 0 }
    $nid = [long]$r.result
    $cards = (Parse (Raw 'findCards' ('{"query":"nid:' + $nid + '"}'))).result
    if (-not $cards) { Write-Host ('  note ' + $nid + ' -> NO CARDS'); return $nid }
    $ci = (Parse (Raw 'cardsInfo' ('{"cards":[' + ($cards -join ',') + ']}'))).result
    $where = $ci[0].deckName
    $ok = '  [MISMATCH] note ' + $nid + ' -> card deck = [' + $where + ']  wanted [' + $deck + ']'
    if ($where -eq $deck) { $ok = '  [OK] note ' + $nid + ' -> card deck = [' + $where + ']' }
    Write-Host $ok
    return $nid
}

Write-Host '=== 1) existing user deck: A Level Pure Mathematics ==='
$n1 = AddTest 'A Level Pure Mathematics'

Write-Host '=== 2) freshly created top-level deck: AAProbe2 ==='
$null = Raw 'createDeck' '{"deck":"AAProbe2"}'
Write-Host ('  AAProbe2 in deckNames: ' + (((Parse (Raw 'deckNames' '{}')).result) -contains 'AAProbe2'))
$n2 = AddTest 'AAProbe2'

Write-Host '=== 3) fresh subdeck: AAProbe2::Sub ==='
$null = Raw 'createDeck' '{"deck":"AAProbe2::Sub"}'
$n3 = AddTest 'AAProbe2::Sub'

Write-Host '=== 4) cleanup ==='
$ids = @()
foreach ($n in @($n1, $n2, $n3)) { if ($n -gt 0) { $ids += $n } }
if ($ids.Count -gt 0) {
    $null = Raw 'deleteNotes' ('{"notes":[' + ($ids -join ',') + ']}')
    Write-Host ('  deleted probe notes: ' + $ids.Count)
}
$null = Raw 'deleteDecks' '{"decks":["AAProbe2::Sub","AAProbe2"],"cardsToo":true}'
Write-Host ('  leftover AAProbe decks: ' + ((((Parse (Raw 'deckNames' '{}')).result) | Where-Object { $_ -like 'AAProbe*' }) -join ','))

Write-Host '=== 5) user collection recheck ==='
Write-Host ('  total cards = ' + @((Parse (Raw 'findCards' '{"query":"*"}')).result).Count)
Write-Host ('  deck count  = ' + @((Parse (Raw 'deckNames' '{}')).result).Count)
Write-Host ('  notes added today = ' + @((Parse (Raw 'findNotes' '{"query":"added:1"}')).result).Count)
