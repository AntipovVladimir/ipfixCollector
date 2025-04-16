using fixclean;
Console.WriteLine("ipfix log duplicate cleaner");

if (Environment.GetCommandLineArgs().Length < 2)
{
    Console.WriteLine("Usage: fixclean input.rawlog");
    return;
}

string inputFileName = Environment.GetCommandLineArgs()[1];
if (string.IsNullOrWhiteSpace(inputFileName))
{
    Console.WriteLine("filename error");
    return;
}

FileAttributes attr = File.GetAttributes(inputFileName);
Cleaner cleaner = new Cleaner();

if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
{
    // is a directory
    DirectoryInfo d = new DirectoryInfo(inputFileName);
    FileInfo[] filelist = d.GetFiles("*.rawlog", SearchOption.AllDirectories);
    Console.WriteLine("Found {0} files", filelist.Length);
    int totalf = filelist.Length;
    int currentf = 0;
    foreach (FileInfo file in filelist)
    {
        currentf++;
        Console.WriteLine("Processing {0} / {1}", currentf, totalf);
        cleaner.Run(file.FullName);
    }
}
else
{
    cleaner.Run(inputFileName);
}

