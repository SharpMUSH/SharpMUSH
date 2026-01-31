for ($i = 1; $i -le 3; $i++) {
    Write-Host "=== TEST RUN $i ===" -ForegroundColor Yellow
    
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect("localhost", 4201)
        $stream = $client.GetStream()
        $writer = New-Object System.IO.StreamWriter($stream)
        $writer.AutoFlush = $true
        $buffer = New-Object byte[] 4096
        
        # Discard welcome
        Start-Sleep -Milliseconds 1000
        if ($stream.DataAvailable) {
            $null = $stream.Read($buffer, 0, $buffer.Length)
        }
        
        # Connect
        $writer.WriteLine("connect #1")
        Start-Sleep -Milliseconds 1000
        if ($stream.DataAvailable) {
            $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
            Write-Host "After connect #1:"
            Write-Host ([System.Text.Encoding]::UTF8.GetString($buffer[0..($bytesRead-1)]))
        }
        
        # Look - THIS is where you see the issue
        $writer.WriteLine("look")
        Start-Sleep -Milliseconds 1000
        if ($stream.DataAvailable) {
            $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
            Write-Host "After look:"
            $text = [System.Text.Encoding]::UTF8.GetString($buffer[0..($bytesRead-1)])
            Write-Host $text
            Write-Host "HEX:" ([BitConverter]::ToString($buffer[0..($bytesRead-1)]))
        }
        
    } catch {
        Write-Host "Error: $_"
    } finally {
        if ($client) {
            $client.Close()
        }
    }
    
    Write-Host ""
    Start-Sleep -Milliseconds 500
}
