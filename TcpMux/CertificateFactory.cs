using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace TcpMux
{
    public class CertificateFactory
    {
        public const string TcpMuxCASubject = "DO_NOT_TRUST__TCPMUX_CA";
        public static readonly string TcpMuxCASubjectDN = $"CN={TcpMuxCASubject}";
        public static bool Verbose { get; set; }

        public static X509Certificate2 GenerateCertificate(string subjectName, X509Certificate2? issuerCertificate = null,
            bool generateCA = false, int keyStrength = 2048)
        {
            if (Verbose)
                Console.WriteLine($"Generating {(issuerCertificate != null ? "" : "self-signed ")}certificate for {subjectName}");

            // Generating Random Numbers
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            var certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(long.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Signature Algorithm
            const string signatureAlgorithm = "SHA256WithRSA";

            // Issuer and Subject Name
            var subjectDN = new X509Name(subjectName);
            var issuerDN = issuerCertificate != null ? new X509Name(issuerCertificate.Subject) : subjectDN;
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For the next 2 year
            var notBefore = DateTime.UtcNow.Date;
            var notAfter = notBefore.AddYears(2);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public/Private Key Pair
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            if (generateCA)
            {
                // Indicate we're a CA
                certificateGenerator.AddExtension(
                    X509Extensions.BasicConstraints.Id, true, new BasicConstraints(cA: true));
            }

            // Generating the Certificate
            // For CA-signed certificate, we use the issuer private key, or for self-signed we use the cert's own key
            AsymmetricKeyParameter signingPrivateKey;
            if (issuerCertificate != null)
            {
                var issuerKeyPair = DotNetUtilities.GetKeyPair(issuerCertificate.PrivateKey);
                var issuerSerialNumber = new BigInteger(issuerCertificate.GetSerialNumber());
                AddAuthorityKeyIdentifier(certificateGenerator, issuerDN, issuerKeyPair.Public, issuerSerialNumber);
                signingPrivateKey = issuerKeyPair.Private;
            }
            else
            {
                signingPrivateKey = subjectKeyPair.Private;
            }

            var signatureFactory = new Asn1SignatureFactory(signatureAlgorithm, signingPrivateKey, random);
            var certificate = certificateGenerator.Generate(signatureFactory);

            // merge into X509Certificate2
            var x509Cert = new X509Certificate2(DotNetUtilities.ToX509Certificate(certificate));
            var rsaKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)subjectKeyPair.Private);
#if NET452
            x509Cert.PrivateKey = rsaKey;
            return x509Cert;
#else
            return x509Cert.CopyWithPrivateKey(rsaKey);
#endif
        }

        /// <summary>
        /// Add the Authority Key Identifier. According to http://www.alvestrand.no/objectid/2.5.29.35.html, this
        /// identifies the public key to be used to verify the signature on this certificate.
        /// In a certificate chain, this corresponds to the "Subject Key Identifier" on the *issuer* certificate.
        /// The Bouncy Castle documentation, at http://www.bouncycastle.org/wiki/display/JA1/X.509+Public+Key+Certificate+and+Certification+Request+Generation,
        /// shows how to create this from the issuing certificate. Since we're creating a self-signed certificate, we have to do this slightly differently.
        /// </summary>
        private static void AddAuthorityKeyIdentifier(X509V3CertificateGenerator certificateGenerator,
            X509Name issuerDN,
            AsymmetricKeyParameter issuerPublicKey,
            BigInteger issuerSerialNumber)
        {
            var authorityKeyIdentifierExtension =
                new AuthorityKeyIdentifier(
                    SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(issuerPublicKey),
                    new GeneralNames(new GeneralName(issuerDN)),
                    issuerSerialNumber);
            certificateGenerator.AddExtension(
                X509Extensions.AuthorityKeyIdentifier.Id, false, authorityKeyIdentifierExtension);
        }

        public static void AddCertToStore(X509Certificate2 cert, StoreName st, StoreLocation sl)
        {
            var store = new X509Store(st, sl);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);

            store.Close();
        }

        private static readonly Dictionary<string, X509Certificate2> _certificateCache =
            new Dictionary<string, X509Certificate2>();

        public static X509Certificate2 GetCertificateForSubject(string subject)
        {
            if (!_certificateCache.TryGetValue(subject, out X509Certificate2? cert))
            {
                cert = LoadExistingPrivateKeyCertificate(subject);

                if (cert == null)
                {
                    // Special case: if we're generating the TCP Mux CA, make a self-signed CA cert
                    if (subject == TcpMuxCASubject)
                    {
                        cert = GenerateCertificate(TcpMuxCASubjectDN, generateCA: true);
                    }
                    else
                    {
                        var tcpMuxCACert = GetCertificateForSubject(TcpMuxCASubject);
                        cert = GenerateCertificate($"CN={subject}", tcpMuxCACert);
                    }
                }

                _certificateCache[subject] = cert;
            }

            return cert;
        }

        private static X509Certificate2? LoadExistingPrivateKeyCertificate(string subject)
        {
            var store =
                subject == TcpMuxCASubject
                ? new X509Store(StoreName.Root, StoreLocation.CurrentUser)
                : new X509Store(StoreLocation.CurrentUser);
            store.Open(OpenFlags.OpenExistingOnly);
            var existingCertificate = store.Certificates.Cast<X509Certificate2>()
                .FirstOrDefault(c => c.HasPrivateKey && c.GetNameInfo(X509NameType.SimpleName, false) == subject);

            if (Verbose)
            {
                if (existingCertificate == null)
                {
                    Console.WriteLine($"No existing certificate for subject {subject} found in the current user's " +
                                      "certificate store; generating a new certificate now");
                }
                else
                {
                    Console.WriteLine($"Successfully loaded certificate for subject {subject} found in the current " +
                                      $"user's certificate store: " +
                                      $"{existingCertificate.Subject}");
                }
            }

            return existingCertificate;
        }
    }
}
