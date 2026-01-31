$client = New-Object System.Net.Sockets.TcpClient
try {
    Write-Host "Connecting to localhost:4201..."
    $client.Connect("localhost", 4201)
    Write-Host "Connected!"
    
    $stream = $client.GetStream()
    $reader = New-Object System.IO.StreamReader($stream)
    $writer = New-Object System.IO.StreamWriter($stream)
    $writer.AutoFlush = $true
    
    # Wait for initial message
    Start-Sleep -Milliseconds 500
    
    # Read any available data
    $buffer = New-Object byte[] 4096
    if ($stream.DataAvailable) {
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
        Write-Host "Received: $response"
    } else {
        Write-Host "No data received yet"
    }
    
    # Try sending a command
    Write-Host "`nSending: connect #1"
    $writer.WriteLine("connect #1")
    
    # Wait for response
    Start-Sleep -Milliseconds 500
    
    if ($stream.DataAvailable) {
        $bytesRead = $stream.Read($buffer, 0, $buffer.Length)
        $response = [System.Text.Encoding]::UTF8.GetString($buffer, 0, $bytesRead)
        Write-Host "Received: $response"
    } else {
        Write-Host "No response to command"
    }
    
} catch {
    Write-Host "Error: $_"
} finally {
    $client.Close()
    Write-Host "`nConnection closed"
}
