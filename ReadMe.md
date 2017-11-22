# HashComparer 1.0

HashComparer is a small application that checks the integrity of files by comparing their pre-calculated checksums in a file.

## How Does It Work?

When it's first run, it creates a database of paths of files and their checksums.

Checksums are calculated by MD5 algorithm.

The idea is to find modified files by comparing the checksums of files to the ones in the created database file. 

The added, deleted, and modified files are logged in a log file and/or sent as e-mail.

## Configuration

The configuration is in App.config and NLog.config.

When project is compiled, App.config is put under bin and named as HashComparer.exe.config.

The configuration keys and their values are described below:

**TargetDirectories:** The program scans these directories. The directories are seperated by pipe (|) character. If no target directory is specified, the directory where application resides is scanned.

**IndexFileLocation:** The path of the text file that includes the paths of files, their checksums, and time of entry.  If nothing is specified, the default value is "_hc_index.dat".

**SearchLevel:** The default value is 1. To scan the subdirectories under the target directories, the level should be greater than 1.

For instance to scan the "d" directory under "a" (a->b->c->d), the search level should be 4.

**SearchPattern:** This is the file filter that specifies which files will be included. The default is * which means all files. To include for example just pdf and xlsx files, *.pdf;*.xlsx should be the value.

## Notes for Developers

It's a .Net application. The development IDE is Visual Studio Community 2017. The target framework is 4.6.1.

