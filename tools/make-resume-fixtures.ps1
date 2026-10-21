<#
.SYNOPSIS
  Regenerates the genuine resume files in tests\JobTracker.Core.Tests\Fixtures, which the resume reader tests run against.

.DESCRIPTION
  The files are made by real programs from INVENTED resumes (never a real person's):
    resume-word.docx / resume-word.pdf              Microsoft Word: styles, a list style and a directly formatted list, right-aligned dates, then Word's own PDF export
    resume-word-table.docx / resume-word-table.pdf  Word: a two-column layout built on a table, with the name in the page header
    resume-edge.pdf                                 Microsoft Edge (Chromium) printing HTML: bullet dots are drawn as shapes, not characters
    scanned-image.pdf                               Edge printing a picture of text: a PDF with no text layer
  Needs Word and Edge. It uses only a Word process of its own and stops without touching anything if COM would attach to a Word that is
  already open (so it never touches your documents). The fixtures are committed, so this only needs running to change them.
#>$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path -Parent $PSScriptRoot) 'tests\JobTracker.Core.Tests\Fixtures'
New-Item -ItemType Directory $out -Force | Out-Null
$work = Join-Path $env:TEMP 'jt-fixture-work'
if (Test-Path $work) { [IO.Directory]::Delete($work, $true) }
New-Item -ItemType Directory $work | Out-Null

# ---------- Word: only ever our own instance (the user has Word open) ----------
$before = @(Get-Process WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id)
$word = New-Object -ComObject Word.Application
Start-Sleep -Seconds 3
$after = @(Get-Process WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id)
$mine = @($after | Where-Object { $before -notcontains $_ })
"Word processes before: $($before -join ',')  after: $($after -join ',')  ours: $($mine -join ',')"
if ($mine.Count -eq 0) {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    throw "COM attached to the user's own running Word. Aborting without touching it."
}
if ($word.Documents.Count -ne 0) {
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    throw "This Word instance already has documents open. Aborting without touching it."
}
$word.Visible = $false
$word.DisplayAlerts = 0

# Word writes the signed-in person as the author and last editor of a DOCX. Replace them, so no real name ends up in the repository.
function Scrub-Docx($path) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem   # ZipFile lives in the FileSystem assembly on Windows PowerShell
    $zip = [System.IO.Compression.ZipFile]::Open($path, 'Update')
    try {
        $entry = $zip.GetEntry('docProps/core.xml')
        $reader = New-Object System.IO.StreamReader($entry.Open()); $xml = $reader.ReadToEnd(); $reader.Close()
        $xml = [regex]::Replace($xml, '<dc:creator>.*?</dc:creator>', '<dc:creator>Resume Fixture</dc:creator>')
        $xml = [regex]::Replace($xml, '<cp:lastModifiedBy>.*?</cp:lastModifiedBy>', '<cp:lastModifiedBy>Resume Fixture</cp:lastModifiedBy>')
        $entry.Delete()
        $new = $zip.CreateEntry('docProps/core.xml')
        $writer = New-Object System.IO.StreamWriter($new.Open(), (New-Object System.Text.UTF8Encoding($false))); $writer.Write($xml); $writer.Close()
    }
    finally { $zip.Dispose() }
}

function Save-Both($doc, $name) {
    $docx = Join-Path $out "$name.docx"
    $pdf = Join-Path $out "$name.pdf"
    if (Test-Path $docx) { [IO.File]::Delete($docx) }
    if (Test-Path $pdf) { [IO.File]::Delete($pdf) }
    # PowerShell cannot pass Word's by-reference Variant arguments, so call through reflection
    [void][System.__ComObject].InvokeMember("SaveAs2", [Reflection.BindingFlags]::InvokeMethod, $null, $doc, [object[]]@([string]$docx, [int]16))
    $doc.ExportAsFixedFormat([string]$pdf, [int]17)
    "wrote $name.docx ($((Get-Item $docx).Length) B) and $name.pdf ($((Get-Item $pdf).Length) B)"
}

