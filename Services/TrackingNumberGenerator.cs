namespace appdevPUPSCOUT.Services;

public class TrackingNumberGenerator
{
    public string Generate() => Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
}