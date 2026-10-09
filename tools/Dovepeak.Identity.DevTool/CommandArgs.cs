namespace Dovepeak.Identity.DevTool;

/// <summary>Minimal "--name value" and "--flag" argument parsing.</summary>
internal sealed class CommandArgs(string[] args)
{
    public string? Get(string name)
    {
        var index = Array.IndexOf(args, $"--{name}");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    public string Require(string name) =>
        Get(name) ?? throw new ArgumentException($"Missing required option --{name}.");

    public bool Has(string name) => args.Contains($"--{name}");
}
