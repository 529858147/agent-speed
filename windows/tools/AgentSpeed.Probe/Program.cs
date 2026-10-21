using AgentSpeed.Probe;

var options = ProbeOptions.Parse(args, out var error);
if (options is null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(ProbeOptions.Usage);
    return 2;
}

using var output = new ProbeOutput();
return options.Mode == ProbeMode.DumpToday ? DumpToday.Run(options, output) : Watch.Run(options, output);
