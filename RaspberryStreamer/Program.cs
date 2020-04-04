using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace RaspberryStreamer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var parser = new Parser(x => x.HelpWriter = Console.Out);
            var settings = parser.ParseArguments<StreamerSettings>(args);
            settings.WithParsed(RunApp)
                .WithNotParsed(errors =>
                {
                    foreach (var error in errors)
                    {
                        Console.WriteLine(error);
                    }
                });
        }

        private static void RunApp(StreamerSettings streamerSettings)
        {
            Host.CreateDefaultBuilder()
                .UseSystemd()
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton(streamerSettings);
                    services.AddSingleton<DuetWifiStatusProvider>();
                    services.AddSingleton<WebCameraProvider>();
                    services.AddHostedService<Worker>();
                })
                .Build()
                .Run();
        }
    }
}
