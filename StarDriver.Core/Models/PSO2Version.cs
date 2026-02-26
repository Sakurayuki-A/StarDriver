namespace StarDriver.Core.Models;

/// <summary>
/// PSO2 版本号
/// </summary>
public readonly struct PSO2Version : IEquatable<PSO2Version>, IComparable<PSO2Version>
{
    public string VersionString { get; }

    public PSO2Version(string versionString)
    {
        VersionString = versionString?.Trim() ?? throw new ArgumentNullException(nameof(versionString));
    }

    public static bool TryParse(string? input, out PSO2Version version)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            version = default;
            return false;
        }

        version = new PSO2Version(input);
        return true;
    }

    public bool Equals(PSO2Version other) => 
        string.Equals(VersionString, other.VersionString, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => 
        obj is PSO2Version other && Equals(other);

    public override int GetHashCode() => 
        StringComparer.OrdinalIgnoreCase.GetHashCode(VersionString ?? string.Empty);

    public int CompareTo(PSO2Version other) => 
        string.Compare(VersionString, other.VersionString, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => VersionString ?? "Unknown";

    public static bool operator ==(PSO2Version left, PSO2Version right) => left.Equals(right);
    public static bool operator !=(PSO2Version left, PSO2Version right) => !left.Equals(right);
}
