using AgentSpeed.Probe;

var options = ProbeOptions.Parse(args, out var error);
if (options is null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine(ProbeOptions.Usage);
    return 2;
}

using var output = new ProbeOutput();
return options.Mode switch
{
    ProbeMode.DumpToday => DumpToday.Run(options, output),
    ProbeMode.DumpHistory => DumpHistory.Run(options, output),
    ProbeMode.SyncPrices => NetworkSync.Prices(options, output),
    ProbeMode.SyncRates => NetworkSync.Rates(options, output),
    _ => Watch.Run(options, output),
};
