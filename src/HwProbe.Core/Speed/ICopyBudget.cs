namespace Jellyfin.Plugin.HwProbe.Core.Speed;

/// <summary>What the server's memory and CPU leave room for while concurrent streams are counted.</summary>
public interface ICopyBudget
{
    /// <summary>Gets a value indicating whether the last run of copies was stopped because memory ran low.</summary>
    bool LastStopped { get; }

    /// <summary>Returns the most copies the memory free now and the CPU leave room for.</summary>
    /// <param name="speed">One copy's speed as a multiple of real time, which sets the CPU a copy needs at real time.</param>
    /// <returns>The limit, or null when neither one copy's needs nor what's free is known.</returns>
    CopyLimit? MostCopies(double speed);
}
