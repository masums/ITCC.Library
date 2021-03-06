﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ITCC.HTTP.Client.Core;
using ITCC.HTTP.Client.Utils;
using ITCC.Logging.Core;
using ITCC.Logging.Windows.Loggers;
using Newtonsoft.Json;

namespace ITCC.HTTP.Testing
{
    public class TestClass
    {
        public string First { get; set; }
        public string Second { get; set; }
    }

    internal static class Program
    {
        private static void Main(string[] args) => MainAsync().GetAwaiter().GetResult();

        private static async Task MainAsync()
        {
            Thread.CurrentThread.Name = "MAIN";
            if (!InitializeLoggers())
                return;
            Logger.LogEntry("MAIN", LogLevel.Info, "Started");

            StaticClient.ServerAddress = "https://www.google.ru/";
            StaticClient.AllowedRedirectCount = 2;
            var result = await StaticClient.GetRawAsync(string.Empty);
            Logger.LogEntry("TEST", LogLevel.Info, $"Status: {result.Status}");
            Logger.LogEntry("TEST", LogLevel.Info, $"Headers:\n{string.Join("\n", result.Headers.Select(h => $"{h.Key}: {h.Value}"))}");

            await Task.Yield();
            Logger.LogEntry("MAIN", LogLevel.Info, "Finished");
            Console.ReadLine();
        }

        private static bool InitializeLoggers()
        {
            Logger.Level = LogLevel.Trace;
            Logger.RegisterReceiver(new ColouredConsoleLogger(), true);

            return true;
        }
    }
}
