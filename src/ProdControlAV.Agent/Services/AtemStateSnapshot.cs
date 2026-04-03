using ProdControlAV.Agent.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// Thread-safe cache of the most recently received state from an ATEM switcher.
/// Updated by calling <see cref="Apply"/> with parsed command-block names and data
/// extracted from inbound ATEM UDP packets.
/// </summary>
public sealed class AtemStateSnapshot
{
    private readonly object _lock = new();

    // Per-ME (Mix/Effect) program and preview inputs. Key = ME index (0-based).
    private readonly Dictionary<int, int> _programInputs = new();
    private readonly Dictionary<int, int> _previewInputs = new();

    // Completed the first time any program state block is successfully applied.
    private readonly TaskCompletionSource<bool> _programInputReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Auxiliary output routing. Key = aux-channel index (0-based).
    private readonly Dictionary<int, int> _auxSources = new();

    // Transition state per ME.
    private readonly Dictionary<int, bool> _inTransition = new();
    private readonly Dictionary<int, string?> _transitionType = new();
    private readonly Dictionary<int, int> _transitionRate = new();

    private DateTimeOffset _lastUpdated = DateTimeOffset.MinValue;

    /// <summary>
    /// Applies a parsed ATEM command block to the snapshot.
    /// </summary>
    /// <param name="commandName">The ATEM command name (trimmed or raw 4-byte ASCII).</param>
    /// <param name="data">The raw command-block data bytes (excluding the 8-byte block header).</param>
    public void Apply(string commandName, ReadOnlySpan<byte> data)
    {
        var name = commandName.TrimEnd('\0');

        lock (_lock)
        {
            switch (name)
            {
                // Program Input state
                // Older style: PrgI
                // Newer style seen on your device: Prp
                // Expected shape: [ME, 0x00, inputH, inputL]
                case "PrgI" when data.Length >= 4:
                case "Prp"  when data.Length >= 4:
                    _programInputs[data[0]] = (data[2] << 8) | data[3];
                    _lastUpdated = DateTimeOffset.UtcNow;
                    _programInputReady.TrySetResult(true);
                    break;

                // Preview Input state
                // Older style: PrvI
                // Possible newer style: Prv
                case "PrvI" when data.Length >= 4:
                case "Prv"  when data.Length >= 4:
                    _previewInputs[data[0]] = (data[2] << 8) | data[3];
                    _lastUpdated = DateTimeOffset.UtcNow;
                    break;

                // Aux Source routing: [channel, 0x00, srcH, srcL]
                case "AuxS" when data.Length >= 4:
                    _auxSources[data[0]] = (data[2] << 8) | data[3];
                    _lastUpdated = DateTimeOffset.UtcNow;
                    break;

                // Transition State: [ME, flags, ...]
                // Bit 0 of flags = inTransition
                case "TrSS" when data.Length >= 2:
                    _inTransition[data[0]] = (data[1] & 0x01) != 0;
                    _lastUpdated = DateTimeOffset.UtcNow;
                    break;

                // Transition Mix rate: [ME, 0x00, rateH, rateL]
                case "TMxP" when data.Length >= 4:
                    _transitionRate[data[0]] = (data[2] << 8) | data[3];
                    _lastUpdated = DateTimeOffset.UtcNow;
                    break;

                // Transition Type: [ME, 0x00, type, 0x00]
                // type: 0=Mix, 1=Dip, 2=Wipe, 3=DVE, 4=Stinger
                case "TrPr" when data.Length >= 3:
                    _transitionType[data[0]] = data[2] switch
                    {
                        0 => "mix",
                        1 => "dip",
                        2 => "wipe",
                        3 => "dve",
                        4 => "stinger",
                        _ => null
                    };
                    _lastUpdated = DateTimeOffset.UtcNow;
                    break;
            }
        }
    }

    /// <summary>
    /// Returns a snapshot of the current state for ME 0 (the primary Mix/Effect bus).
    /// Returns <c>null</c> if no state has been received yet.
    /// </summary>
    public AtemState? ToAtemState()
    {
        lock (_lock)
        {
            if (_lastUpdated == DateTimeOffset.MinValue)
                return null;

            const int me = 0;

            return new AtemState
            {
                ProgramInputId     = _programInputs.GetValueOrDefault(me),
                PreviewInputId     = _previewInputs.GetValueOrDefault(me),
                InTransition       = _inTransition.GetValueOrDefault(me),
                LastTransitionType = _transitionType.GetValueOrDefault(me),
                LastTransitionRate = _transitionRate.TryGetValue(me, out var rate) ? rate : null,
                Timestamp          = _lastUpdated
            };
        }
    }

    public int GetProgramInput(int me = 0)
    {
        lock (_lock) { return _programInputs.GetValueOrDefault(me); }
    }

    public int GetPreviewInput(int me = 0)
    {
        lock (_lock) { return _previewInputs.GetValueOrDefault(me); }
    }

    public int GetAuxSource(int channel)
    {
        lock (_lock) { return _auxSources.GetValueOrDefault(channel); }
    }

    /// <summary>
    /// Waits until at least one program-input state block has been applied.
    /// </summary>
    public async Task<bool> WaitForProgramInputAsync(TimeSpan timeout, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_programInputs.Count > 0)
                return true;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await _programInputReady.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}