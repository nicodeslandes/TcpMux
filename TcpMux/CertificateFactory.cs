using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using Org.BouncyCastle.X509;

namespace TcpMux
{
    class CertificateFactory
    {
        public const string TcpMuxCASubject = "DO_NOT_TRUST__TCPMUX_CA";
        public static readonly string TcpMuxCASubjectDN = $"CN={TcpMuxCASubject}";
        public static bool Verbose { get; set; }

        public static X509Certificate2 GenerateSelfSignedCertificate(string subjectName, string issuerName, AsymmetricKeyParameter issuerPrivKey, int keyStrength = 2048)
        {
            if (Verbose)
                Console.WriteLine($"Generating certificate for {subjectName}");

            // Generating Random Numbers
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            var certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            //// Signature Algorithm
            const string signatureAlgorithm = "SHA256WithRSA";
            //certificateGenerator.SetSignatureAlgorithm(signatureAlgorithm);

            // Issuer and Subject Name
            var subjectDN = new X509Name(subjectName);
            var issuerDN = new X509Name(issuerName);
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For
            var notBefore = DateTime.UtcNow.Date;
            var notAfter = notBefore.AddYears(2);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // Generating the Certificate

            // selfsign certificate
            var signatureFactory = new Asn1SignatureFactory(signatureAlgorithm, subjectKeyPair.Private, random);
            var certificate = certificateGenerator.Generate(signatureFactory);

            // correcponding private key
            PrivateKeyInfo info = PrivateKeyInfoFactory.CreatePrivateKeyInfo(subjectKeyPair.Private);


            // merge into X509Certificate2
            var x509 = new X509Certificate2(certificate.GetEncoded());

            var seq = (Asn1Sequence)Asn1Object.FromByteArray(info.ParsePrivateKey().GetDerEncoded());
            if (seq.Count != 9)
                throw new PemException("malformed sequence in RSA private key");

            var rsa = RsaPrivateKeyStructure.GetInstance(seq);
            RsaPrivateCrtKeyParameters rsaparams = new RsaPrivateCrtKeyParameters(
                rsa.Modulus, rsa.PublicExponent, rsa.PrivateExponent, rsa.Prime1, rsa.Prime2, rsa.Exponent1, rsa.Exponent2, rsa.Coefficient);

            x509.PrivateKey = DotNetUtilities.ToRSA(rsaparams);
            return x509;

        }


        public static X509Certificate2 GenerateCACertificate(string subjectName, int keyStrength = 2048)
        {
            if (Verbose)
                Console.WriteLine($"Generating certificate for {subjectName}");

            // Generating Random Numbers
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            // The Certificate Generator
            var certificateGenerator = new X509V3CertificateGenerator();

            // Serial Number
            var serialNumber = BigIntegers.CreateRandomInRange(BigInteger.One, BigInteger.ValueOf(Int64.MaxValue), random);
            certificateGenerator.SetSerialNumber(serialNumber);

            // Signature Algorithm
            const string signatureAlgorithm = "SHA256WithRSA";

            // Issuer and Subject Name
            var subjectDN = new X509Name(subjectName);
            var issuerDN = subjectDN;
            certificateGenerator.SetIssuerDN(issuerDN);
            certificateGenerator.SetSubjectDN(subjectDN);

            // Valid For
            var notBefore = DateTime.UtcNow.Date;
            var notAfter = notBefore.AddYears(2);

            certificateGenerator.SetNotBefore(notBefore);
            certificateGenerator.SetNotAfter(notAfter);

            // Subject Public Key
            var keyGenerationParameters = new KeyGenerationParameters(random, keyStrength);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            var subjectKeyPair = keyPairGenerator.GenerateKeyPair();

            certificateGenerator.SetPublicKey(subjectKeyPair.Public);

            // Generating the Certificate

            // selfsign certificate
            var signatureFactory = new Asn1SignatureFactory(signatureAlgorithm, subjectKeyPair.Private, random);
            var certificate = certificateGenerator.Generate(signatureFactory);
            var rsaKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)subjectKeyPair.Private);

            return new X509Certificate2(certificate.GetEncoded())
            {
                PrivateKey = rsaKey
            };
        }

        public static void AddCertToStore(X509Certificate2 cert, StoreName st, StoreLocation sl)
        {
            X509Store store = new X509Store(st, sl);
            store.Open(OpenFlags.ReadWrite);
            store.Add(cert);

            store.Close();
        }

        private static readonly Dictionary<string, X509Certificate2> _certificateCache =
            new Dictionary<string, X509Certificate2>();

        public static X509Certificate2 GetCertificateForSubject(string subject)
        {
            if (!_certificateCache.TryGetValue(subject, out var cert))
            {
                cert = LoadExistingPrivateKeyCertificate(subject);

                if (cert == null)
                {
                    // Special case: if we're generating the TCP Mux CA, make a self-signed cert
                    if (subject == TcpMuxCASubject)
                        cert = GenerateCACertificate(TcpMuxCASubjectDN);
                    else
                    {
                        var tcpMuxCACert = GetCertificateForSubject(TcpMuxCASubject);
                        var tcpMuxCAPrivateKey = TransformRSAPrivateKey(tcpMuxCACert.PrivateKey);

                        // TODO: use the CA as the issuer
                        cert = GenerateSelfSignedCertificate($"CN={subject}", $"CN={subject}", tcpMuxCAPrivateKey);
                    }
                }

                _certificateCache[subject] = cert;
            }

            return cert;
        }

        public static AsymmetricKeyParameter TransformRSAPrivateKey(AsymmetricAlgorithm privateKey)
        {
            RSACryptoServiceProvider prov = privateKey as RSACryptoServiceProvider;
            RSAParameters parameters = prov.ExportParameters(true);

            return new RsaPrivateCrtKeyParameters(
                new BigInteger(1, parameters.Modulus),
                new BigInteger(1, parameters.Exponent),
                new BigInteger(1, parameters.D),
                new BigInteger(1, parameters.P),
                new BigInteger(1, parameters.Q),
                new BigInteger(1, parameters.DP),
                new BigInteger(1, parameters.DQ),
                new BigInteger(1, parameters.InverseQ));
        }

        private static X509Certificate2 LoadExistingPrivateKeyCertificate(string subject)
        {
            var store =
                subject == TcpMuxCASubject
                ? new X509Store(StoreName.CertificateAuthority, StoreLocation.CurrentUser)
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
