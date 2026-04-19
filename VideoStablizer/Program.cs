using Microsoft.Extensions.Configuration;
using VideoStabilization.Models;

namespace VideoStabilization
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .Build();

            var options = config.GetSection(VideoStabilizerOptions.SectionName)
                                .Get<VideoStabilizerOptions>() ?? new VideoStabilizerOptions();

            if (string.IsNullOrWhiteSpace(options.InputPath) || string.IsNullOrWhiteSpace(options.OutputPath))
            {
                Console.Error.WriteLine("Error: InputPath and OutputPath must be set in appsettings.json.");
                Environment.Exit(1);
            }

            var stabilizer = new VideoStabilizer(options);

            try
            {
                stabilizer.Stabilize(options.InputPath, options.OutputPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }
    }
}