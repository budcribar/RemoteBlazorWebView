# Run this on the server machine as administrator
$IP = "192.168.1.35"
$DnsName = "localhost"
$CertName = "DevCertificate_$IP"
$CertPath = "Cert:\LocalMachine\My"
$PfxPassword = "YourStrongPassword"
$SecurePassword = ConvertTo-SecureString -String $PfxPassword -Force -AsPlainText
$PfxPath = "C:\Certificates\DevCertificate_$IP.pfx"
$CerPath = "C:\Certificates\DevCertificate_$IP.cer"

# Ensure the certificate directory exists
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $PfxPath) | Out-Null

# Create the SAN entries
$san = @("IPAddress=$IP", "DNS=$DnsName")
$sanText = [string]::Join("&", $san)

# Generate certificate with IP address in SAN as IPAddress and localhost as DNS
$cert = New-SelfSignedCertificate `
  -Subject "CN=$IP" `
  -CertStoreLocation $CertPath `
  -FriendlyName $CertName `
  -NotAfter (Get-Date).AddYears(10) `
  -KeyUsage DigitalSignature, KeyEncipherment `
  -KeyExportPolicy Exportable `
  -KeyAlgorithm RSA `
  -KeyLength 2048 `
  -HashAlgorithm SHA256 `
  -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
  -TextExtension @("2.5.29.17={text}$sanText")

# Export PFX
Export-PfxCertificate -Cert $cert -FilePath $PfxPath -Password $SecurePassword

# Export CER
Export-Certificate -Cert $cert -FilePath $CerPath -Type CERT

Write-Host "Certificate created with IP ($IP) as primary subject and localhost in SAN"
Write-Host "PFX File: $PfxPath"
Write-Host "CER File: $CerPath"
Write-Host "Please ensure you have the PFX and CER files for further use"

# Display certificate details
Write-Host "`nCertificate Details:"
$cert | Format-List Subject, DnsNameList, Thumbprint

Write-Host "`nSubject Alternative Name Extension Details:"
$sanExtension = $cert.Extensions | Where-Object {$_.Oid.FriendlyName -eq "Subject Alternative Name"}
if ($sanExtension) {
    $sanASN = New-Object System.Security.Cryptography.AsnEncodedData ($sanExtension.Oid, $sanExtension.RawData)
    Write-Host $sanASN.Format($true)
} else {
    Write-Host "No Subject Alternative Name extension found."
}
#PFX File: C:\Certificates\DevCertificate_192.168.1.35.pfx
#CER File: C:\Certificates\DevCertificate_192.168.1.35.cer