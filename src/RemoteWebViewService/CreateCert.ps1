# Set variables
$IP = "192.168.1.35"
$Port = 5002
$DnsName = "localhost"
$CertName = "MySelfSignedCert"
$CertPath = "Cert:\LocalMachine\My"
$PfxPassword = ConvertTo-SecureString -String "YourStrongPassword" -Force -AsPlainText
$PfxPath = "C:\Certificates\MySelfSignedCert.pfx"
$CerPath = "C:\Certificates\MySelfSignedCert.cer"

# Ensure the certificate directory exists
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $PfxPath) | Out-Null

# Create self-signed certificate
$Cert = New-SelfSignedCertificate `
    -Subject "CN=$DnsName" `
    -DnsName $DnsName, $IP `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -NotAfter (Get-Date).AddYears(1) `
    -CertStoreLocation $CertPath `
    -FriendlyName $CertName `
    -HashAlgorithm SHA256 `
    -KeyUsage DigitalSignature, KeyEncipherment, DataEncipherment `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")

# Export certificate to PFX file
$CertThumbprint = $Cert.Thumbprint
Export-PfxCertificate -Cert "$CertPath\$CertThumbprint" -FilePath $PfxPath -Password $PfxPassword

# Display certificate details
Write-Host "Self-signed certificate created for IP $IP and port $Port"
Write-Host "Certificate Thumbprint: $CertThumbprint"
Write-Host "PFX File Path: $PfxPath"
Write-Host "Please keep the PFX password safe: YourStrongPassword"

# Export as CER file (for importing to trusted root on other machines)
Export-Certificate -Cert "$CertPath\$CertThumbprint" -FilePath $CerPath

Write-Host "CER File Path: $CerPath"

#Self-signed certificate created for IP 192.168.1.35 and port 5002
#Certificate Thumbprint: 745B832B3ECC90306BB0E6B8922A2BAE7FEA3304
#PFX File Path: C:\Certificates\MySelfSignedCert.pfx
#Please keep the PFX password safe: YourStrongPassword
-a----         9/13/2024   4:19 AM            800 MySelfSignedCert.cer
#CER File Path: C:\Certificates\MySelfSignedCert.cer