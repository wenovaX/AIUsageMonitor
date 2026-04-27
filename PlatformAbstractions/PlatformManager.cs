namespace AIUsageMonitor.PlatformAbstractions;

public class PlatformManager
{
    public PlatformManager(IPlatformController controller)
    {
        Current = controller;
    }

    public IPlatformController Current { get; }
}
