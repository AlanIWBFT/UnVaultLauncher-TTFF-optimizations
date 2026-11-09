using System.Runtime.InteropServices;

namespace UnVault.App.Services;

/// <summary>Keeps software rendering's ordinary timeout waits close to the intended frame interval.</summary>
internal sealed class TimerResolution : IDisposable
{
    private const uint Period = 1;
    private bool _active = true;

    private TimerResolution() { }

    /// <summary>Best effort: a failed request must not prevent the launcher from starting.</summary>
    public static TimerResolution? RequestOneMillisecond()
    {
        if (!OperatingSystem.IsWindows() || timeBeginPeriod(Period) != 0)
            return null;
        return new TimerResolution();
    }

    public void Dispose()
    {
        if (_active)
        {
            _active = false;
            timeEndPeriod(Period);
        }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
}
