﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Net.Security;
using System.IO;
using System.Security;
using TeamServer.Models.Extras;
using System.Linq;
using System.Collections.Generic;
using System.Security.Authentication;
using Microsoft.Extensions.Options;

namespace TeamServer.Models
{
    public class Httpmanager : manager
    {
        public override string Name { get; set; } // properties allows us to get Name when manager is created later so set will go with creation functions later
        public int ConnectionPort { get; set; }         // connection location for engineers created with this manager
        public string ConnectionAddress { get; set; }   // connection location for engineers created with this manager
        public string BindAddress { get; set; }         // local teamserver address to bind to
        public int BindPort { get; set; }               // local teamserver port to bind to 
        public bool Active => _tokenSource is not null && !_tokenSource.IsCancellationRequested;     // active is true when manager is running which is whenever the token source is not null and not cancelled
        public bool IsSecure { get; set; }
        public string CertificatePath { get; set; }
        public string CertificatePassword { get; set; } = "p@ssw0rd";
        private X509Certificate2 cert { get; set; }

        public ApiModels.Requests.C2Profile c2Profile { get; set;}
        
        public override ManagerType Type { get; set; } = ManagerType.http;

        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        


        public Httpmanager(string name, string connectionAddress,int connectionPort ,string bindAddress,int bindPort, bool isSecure, ApiModels.Requests.C2Profile profile)    //Constructor for Httpmanager
        {
            Name = name;
            ConnectionPort = connectionPort;
            ConnectionAddress = connectionAddress;
            BindAddress = bindAddress;
            BindPort = bindPort;
            IsSecure = isSecure;
            c2Profile = profile;
        }

        public Httpmanager() { }

        public override async Task Start()
        {
            try
            {
                //check that ConnectionAddress is in the format of an ip address and that ConnectionPort is between 1-65535 and if either one is not cancel the token
                if (IsSecure)
                {
                    Type = ManagerType.https;
                    await GeneerateCert();

                    var hostBuilder = new HostBuilder().ConfigureWebHostDefaults(host =>
                    {
                        host.UseUrls($"https://{BindAddress}:{BindPort}");
                        host.Configure(ConfigureApp);
                        host.ConfigureServices(ConfigureServices);
                        // have the host use the certificate
                        host.ConfigureKestrel(serverOptions =>
                        {
                            serverOptions.Limits.MaxRequestBodySize = int.MaxValue;
                            serverOptions.AddServerHeader = false;
                            serverOptions.ConfigureEndpointDefaults(listenOptions =>
                            {
                                listenOptions.UseHttps(new X509Certificate2(CertificatePath, CertificatePassword));
                                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;

                            });
                            //set connectionOptions to make sure client has a cert
                            serverOptions.ConfigureHttpsDefaults(httpsOptions =>
                            {
                                httpsOptions.SslProtocols = SslProtocols.Tls13 | SslProtocols.Tls12 | SslProtocols.Tls11 | SslProtocols.Tls;
                                httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                                httpsOptions.ClientCertificateValidation = (certificate, chain, sslPolicyErrors) =>
                                {
                                    //ignore remote certificate chain errors
                                    if (sslPolicyErrors == SslPolicyErrors.RemoteCertificateChainErrors)
                                    {
                                        return true;
                                    }
                                    else if (sslPolicyErrors == SslPolicyErrors.None)
                                    {
                                        return true;
                                    }

                                    //print the errors
                                    foreach (var error in sslPolicyErrors.ToString().Split(','))
                                    {
                                        Console.WriteLine(error);
                                    }
                                    return false;
                                };
                            });
                        });
                    });

                    var host = hostBuilder.Build();
                    host.RunAsync(_tokenSource.Token);
                }
                else
                {
                    var hostBuilder = new HostBuilder().ConfigureWebHostDefaults(host =>
                    {
                        host.UseUrls($"http://{BindAddress}:{BindPort}");
                        host.Configure(ConfigureApp);
                        host.ConfigureServices(ConfigureServices);
                        host.ConfigureKestrel(serverOptions =>
                        {
                            serverOptions.Limits.MaxRequestBodySize = int.MaxValue;
                            serverOptions.AddServerHeader = false;
                            serverOptions.ConfigureEndpointDefaults(listenOptions =>
                            {
                                listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2AndHttp3;
                            });
                        });
                    });
                    var host = hostBuilder.Build();
                    await host.RunAsync(_tokenSource.Token);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
            }
            
        }


        private void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSingleton(EngineerService);
        }

        private void ConfigureApp(IApplicationBuilder app)
        {
            
            app.UseRouting();
            List<string> urls = c2Profile.Urls.Split(',').ToList();
            foreach(string url in urls)
            {
                app.UseEndpoints(e =>
                {
                e.MapControllerRoute(url, url, new { controller = "httpmanager", action = "HandleImplant" });
                });
            }
            app.UseStaticFiles();

        }

        public override void Stop()
        {
            _tokenSource.Cancel();
        }

        private async Task GeneerateCert()
        {
            // Generate private-public key pair
            var rsaKey = RSA.Create(2048);

            // Describe certificate
            string subject = "CN=HardHat Manager";

            // Create certificate request
            var certificateRequest = new CertificateRequest(
                subject,
                rsaKey,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1
            );

            certificateRequest.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(
                    certificateAuthority: false,
                    hasPathLengthConstraint: false,
                    pathLengthConstraint: 0,
                    critical: true
                )
            );

            certificateRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    keyUsages:
                        X509KeyUsageFlags.DigitalSignature
                        | X509KeyUsageFlags.KeyEncipherment,
                    critical: false
                )
            );

            certificateRequest.CertificateExtensions.Add(
                new X509SubjectKeyIdentifierExtension(
                    key: certificateRequest.PublicKey,
                    critical: false
                )
            );

            var expireAt = DateTimeOffset.Now.AddYears(5);

            cert = certificateRequest.CreateSelfSigned(DateTimeOffset.Now, expireAt);

            // Export certificate with private key
            var exportableCertificate = new X509Certificate2(cert.Export(X509ContentType.Cert), (string)null, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet).CopyWithPrivateKey(rsaKey);


            //check if the operating system is windows or linux and only set friendlyName on windows
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                exportableCertificate.FriendlyName = "Certificate For Client Authorization";
            }

            // Create password for certificate protection
            var passwordForCertificateProtection = new SecureString();
            foreach (var @char in "p@ssw0rd")
            {
                passwordForCertificateProtection.AppendChar(@char);
            }

            // Export certificate to a file.
            var DirectorySeperatorChar = Path.DirectorySeparatorChar;
            string CertificateDir = $"{Environment.CurrentDirectory}{DirectorySeperatorChar}Certificates{DirectorySeperatorChar}";
            
            File.WriteAllBytes($"{CertificateDir}certClient{Name}.pfx",exportableCertificate.Export(X509ContentType.Pfx,passwordForCertificateProtection));
            CertificatePath = CertificateDir + $"certClient{Name}.pfx";
        }

    }
}
