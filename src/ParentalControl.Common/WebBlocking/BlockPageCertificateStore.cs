using System.Net;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace ParentalControl.Common.WebBlocking;

/// <summary>Creates and caches a local root CA plus per-host leaf certificates so blocked sites
/// can be served a real browser page over HTTPS instead of a certificate error.</summary>
[SupportedOSPlatform("windows")]
public sealed class BlockPageCertificateStore
{
    private const string RootSubject = "CN=ParentalControl Block Page Root";
    private readonly object _sync = new();
    private readonly Dictionary<string, X509Certificate2> _leafCertificates = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<BlockPageCertificateStore> _logger;

    private readonly X509Certificate2 _rootCertificate;

    public BlockPageCertificateStore(ILogger<BlockPageCertificateStore> logger)
    {
        _logger = logger;
        PathsConfig.EnsureFoldersExist();
        _rootCertificate = LoadOrCreateRootCertificate();
    }

    public X509Certificate2 GetCertificateForHost(string hostName)
    {
        var normalizedHost = NormalizeHost(hostName);
        lock (_sync)
        {
            if (_leafCertificates.TryGetValue(normalizedHost, out var certificate))
            {
                return certificate;
            }

            var newCertificate = CreateLeafCertificate(normalizedHost);
            _leafCertificates[normalizedHost] = newCertificate;
            return newCertificate;
        }
    }

    private static string NormalizeHost(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            return "blocked.local";
        }

        return hostName.Trim().Trim('.');
    }

    private X509Certificate2 LoadOrCreateRootCertificate()
    {
        var certDir = Path.Combine(PathsConfig.AppDataFolder, "certs");
        Directory.CreateDirectory(certDir);

        var pfxPath = Path.Combine(certDir, "block-page-root.pfx");
        var passwordPath = Path.Combine(certDir, "block-page-root.pwd");
        string password;

        if (File.Exists(pfxPath) && File.Exists(passwordPath))
        {
            password = File.ReadAllText(passwordPath);
            var stored = X509CertificateLoader.LoadPkcs12FromFile(
                pfxPath,
                password,
                X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
            EnsureTrustedInRootStore(stored);
            return stored;
        }

        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            RootSubject,
            rootKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));

        // CreateSelfSigned already returns a certificate with rootKey attached as its private key.
        var rootWithKey = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(passwordPath, password);
        File.WriteAllBytes(pfxPath, rootWithKey.Export(X509ContentType.Pfx, password));
        EnsureTrustedInRootStore(rootWithKey);
        return X509CertificateLoader.LoadPkcs12FromFile(
            pfxPath,
            password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);
    }

    // Trusting the root CA needs admin rights; without it the block page still serves, just with a browser cert warning.
    private void EnsureTrustedInRootStore(X509Certificate2 certificate)
    {
        try
        {
            using var store = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
            store.Open(OpenFlags.ReadWrite);
            if (!store.Certificates.Cast<X509Certificate2>().Any(existing => existing.Thumbprint == certificate.Thumbprint))
            {
                store.Add(certificate);
            }
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Unable to add the block-page root certificate to the trusted root store - service must run with administrator privileges. HTTPS block pages will show a certificate warning until this is resolved.");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unable to add the block-page root certificate to the trusted root store - service must run with administrator privileges. HTTPS block pages will show a certificate warning until this is resolved.");
        }
    }

    private X509Certificate2 CreateLeafCertificate(string hostName)
    {
        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={hostName}",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName(hostName);
        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        var certificate = request.Create(_rootCertificate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), serial);
        return certificate.CopyWithPrivateKey(leafKey);
    }
}