using CommandLine;
using CommandLine.Text;

namespace FAForever.Replay.Sandbox
{
    internal class ProgramArguments
    {
        [Option('i', "interactive", Required = false, HelpText = "Interactive mode that allows you to provide URLs and/or files to process.")]
        public bool Interactive { get; set; } = false;

        [Option('f', "file", Required = false, HelpText = "Process a specific file.")]
        public string FilePath { get; set; } = string.Empty;

        [Option('u', "url", Required = false, HelpText = "Process a specific file from the internet.")]
        public string URL { get; set; } = string.Empty;

        [Option('j', "output-json", Required = false, HelpText = "Store the output as JSON." )]
        public bool OutputJSON { get; set; } = false;

        [Option('x', "output-xml", Required = false, HelpText = "Store the output as XML.")]
        public string OutputXML { get; set; } = string.Empty;

        [Option('c', "output-csv", Required = false, HelpText = "Store the output as CSV. Note that only the user input is part of this output, the header and other relevant metadata is skipped.")]
        public string OutputCSV { get; set; } = string.Empty;

        [Option('o', "output-directory", Required = true, HelpText = "Directory to store the converted files. The file names are")]
        public string OutputDirectory { get; set; } = string.Empty;
    }
}
