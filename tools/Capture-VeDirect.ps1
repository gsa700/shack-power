# Captures raw VE.Direct bytes from the SmartShunt to a file, for committing real protocol
# fixtures (the byte-level framer tests were authored against constructed frames until cutover).
# Needs the port free - close Shack Power (or the old prototype) first.
param(
    [string]$Port = "COM13",
    [int]$Seconds = 10,
    [string]$OutFile = "vedirect-capture.bin"
)

$sp = New-Object System.IO.Ports.SerialPort($Port, 19200, "None", 8, "One")
$sp.ReadTimeout = 2000
$sp.Open()
try {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $bytes = New-Object System.Collections.Generic.List[byte]
    $buf = New-Object byte[] 512
    while ((Get-Date) -lt $deadline) {
        $n = $sp.BaseStream.Read($buf, 0, $buf.Length)
        if ($n -gt 0) { for ($i = 0; $i -lt $n; $i++) { $bytes.Add($buf[$i]) } }
    }
    [System.IO.File]::WriteAllBytes($OutFile, $bytes.ToArray())
    Write-Host "wrote $($bytes.Count) bytes to $OutFile"
} finally {
    $sp.Close()
}
