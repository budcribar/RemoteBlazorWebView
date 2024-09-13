# Run this on the server machine as administrator
$IP = "192.168.1.35"
$DnsName = "localhost"
$CertName = "MySelfSignedCertWithExplicitIP"
$CertPath = "Cert:\LocalMachine\My"
$PfxPassword = "YourStrongPassword"
$SecurePassword = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
$PfxPath = "C:\Certificates\MySelfSignedCertWithExplicitIP.pfx"
$CerPath = "C:\Certificates\MySelfSignedCertWithExplicitIP.cer"

# Ensure the certificate directory exists
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $PfxPath) | Out-Null

# Create a temporary file for OpenSSL configuration
$OpenSSLConfigPath = "C:\Temp\openssl.cnf"
@"
[req]
distinguished_name = req_distinguished_name
x509_extensions = v3_req
prompt = no
[req_distinguished_name]
CN = $DnsName
[v3_req]
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = @alt_names
[alt_names]
DNS.1 = $DnsName
IP.1 = $IP
"@ | Out-File -FilePath $OpenSSLConfigPath -Encoding ascii

# Generate certificate using OpenSSL
$Env:OPENSSL_CONF = $OpenSSLConfigPath
openssl req -x509 -newkey rsa:2048 -keyout temp.key -out temp.crt -days 365 -nodes -subj "/CN=$DnsName"
openssl pkcs12 -export -out $PfxPath -inkey temp.key -in temp.crt -password pass:$PfxPassword

# Import the certificate to the certificate store
try {
    Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation $CertPath -Password $SecurePassword -Exportable
    Write-Host "Certificate imported successfully"
} catch {
    Write-Host "Error importing certificate: $_"
    Write-Host "You may need to manually import the PFX file using the Certificates MMC snap-in"
}

# Export as CER file
$Cert = Get-ChildItem -Path $CertPath | Where-Object { $_.Subject -eq "CN=$DnsName" } | Select-Object -First 1
if ($Cert) {
    Export-Certificate -Cert $Cert -FilePath $CerPath
    Write-Host "Certificate exported to $CerPath"
} else {
    Write-Host "Certificate not found in store. You may need to manually export the CER file."
}

# Clean up temporary files
Remove-Item temp.key, temp.crt, $OpenSSLConfigPath

Write-Host "Certificate created with explicit IP in SAN"
Write-Host "PFX File: $PfxPath"
Write-Host "CER File: $CerPath"
Write-Host "Please ensure you have the PFX and CER files for further use"

#Certificate exported to C:\Certificates\MySelfSignedCertWithExplicitIP.cer
#Certificate created with explicit IP in SAN
#PFX File: C:\Certificates\MySelfSignedCertWithExplicitIP.pfx
#CER File: C:\Certificates\MySelfSignedCertWithExplicitIP.cer