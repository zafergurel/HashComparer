using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Configuration;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NLog;

namespace HashComparer
{
    public class ConfigLoader
    {
        ILogger _logger;
        public ConfigLoader(ILogger logger)
        {
            _logger = logger;
        }
        public Config LoadConfiguration()
        {
            Config config = new Config();
            if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["TargetDirectories"]))
            {
                config.TargetDirectories = ConfigurationManager.AppSettings["TargetDirectories"].Split('|');
            }
            else
            {
                config.TargetDirectories = new string[] { AppDomain.CurrentDomain.BaseDirectory };
            }

            config.SearchPattern = ConfigurationManager.AppSettings["SearchPattern"] ?? "*";

            var missingDirs = config.TargetDirectories.Where(t => !Directory.Exists(t));

            if (missingDirs.Count() > 0)
            {
                throw new ConfigurationErrorsException($"Target directories ({String.Join("; ", missingDirs)}) could not be found. Please check the configuration file.");
            }

            config.IndexFileLocation = ConfigurationManager.AppSettings["IndexFileLocation"] ?? Path.Combine(config.TargetDirectories.FirstOrDefault(), Config.DEFAULT_INDEX_FILE_NAME);

            int.TryParse(ConfigurationManager.AppSettings["SearchLevel"], out int searchLevel);
            config.SearchLevel = searchLevel == 0 ? 1 : searchLevel;
            return config;
        }
    }
}