try {
    # ---- 1. a plain single-column resume: styles, a style-based bullet list, a directly formatted bullet list, right-aligned dates ----
    $doc = $word.Documents.Add()
    $doc.PageSetup.LeftMargin = 72; $doc.PageSetup.RightMargin = 72
    $sel = $word.Selection
    function Para($style, $text) { $sel.Style = $doc.Styles.Item($style); $sel.TypeText($text); $sel.TypeParagraph() }
    function Role($left, $right) {
        $sel.Style = $doc.Styles.Item('Normal')
        $sel.ParagraphFormat.TabStops.ClearAll()
        [void]$sel.ParagraphFormat.TabStops.Add(468, 2)
        $sel.Font.Bold = 1; $sel.TypeText($left); $sel.TypeText("`t$right"); $sel.Font.Bold = 0; $sel.TypeParagraph()
        $sel.ParagraphFormat.TabStops.ClearAll()
    }
    Para 'Title' 'Alex Morgan'
    Para 'Normal' 'Seattle, WA | alex.morgan@example.com | 555-0100 | linkedin.com/in/alexmorgan-example'
    Para 'Heading 1' 'Summary'
    Para 'Normal' 'Backend engineer with eight years of experience building payment and claims systems on .NET and Azure, comfortable owning services from design to on-call and mentoring engineers who are new to distributed systems.'
    Para 'Heading 1' 'Experience'
    Role 'Senior Software Engineer, Contoso Health (Seattle, WA)' '2021 to present'
    Para 'List Bullet' 'Built Kafka consumers that process 40,000 events per second with zero data loss across three regions and two failovers during the quarterly enrollment peak.'
    Para 'List Bullet' 'Cut SQL Server query latency by 63% by redesigning indexes and removing N+1 queries in the claims API.'
    Para 'List Bullet' 'Led a team of four engineers delivering a claims platform on Azure Service Bus and Cosmos DB.'
    Role 'Software Engineer, Fabrikam Logistics (Remote)' '2017 to 2021'
    $sel.Style = $doc.Styles.Item('Normal')
    $sel.Range.ListFormat.ApplyBulletDefault()
    $sel.TypeText('Maintained a C# and .NET Core order service used by 120 warehouses.'); $sel.TypeParagraph()
    $sel.TypeText('Introduced contract tests that caught 30 breaking API changes before release.'); $sel.TypeParagraph()
    $sel.Range.ListFormat.RemoveNumbers()
    Para 'Heading 1' 'Skills'
    Para 'Normal' 'C#, .NET 10, ASP.NET Core, EF Core, SQL Server, Redis, Azure Service Bus, Kafka, Docker'
    Para 'Heading 1' 'Education'
    $sel.Style = $doc.Styles.Item('Normal'); $sel.TypeText('B.S. Computer Science, State University, 2017')
    Save-Both $doc 'resume-word'
    $doc.Close(0)
    Scrub-Docx (Join-Path $out 'resume-word.docx')

    # ---- 2. a two-column layout in a table, with the name in the page header (a very common template shape) ----
    $doc2 = $word.Documents.Add()
    $doc2.PageSetup.LeftMargin = 54; $doc2.PageSetup.RightMargin = 54
    $doc2.Sections.Item(1).Headers.Item(1).Range.Text = 'Jordan Rivers | Austin, TX | jordan.rivers@example.com | 555-0111'
    $range = $doc2.Content
    $tbl = $doc2.Tables.Add($range, 1, 2)
    $tbl.Borders.Enable = 0
    $tbl.Columns.Item(1).Width = 130
    $tbl.Columns.Item(2).Width = 350
    $tbl.Cell(1, 1).Range.Text = "SKILLS`rPython`rPostgreSQL`rAirflow`rTerraform`rAWS"
    $tbl.Cell(1, 2).Range.Text = "EXPERIENCE`rData Engineer, Northwind Traders (Austin, TX) 2020 to present`rBuilt Airflow pipelines that load 2 TB of order data every night into PostgreSQL with automated backfills.`rReduced warehouse build time from six hours to 55 minutes by partitioning the largest tables.`rAnalyst, Tailspin Toys 2017 to 2020`rAutomated the monthly sales report that three teams used to build by hand."
    foreach ($i in 3, 4, 6) { $tbl.Cell(1, 2).Range.Paragraphs.Item($i).Style = $doc2.Styles.Item('List Bullet') }
    Save-Both $doc2 'resume-word-table'
    $doc2.Close(0)
    Scrub-Docx (Join-Path $out 'resume-word-table.docx')
}
finally {
    try { $word.Quit(0) } catch { }
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($word)
    Start-Sleep -Seconds 2
    foreach ($id in $mine) { $p = Get-Process -Id $id -ErrorAction SilentlyContinue; if ($p) { "our Word (pid $id) was still running: stopping only that process"; Stop-Process -Id $id -Force } }
}
"Word processes now (should equal the user's, before: $($before -join ',')): $((Get-Process WINWORD -ErrorAction SilentlyContinue | ForEach-Object Id) -join ',')"

