# Add certificate to Trusted Root store
$CerPath = "C:\Certificates\MySelfSignedCert.cer"
$CertStore = "Cert:\LocalMachine\Root"

# Import the certificate to the Trusted Root store
Import-Certificate -FilePath $CerPath -CertStoreLocation $CertStore

Write-Host "Certificate has been added to the Trusted Root store."

# Configure PowerShell to trust the certificate for HTTPS connections
Add-Type @"
    using System.Net;
    using System.Security.Cryptography.X509Certificates;
    public class TrustAllCertsPolicy : ICertificatePolicy {
        public bool CheckValidationResult(
            ServicePoint srvPoint, X509Certificate certificate,
            WebRequest request, int certificateProblem) {
            return true;
        }
    }
"@
[System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAllCertsPolicy

Write-Host "PowerShell has been configured to trust all certificates for this session."