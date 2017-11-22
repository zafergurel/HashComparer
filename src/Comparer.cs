using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;
using NLog;

namespace HashComparer
{
    public class Comparer
    {
        public enum ComparisonResult : int
        {
            None,
            Unmodified,
            Added,
            Modified,
            Deleted,
            Error
        }

        public class Entry
        {
            public DateTime TimestampUtc { get; set; }
            public string FilePath { get; set; }
            public string Checksum { get; set; }
            public ComparisonResult ComparisonResult { get; set; }
        }

        private Dictionary<string, Entry> _fileDictionary;

        public List<string> ModifiedFiles { get; private set; }
        public List<string> NewFiles { get; private set; }
        public List<string> DeletedFiles { get; private set; }
        public List<string> UncheckedFiles { get; private set; }
        public int FileCount { get => _fileDictionary.Count; }


        Config _config;
        ILogger _logger;
        public Comparer(Config appConfig, ILogger logger)
        {
            _fileDictionary = new Dictionary<string, Entry>();
            _config = appConfig;
            _logger = logger;
        }

        public bool Run()
        {
            ModifiedFiles = new List<string>();
            NewFiles = new List<string>();
            DeletedFiles = new List<string>();
            UncheckedFiles = new List<string>();

            try
            {
                var missingDirs = _config.TargetDirectories.Where(dir => !Directory.Exists(dir)).ToList();
                
                if (missingDirs.Count > 0)
                {
                    _logger.Error($"Target directories ({String.Join("; ", missingDirs)}) could not be found.");
                    if (missingDirs.Count == _config.TargetDirectories.Count())
                    {
                        return false;
                    }
                }

                var existingTargetDirectories = _config.TargetDirectories.Where(t => !missingDirs.Contains(t));

                if (string.IsNullOrEmpty(_config.IndexFileLocation))
                {
                    _config.IndexFileLocation = Path.Combine(existingTargetDirectories.FirstOrDefault(), Config.DEFAULT_INDEX_FILE_NAME);
                }

                if (string.IsNullOrEmpty(_config.SearchPattern))
                {
                    _config.SearchPattern = "*";
                }

                loadIndexIntoMemory();

                foreach (var targetDirectory in existingTargetDirectories)
                {
                    compareOrCreateChecksums(targetDirectory, 1);
                }

                NewFiles.AddRange(_fileDictionary.Where(f => f.Value.ComparisonResult == ComparisonResult.Added).Select(f => f.Key));
                ModifiedFiles.AddRange(_fileDictionary.Where(f => f.Value.ComparisonResult == ComparisonResult.Modified).Select(f => f.Key));
                UncheckedFiles.AddRange(_fileDictionary.Where(f => f.Value.ComparisonResult == ComparisonResult.Error).Select(f => f.Key));
                DeletedFiles.AddRange(_fileDictionary.Where(f => f.Value.ComparisonResult == ComparisonResult.None && !File.Exists(f.Key)).Select(f => f.Key));

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An exception occurred.");
                return false;
            }
        }

        void loadIndexIntoMemory()
        {
            if (!File.Exists(_config.IndexFileLocation)) return;

            foreach (var line in File.ReadLines(_config.IndexFileLocation))
            {
                if (line.IndexOf(Config.INDEX_ENTRY_SEPERATOR) > -1)
                {
                    var arr = line.Split(Config.INDEX_ENTRY_SEPERATOR);
                    _fileDictionary.Add(arr[0], new Entry
                    {
                        FilePath = arr[0],
                        Checksum = arr[1],
                        TimestampUtc = DateTime.ParseExact(arr[2], "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture),
                        ComparisonResult = ComparisonResult.None
                    });
                }
            }

        }
        void compareOrCreateChecksums(string directory, int level)
        {
            using (var fs = new StreamWriter(File.Open(_config.IndexFileLocation, FileMode.Append)))
            {
                var searchPatterns = _config.SearchPattern.Split(';');
                foreach (var pattern in searchPatterns)
                {
                    var files = Directory.GetFiles(directory, pattern);
                    foreach (var f in files)
                    {
                        if (_config.IndexFileLocation.Equals(f, StringComparison.CurrentCultureIgnoreCase))
                        {
                            continue;
                        }

                        if (isInIndex(f, out Entry entry))
                        {
                            entry.ComparisonResult = compareChecksum(f);
                            _logger.Trace($"Checked {f} -> " + entry.ComparisonResult.ToString());
                        }
                        else
                        {
                            _logger.Trace($"Added {f} to index");

                            var cs = checksum(f);
                            var now = DateTime.UtcNow;

                            _fileDictionary.Add(f, new Entry
                            {
                                Checksum = cs,
                                ComparisonResult = ComparisonResult.Added,
                                FilePath = f,
                                TimestampUtc = now
                            });

                            string line = f + Config.INDEX_ENTRY_SEPERATOR + cs + Config.INDEX_ENTRY_SEPERATOR + now.ToString("yyyyMMddHHmmss");
                            fs.WriteLine(line);
                        }
                    }
                }

            }

            if (_config.SearchLevel > level)
            {
                foreach (var dir in Directory.GetDirectories(directory))
                {
                    compareOrCreateChecksums(dir, level++);
                }
            }
        }

        private bool isInIndex(string f, out Entry entry)
        {
            entry = new Entry();
            bool exists = _fileDictionary.ContainsKey(f);
            if (exists) entry = _fileDictionary[f];
            return exists;
        }

        ComparisonResult compareChecksum(string path)
        {
            ComparisonResult status = ComparisonResult.Unmodified;

            try
            {
                var cs = checksum(path);

                if (cs.Equals(_fileDictionary[path].Checksum, StringComparison.InvariantCultureIgnoreCase))
                {
                    status = ComparisonResult.Unmodified;
                }
                else
                {
                    status = ComparisonResult.Modified;
                }
            }
            catch
            {
                status = ComparisonResult.Error;
            }
            return status;
        }

        string checksum(string path)
        {
            var md5 = MD5.Create();
            byte[] hash = null;

            try
            {
                using (var stream = File.OpenRead(path))
                {
                    hash = md5.ComputeHash(stream);
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            if (hash != null && hash.Length > 0)
            {
                return Convert.ToBase64String(hash);
            }
            else
            {
                return String.Empty;
            }
        }
    }
}
