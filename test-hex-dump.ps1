$client = New-Object System.Net.Sockets.TcpClient
try {
    Write-Host "Connecting to localhost:4201..."
    $client.Connect("localhost", 4201)
    Write-Host "Connected!`n"
    
    $stream = $client.GetStream()
    $buffer = New-Object byte[] 4096
    
    # Wait for and display welcome
    Start-Sleep -Milliseconds 1000
    if ($stream.DataAvailable) {
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        $bytes = $buffer[0..($bytesRead-1)]
        Write-Host "=== WELCOME (${bytesRead} bytes) ==="
        Write-Host "Hex: $([BitConverter]::ToString($bytes))"
        Write-Host "Text: $([System.Text.Encoding]::UTF8.GetString($bytes))"
        Write-Host ""
    }
    
    # Send look command
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    Write-Host "Sending: look"
    $writer.WriteLine("look")
    
    Start-Sleep -Milliseconds 2000
    
    if ($stream.DataAvailable) {
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        $bytes = $buffer[0..($bytesRead-1)]
        Write-Host "=== LOOK RESPONSE (${bytesRead} bytes) ==="
        Write-Host "Hex: $([BitConverter]::ToString($bytes))"
        Write-Host "Text: $([System.Text.Encoding]::UTF8.GetString($bytes))"
        Write-Host ""
    }
    
} catch {
    Write-Host "Error: $_"
} finally {
    if ($client) {
        $client.Close()
    }
}