# ---------- Edge (Chromium) prints a real PDF from HTML: a different producer, list markers drawn as text ----------
$edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
$profile = Join-Path $work 'edge-profile'
$html = @'
<!doctype html><html><head><meta charset="utf-8"><style>
body{font-family:Arial;font-size:11pt;margin:0} h1{font-size:22pt;margin:0 0 4px} h2{font-size:12pt;border-bottom:1px solid #444;margin:14px 0 6px;text-transform:uppercase}
.row{display:flex;justify-content:space-between;font-weight:bold} ul{margin:4px 0 8px 22px;padding:0} li{margin:2px 0}
</style></head><body>
<h1>Sam Okafor</h1>
<div>Denver, CO | sam.okafor@example.com | 555-0122</div>
<h2>Summary</h2>
<p>Platform engineer who keeps build systems fast and boring, with a habit of writing the runbook before the incident rather than after it.</p>
<h2>Experience</h2>
<div class="row"><span>Platform Engineer, Adventure Works (Denver, CO)</span><span>2019 to present</span></div>
<ul>
<li>Moved 85 services from hand-built servers to Kubernetes with Helm charts and a shared pipeline, cutting deploys from 40 minutes to six.</li>
<li>Wrote the on-call runbooks that brought the median incident time down by 35%.</li>
<li>Mentored three junior engineers through their first production releases.</li>
</ul>
<div class="row"><span>Systems Administrator, Wide World Importers</span><span>2015 to 2019</span></div>
<ul>
<li>Automated server patching with Ansible across 200 machines.</li>
<li>Replaced a manual backup process with nightly verified restores.</li>
</ul>
<h2>Skills</h2>
<p>Kubernetes, Helm, Terraform, Ansible, Linux, GitHub Actions, Prometheus</p>
</body></html>
'@
$htmlPath = Join-Path $work 'resume-edge.html'
[IO.File]::WriteAllText($htmlPath, $html, (New-Object Text.UTF8Encoding($false)))
$pdf = Join-Path $out 'resume-edge.pdf'; if (Test-Path $pdf) { [IO.File]::Delete($pdf) }
$p = Start-Process $edge -ArgumentList '--headless=new','--disable-gpu','--no-pdf-header-footer',"--user-data-dir=`"$profile`"","--print-to-pdf=`"$pdf`"","file:///$($htmlPath -replace '\\','/')" -PassThru
[void]$p.WaitForExit(60000); Start-Sleep 2
"wrote resume-edge.pdf: $(Test-Path $pdf) ($(if(Test-Path $pdf){(Get-Item $pdf).Length}) B)"

# ---------- a PDF with no text layer: a picture of a resume, printed to PDF by Edge ----------
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 1200, 700
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::White); $g.TextRenderingHint = 'AntiAlias'
$f1 = New-Object System.Drawing.Font 'Arial', 34, ([System.Drawing.FontStyle]::Bold); $f2 = New-Object System.Drawing.Font 'Arial', 20
$g.DrawString('Casey Lindgren', $f1, [System.Drawing.Brushes]::Black, 40, 30)
$g.DrawString('Experience', $f1, [System.Drawing.Brushes]::Black, 40, 130)
$g.DrawString('- Scanned in from paper, so there is no text layer in this file.', $f2, [System.Drawing.Brushes]::Black, 40, 230)
$g.DrawString('- Built an inventory system used by 14 stores.', $f2, [System.Drawing.Brushes]::Black, 40, 290)
$g.Dispose(); $png = Join-Path $work 'scan.png'; $bmp.Save($png, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($png))
$h2 = "<!doctype html><html><body style='margin:0'><img src='data:image/png;base64,$b64' style='width:100%'></body></html>"
$h2Path = Join-Path $work 'scan.html'; [IO.File]::WriteAllText($h2Path, $h2, (New-Object Text.UTF8Encoding($false)))
$pdf2 = Join-Path $out 'scanned-image.pdf'; if (Test-Path $pdf2) { [IO.File]::Delete($pdf2) }
$p = Start-Process $edge -ArgumentList '--headless=new','--disable-gpu','--no-pdf-header-footer',"--user-data-dir=`"$profile`"","--print-to-pdf=`"$pdf2`"","file:///$($h2Path -replace '\\','/')" -PassThru
[void]$p.WaitForExit(60000); Start-Sleep 2
"wrote scanned-image.pdf: $(Test-Path $pdf2) ($(if(Test-Path $pdf2){(Get-Item $pdf2).Length}) B)"

[IO.Directory]::Delete($work, $true)
"--- fixtures ---"; Get-ChildItem $out | ForEach-Object { "{0,-24} {1,8:N0} B" -f $_.Name, $_.Length }
"Edge processes left running: " + @(Get-Process msedge -ErrorAction SilentlyContinue).Count
