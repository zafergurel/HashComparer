
namespace HashComparer
{

    public class Config
    {
        public const string DEFAULT_INDEX_FILE_NAME = "_hc_index.dat";
        public const char INDEX_ENTRY_SEPERATOR = '|';
        
        public string[] TargetDirectories { get; set; }
        public string IndexFileLocation { get; set; }
        public string BackupFolder { get; set; }
        public string SearchPattern { get; set; }
        public int SearchLevel { get; set; }
    }
}
