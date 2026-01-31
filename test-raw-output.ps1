$client = New-Object System.Net.Sockets.TcpClient
try {
    Write-Host "Connecting to localhost:4201..."
    $client.Connect("localhost", 4201)
    
    $stream = $client.GetStream()
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    $buffer = New-Object byte[] 4096
    
    function Read-AllBytes {
        param([int]$waitMs = 1000)
        Start-Sleep -Milliseconds $waitMs
        $allBytes = @()
        while ($stream.DataAvailable) {
            $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
            $allBytes += $buffer[0..($bytesRead-1)]
        }
        return $allBytes
    }
    
    function Send-Command($cmd) {
        Write-Host "SENDING: $cmd"
        $writer.WriteLine($cmd)
        $bytes = Read-AllBytes
        if ($bytes.Length -gt 0) {
            $text = [System.Text.Encoding]::UTF8.GetString($bytes)
            Write-Host "RAW OUTPUT:"
            Write-Host $text
            Write-Host "---"
        }
    }
    
    # Wait for welcome
    $welcomeBytes = Read-AllBytes
    if ($welcomeBytes.Length -gt 0) {
        Write-Host "INITIAL CONNECTION:"
        Write-Host ([System.Text.Encoding]::UTF8.GetString($welcomeBytes))
        Write-Host "---"
    }
    
    # Run commands
    Send-Command "connect #1"
    Send-Command "look"
    Send-Command "+where"
    Send-Command "l God"
    Send-Command "+events"
    Send-Command "WHO"
    Send-Command "asdasd"
    
} catch {
    Write-Host "Error: $_"
} finally {
    if ($client) {
        $client.Close()
    }
}
