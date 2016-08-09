﻿// #define STRESS_TEST

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ITCC.HTTP.Client;
using ITCC.HTTP.Enums;
using ITCC.HTTP.Server;
using ITCC.Logging.Core;
using ITCC.Logging.Windows.Loggers;
using Newtonsoft.Json;

namespace ITCC.HTTP.Testing
{
    internal class TokenStore
    {
        public string Token { get; set; }
    }

    internal class Program
    {
        private static void Main(string[] args)
        {
            MainAsync().GetAwaiter().GetResult();
        }

        private static async Task MainAsync()
        {
            Thread.CurrentThread.Name = "MAIN";
            if (!InitializeLoggers())
                return;

            Logger.LogEntry("MAIN", LogLevel.Info, "Started");

            StartServer();

            StaticClient.ServerAddress = "https://localhost:8888";
            StaticClient.AllowUntrustedServerCertificates();
            StaticClient.LogBodyReplacePatterns.Add(new Tuple<string, string>("(\"Token\":\")([\\w\\d]+)(\")", $"$1REMOVED_FROM_LOG$3"));
            StaticClient.LogProhibitedHeaders.Add("Authorization");
            StaticClient.AllowGzipEncoding = true;
#if !STRESS_TEST
            var result = await StaticClient.GetRawAsync("token",
                new Dictionary<string, string>
                {
                    {"login", "user"},
                    {"password", "pwd"}
                },
                new Dictionary<string, string>
                {
                    {"Authorization", "lkasjdlkaskjdlkajdlkasjdlkasjdlkajsdlkjaskldjaslkdjaslkkd"},
                    {"Accept-Encoding", "gzip"}
                });
#else
            const int requestsPerStep = 10000;
            const int stepCount = 10;
            const int requestCount = requestsPerStep * stepCount;
            double totalElapsed = 0;

            var totalFailed = 0;
            Logger.LogEntry("MAIN", LogLevel.Info, $"Using {requestsPerStep} requests per step");
            for (var step = 0; step < stepCount; ++step)
            {
                var stopWatch = Stopwatch.StartNew();
                var tasks = new Task<RequestResult<string>>[requestCount /stepCount];
                for (var i = 0; i < requestsPerStep; ++i)
                {
                    tasks[i] = StaticClient.GetRawAsync("token", new Dictionary<string, string>
                    {
                        {"login", "user"},
                        {"password", "pwd"}
                    },
                        new Dictionary<string, string>
                        {
                        {"Authorization", "lkasjdlkaskjdlkajdlkasjdlkasjdlkajsdlkjaskldjaslkdjaslkkd"},
                        {"Accept-Encoding", "gzip"}
                        });
                }
                var results = await Task.WhenAll(tasks);
                var stepFailed = results.Count(r => r.Status != ServerResponseStatus.Ok);
                totalFailed += stepFailed;
                stopWatch.Stop();
                var stepElapsed = (double)stopWatch.ElapsedMilliseconds;
                totalElapsed += stepElapsed;
                var level = stepFailed > 0 ? LogLevel.Warning : LogLevel.Info;
                Logger.LogEntry("MAIN", level, $"Step {step}/{stepCount} done in {stepElapsed} ms ({stepElapsed / requestsPerStep} avg). ({stepFailed}/{requestsPerStep} failed)");
            }
            
            Logger.LogEntry("MAIN", LogLevel.Info, $"Done {requestCount} requests in {totalElapsed} ms ({totalElapsed / requestCount} avg). Failed: {totalFailed}");

#endif
            Console.ReadLine();
            StopServer();
            Logger.LogEntry("MAIN", LogLevel.Trace, "Finished");
        }

        private static bool InitializeLoggers()
        {
            Logger.Level = LogLevel.Trace;
            Logger.RegisterReceiver(new ColouredConsoleLogger(), true);

            return true;
        }

        private static void StartServer()
        {
            StaticServer<object>.Start(new HttpServerConfiguration<object>
            {
                BodyEncoder = new BodyEncoder
                {
                    AutoGzipCompression = false,
                    ContentType = "application/json",
                    Encoding = Encoding.UTF8,
                    Serializer = JsonConvert.SerializeObject
                },
                Port = 8888,
                Protocol = Protocol.Https,
                LogBodyReplacePatterns = new List<Tuple<string, string>>
                {
                    new Tuple<string, string>("(\"Token\":\")([\\w\\d]+)(\")", "$1REMOVED_FROM_LOG$3")
                },
                LogProhibitedHeaders = new List<string> { "Authorization" },
                ServerName = "ITCC Test",
                StatisticsEnabled = true,
                SubjectName = "localhost",
                FilesEnabled = true,
                FilesNeedAuthorization = false,
                FilesBaseUri = "files",
                FileSections = new List<FileSection>
                {
                    new FileSection
                    {
                        Folder = "Pictures",
                        MaxFileSize = -1,
                        Name = "Pictures"
                    }
                },
                FilesLocation = @"C:\Users\vladimir.tyrin",
                FilesPreprocessingEnabled = false,
                FilesPreprocessorThreads = -1
            });

            StaticServer<object>.AddRequestProcessor(new RequestProcessor<object>
            {
                AuthorizationRequired = false,
                Handler = async (o, request) =>
                {
                    int delay;
                    var delayString = request.QueryString["value"];
                    if (delayString != null)
                    {
                        try
                        {
                            delay = Convert.ToInt32(delayString);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogException("HANDLER", LogLevel.Debug, ex);
                            return new HandlerResult(HttpStatusCode.BadRequest, "Bad delay value!");
                        }
                    }
                    else
                    {
                        delay = 5;
                    }
                    Logger.LogEntry("HANDLER", LogLevel.Trace, $"Will sleep for {delay} seconds, then respond to {request.RemoteEndPoint}");
                    await Task.Delay(delay * 1000);
                    return new HandlerResult(HttpStatusCode.OK, "Hello ^_^");
                },
                Method = HttpMethod.Get,
                SubUri = "delay"
            });

            StaticServer<object>.AddRequestProcessor(new RequestProcessor<object>
            {
                AuthorizationRequired = false,
                Method = HttpMethod.Get,
                SubUri = "bigdata",
                Handler = (account, request) =>
                {
                    var builder = new StringBuilder(64 * 1024 * 1024);
                    for (var i = 0; i < 1024; ++i)
                    {
                        var str = string.Empty;
                        for (var j = 0; j < 1024; ++j)
                            str += "12345678901234567890123456789012";
                        builder.Append(str);
                    }
                    return Task.FromResult(new HandlerResult(HttpStatusCode.OK, builder.ToString()));
                }
            });

            StaticServer<object>.AddRequestProcessor(new RequestProcessor<object>
            {
                AuthorizationRequired = false,
                Method = HttpMethod.Get,
                SubUri = "token",
                Handler = (account, request) => Task.FromResult(new HandlerResult(HttpStatusCode.OK, new TokenStore { Token = "Hello111" }))
            });

            StaticServer<object>.AddStaticRedirect("test", "delay");
        }

        private static void StopServer()
        {
            StaticServer<object>.Stop();
        }
    }
}
