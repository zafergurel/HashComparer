using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HashComparer
{
    class Program
    {
        public static void Main(string[] args)
        {

            var logger = NLog.LogManager.GetCurrentClassLogger();

            var configLoader = new ConfigLoader(logger);
            Config appConfig = null;
            try
            {
                appConfig = configLoader.LoadConfiguration();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "An exception occurred while loading configuration file.");
                return;
            }

            try
            {
                var startTime = DateTime.Now;
                logger.Info("Scanning is started." + Environment.NewLine +
                    $"Start Time: {startTime}" + Environment.NewLine +
                    $"Target Directories: {String.Join(", ", appConfig.TargetDirectories)}" + Environment.NewLine +
                    $"Index File: {appConfig.IndexFileLocation}" + Environment.NewLine +
                    $"Search Pattern: {appConfig.SearchPattern}" + Environment.NewLine +
                    $"Search Level:{appConfig.SearchLevel}");

                var comparer = new Comparer(appConfig, logger);
                comparer.Run();

                var endTime = DateTime.Now;

                var results = new StringBuilder();

                results.Append("Scanning was completed. " + Environment.NewLine +
                    $"Finish Time: {endTime}" + Environment.NewLine +
                    $"Duration: {Math.Round((endTime - startTime).TotalSeconds, 1)} seconds" + Environment.NewLine +
                    $"Total File Count: {comparer.FileCount}" + Environment.NewLine +
                    $"Modified: {comparer.ModifiedFiles.Count}" + Environment.NewLine +
                    $"Added: {comparer.NewFiles.Count}" + Environment.NewLine +
                    $"Unchecked:{comparer.UncheckedFiles.Count}" + Environment.NewLine +
                    $"Deleted:{comparer.DeletedFiles.Count}" + Environment.NewLine + Environment.NewLine);

                if (comparer.ModifiedFiles.Count > 0)
                {
                    var changedFiles = String.Join(Environment.NewLine, comparer.ModifiedFiles);
                    results.AppendLine($"Modified files:{Environment.NewLine}{changedFiles}{Environment.NewLine}");
                }

                if (comparer.DeletedFiles.Count > 0)
                {
                    var deletedFiles = String.Join(Environment.NewLine, comparer.DeletedFiles);
                    results.AppendLine($"Deleted files:{Environment.NewLine}{deletedFiles}{Environment.NewLine}");
                }

                if (comparer.NewFiles.Count > 0)
                {
                    var newFiles = String.Join(Environment.NewLine, comparer.NewFiles);
                    results.AppendLine($"New files:{Environment.NewLine}{newFiles}{Environment.NewLine}");
                }

                logger.Info(results.ToString());

            }
            catch (Exception ex)
            {
                logger.Error(ex, "An exception occurred.");
                return;
            }
        }
    }
}
