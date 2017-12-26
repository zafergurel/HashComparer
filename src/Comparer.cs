using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Diagnostics;
using NLog;
using System.IO.Compression;

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
            public DateTime? ModifiedTimestampUtc { get; set; }
            public string FilePath { get; set; }
            public string Checksum { get; set; }
            public string OldChecksum { get; set; }
            public ComparisonResult LastComparisonResult { get; set; }
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

                backupIndexFile();

                foreach (var targetDirectory in existingTargetDirectories)
                {
                    compareOrCreateChecksums(targetDirectory, 1);
                }

                NewFiles.AddRange(_fileDictionary.Where(f => f.Value.LastComparisonResult == ComparisonResult.Added).Select(f => f.Key));
                ModifiedFiles.AddRange(_fileDictionary.Where(f => f.Value.LastComparisonResult == ComparisonResult.Modified).Select(f => f.Key));
                UncheckedFiles.AddRange(_fileDictionary.Where(f => f.Value.LastComparisonResult == ComparisonResult.Error).Select(f => f.Key));
                DeletedFiles.AddRange(_fileDictionary.Where(f => f.Value.LastComparisonResult == ComparisonResult.None && !File.Exists(f.Key)).Select(f => f.Key));

                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "An exception occurred.");
                return false;
            }
        }

        private void backupIndexFile()
        {
            if (File.Exists(_config.IndexFileLocation))
            {
                string bakFileName = Path.GetFileName(_config.IndexFileLocation) + "." + DateTime.Now.ToString("yyyyMMddHHmmss") + ".bak";
                string folderToZip = Path.Combine(_config.BackupFolder, DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString());
                string bakFilePath = Path.Combine(folderToZip, bakFileName);

                if (!Directory.Exists(_config.BackupFolder))
                {
                    _logger.Info("Creating root backup folder for database files: " + _config.BackupFolder);
                    Directory.CreateDirectory(_config.BackupFolder);
                }

                _logger.Trace("Creating backup folder to be zipped: " + folderToZip);
                Directory.CreateDirectory(folderToZip);

                _logger.Trace("Copying database file to " + bakFilePath);
                File.Copy(_config.IndexFileLocation, bakFilePath);

                _logger.Trace("Zipping folder " + folderToZip);
                ZipFile.CreateFromDirectory(folderToZip, folderToZip + ".zip");

                _logger.Trace("Deleting folder " + folderToZip);
                Directory.Delete(folderToZip, true);

                if (File.Exists(folderToZip + ".zip"))
                {
                    _logger.Trace("Deleting index file after successful backup: " + _config.IndexFileLocation);
                    File.Delete(_config.IndexFileLocation);
                }
                else
                {
                    throw (new Exception("Database file could not be backed-up. File: " + folderToZip + ".zip"));
                }
            }
        }

        void loadIndexIntoMemory()
        {
            if (!File.Exists(_config.IndexFileLocation)) return;
            long lineCount = 0;
            foreach (var line in File.ReadLines(_config.IndexFileLocation))
            {
                ++lineCount;
                try
                {
                    if (line.IndexOf(Config.INDEX_ENTRY_SEPERATOR) > -1)
                    {
                        var arr = line.Split(Config.INDEX_ENTRY_SEPERATOR);
                        _fileDictionary.Add(arr[0], new Entry
                        {
                            FilePath = arr[0],
                            LastComparisonResult = (ComparisonResult)Enum.Parse(typeof(ComparisonResult), arr[1]),
                            Checksum = arr[2],
                            OldChecksum = arr[3],
                            TimestampUtc = parseTime(arr[4]),
                            ModifiedTimestampUtc = !String.IsNullOrEmpty(arr[5]) ? parseTime(arr[5]) : (DateTime?)null
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, $"Could not parse and add the entity to index. Line number: {lineCount} Line: " + line);
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
                    foreach (var file in files)
                    {
                        if (_config.IndexFileLocation.Equals(file, StringComparison.CurrentCultureIgnoreCase))
                        {
                            continue;
                        }

                        string fileChecksum = String.Empty;
                        var now = DateTime.UtcNow;
                        Entry entry = default(Entry);
                        if (isInIndex(file, out entry))
                        {

                            entry.LastComparisonResult = compareChecksum(file, out fileChecksum);
                            _logger.Trace($"Checked {file} -> " + entry.LastComparisonResult.ToString());
                            string entryModification = String.Empty;
                            if (entry.LastComparisonResult == ComparisonResult.Modified || entry.LastComparisonResult == ComparisonResult.Error)
                            {
                                entry.OldChecksum = entry.Checksum;
                                entry.Checksum = fileChecksum;
                                entry.ModifiedTimestampUtc = DateTime.UtcNow;
                                entryModification = String.Format("{1}{0}{2}{0}{3}{0}{4}{0}{5}", Config.INDEX_ENTRY_SEPERATOR, file, entry.LastComparisonResult, entry.Checksum, entry.OldChecksum, DateTime.Now.ToString("yyyyMMddHHmmss"));
                            }
                            else if (entry.LastComparisonResult == ComparisonResult.Deleted)
                            {
                                entryModification = String.Format("{1}{0}{2}{0}{3}{0}{4}{0}{5}", Config.INDEX_ENTRY_SEPERATOR, file, entry.LastComparisonResult, entry.Checksum, entry.OldChecksum, DateTime.Now.ToString("yyyyMMddHHmmss"));
                                // we don't want the entry to be added to the index again...
                                entry = null;
                            }
                            _logger.Trace(entryModification);
                        }
                        else
                        {
                            _logger.Trace($"New file found: {file}");

                            fileChecksum = checksum(file);
                            entry = new Entry
                            {
                                Checksum = fileChecksum,
                                OldChecksum = String.Empty,
                                LastComparisonResult = ComparisonResult.Added,
                                FilePath = file,
                                TimestampUtc = now,
                                ModifiedTimestampUtc = null
                            };
                            _fileDictionary.Add(file, entry);
                        }

                        if (entry != null)
                        {
                            string line = String.Format("{1}{0}{2}{0}{3}{0}{4}{0}{5:yyyyMMddHHmmss}{0}{6:yyyyMMddHHmmss}", Config.INDEX_ENTRY_SEPERATOR, file, entry.LastComparisonResult, entry.Checksum, entry.OldChecksum, entry.TimestampUtc, entry.ModifiedTimestampUtc);
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
            entry = default(Entry);
            bool exists = _fileDictionary.ContainsKey(f);
            if (exists) entry = _fileDictionary[f];
            return exists;
        }

        ComparisonResult compareChecksum(string path, out string fileChecksum)
        {
            ComparisonResult status = ComparisonResult.Unmodified;
            fileChecksum = String.Empty;
            try
            {
                fileChecksum = checksum(path);

                if (fileChecksum.Equals(_fileDictionary[path].Checksum, StringComparison.InvariantCultureIgnoreCase))
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

        DateTime parseTime(string s)
        {
            if (String.IsNullOrEmpty(s))
                return DateTime.MinValue;

            return DateTime.ParseExact(s, "yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
