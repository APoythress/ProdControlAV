using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProdControlAV.Agent.Interfaces;

namespace ProdControlAV.Agent.Services;

/// <summary>
/// ATEM connection service implementing connection lifecycle, state management,
/// and command execution for Blackmagic Design ATEM video switchers.
/// 
/// This implementation uses LibAtem 1.0.0 for ATEM protocol communication.
/// Note: LibAtem is licensed under LGPL-3.0. See THIRD-PARTY-NOTICES.md for details.
/// </summary>
public class AtemConnectionService : IAtemConnection, IDisposable
{
    private readonly ILogger<AtemConnectionService> _logger;
    private readonly AtemOptions _options;
    
    private AtemConnectionState _connectionState = AtemConnectionState.Disconnected;
    private AtemState? _currentState;
    private DateTimeOffset _lastStateEmit = DateTimeOffset.MinValue;
    
    // Connection management
    private CancellationTokenSource? _reconnectCts;
    private Task? _reconnectTask;
    private int _reconnectAttempts = 0;
    
    // State coalescing
    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private AtemState? _pendingState;
    private Timer? _statePublishTimer;
    
    // Command serialization
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    
    // LibAtem client (will be initialized in real implementation)
    // private AtemClient? _atemClient;
    
    public AtemConnectionState ConnectionState => _connectionState;
    public AtemState? CurrentState => _currentState;
    
    public event EventHandler<AtemConnectionState>? ConnectionStateChanged;
    public event EventHandler<AtemState>? StateChanged;
    
    public AtemConnectionService(
        ILogger<AtemConnectionService> logger,
        IOptions<AtemOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        
        // Validate configuration
        if (string.IsNullOrWhiteSpace(_options.Ip))
        {
            throw new InvalidOperationException("ATEM IP address must be configured");
        }
        
        // Initialize state publish timer for coalescing
        _statePublishTimer = new Timer(
            PublishPendingState,
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }
    
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            if (_connectionState == AtemConnectionState.Connected)
            {
                _logger.LogInformation("ATEM already connected to {Ip}:{Port}", _options.Ip, _options.Port);
                return;
            }
            
            _logger.LogInformation("Connecting to ATEM at {Ip}:{Port} (Name: {Name})", 
                _options.Ip, _options.Port, _options.Name ?? "Unknown");
            
            UpdateConnectionState(AtemConnectionState.Connecting);
            
            try
            {
                // TODO: Initialize LibAtem client and connect
                // _atemClient = new AtemClient(_options.Ip, _options.Port);
                // _atemClient.OnConnectionStateChanged += HandleConnectionStateChanged;
                // _atemClient.OnStateChanged += HandleStateChanged;
                // await _atemClient.ConnectAsync(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds), ct);
                
                // Stub implementation - simulates successful connection
                await Task.Delay(100, ct);
                
                UpdateConnectionState(AtemConnectionState.Connected);
                _reconnectAttempts = 0;
                
                _logger.LogInformation("Successfully connected to ATEM at {Ip}:{Port}", _options.Ip, _options.Port);
                
                // Initialize state from ATEM
                await RefreshStateAsync(ct);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("ATEM connection cancelled");
                UpdateConnectionState(AtemConnectionState.Disconnected);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to ATEM at {Ip}:{Port}", _options.Ip, _options.Port);
                UpdateConnectionState(AtemConnectionState.Disconnected);
                
                // Start reconnect if enabled
                if (_options.ReconnectEnabled)
                {
                    StartReconnectLoop();
                }
                
                throw;
            }
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    public async Task DisconnectAsync()
    {
        await _commandLock.WaitAsync();
        try
        {
            // Stop reconnect loop if running
            if (_reconnectCts != null)
            {
                _reconnectCts.Cancel();
                _reconnectCts.Dispose();
                _reconnectCts = null;
            }
            
            if (_connectionState == AtemConnectionState.Disconnected)
            {
                return;
            }
            
            _logger.LogInformation("Disconnecting from ATEM at {Ip}:{Port}", _options.Ip, _options.Port);
            
            try
            {
                // TODO: Disconnect LibAtem client
                // await _atemClient?.DisconnectAsync();
                // _atemClient?.Dispose();
                // _atemClient = null;
                
                await Task.Delay(50); // Stub
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during ATEM disconnect");
            }
            
            UpdateConnectionState(AtemConnectionState.Disconnected);
            _currentState = null;
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    public async Task CutToProgramAsync(int programInputId, CancellationToken ct = default)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            ValidateInputId(programInputId);
            
            _logger.LogInformation("ATEM: Cut to Program - Input {InputId}", programInputId);
            
            // TODO: Use LibAtem to execute cut
            // _atemClient.SendCommand(new CutCommand { InputId = programInputId });
            
            // Stub: Simulate command execution
            await Task.Delay(50, ct);
            
            // Update local state optimistically
            UpdateStateField(s => s.ProgramInputId = programInputId);
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    public async Task FadeToProgramAsync(int programInputId, int? transitionRate = null, CancellationToken ct = default)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            ValidateInputId(programInputId);
            
            var rate = transitionRate ?? _options.TransitionDefaultRate;
            
            _logger.LogInformation("ATEM: Fade to Program - Input {InputId}, Rate {Rate} frames", 
                programInputId, rate);
            
            // TODO: Use LibAtem to execute fade transition
            // _atemClient.SendCommand(new SetPreviewInputCommand { InputId = programInputId });
            // _atemClient.SendCommand(new SetTransitionTypeCommand { Type = TransitionType.Mix });
            // _atemClient.SendCommand(new SetTransitionRateCommand { Rate = rate });
            // _atemClient.SendCommand(new AutoTransitionCommand());
            
            // Stub: Simulate command execution
            await Task.Delay(100, ct);
            
            // Update local state
            UpdateStateField(s =>
            {
                s.ProgramInputId = programInputId;
                s.LastTransitionType = "mix";
                s.LastTransitionRate = rate;
            });
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    public async Task SetPreviewAsync(int previewInputId, CancellationToken ct = default)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            ValidateInputId(previewInputId);
            
            _logger.LogInformation("ATEM: Set Preview - Input {InputId}", previewInputId);
            
            // TODO: Use LibAtem to set preview
            // _atemClient.SendCommand(new SetPreviewInputCommand { InputId = previewInputId });
            
            // Stub: Simulate command execution
            await Task.Delay(50, ct);
            
            // Update local state
            UpdateStateField(s => s.PreviewInputId = previewInputId);
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    public async Task<List<AtemMacro>> ListMacrosAsync(CancellationToken ct = default)
    {
        EnsureConnected();
        
        _logger.LogInformation("ATEM: Listing available macros");
        
        // TODO: Use LibAtem to retrieve macro list
        // var macros = _atemClient.GetMacroPool();
        // return macros.Select(m => new AtemMacro { MacroId = m.Id, Name = m.Name }).ToList();
        
        // Stub: Return empty list
        await Task.Delay(50, ct);
        return new List<AtemMacro>();
    }
    
    public async Task RunMacroAsync(int macroId, CancellationToken ct = default)
    {
        await _commandLock.WaitAsync(ct);
        try
        {
            EnsureConnected();
            
            _logger.LogInformation("ATEM: Run Macro - ID {MacroId}", macroId);
            
            // TODO: Use LibAtem to run macro
            // _atemClient.SendCommand(new RunMacroCommand { MacroId = macroId });
            
            // Stub: Simulate command execution
            await Task.Delay(100, ct);
        }
        finally
        {
            _commandLock.Release();
        }
    }
    
    private void EnsureConnected()
    {
        if (_connectionState != AtemConnectionState.Connected)
        {
            throw new InvalidOperationException(
                $"ATEM is not connected (current state: {_connectionState}). Cannot execute command.");
        }
    }
    
    private void ValidateInputId(int inputId)
    {
        // TODO: Validate against known inputs from ATEM state
        // For now, accept 1-20 as reasonable range for most ATEMs
        if (inputId < 1 || inputId > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(inputId), 
                $"Input ID {inputId} is out of valid range (1-20)");
        }
    }
    
    private async Task RefreshStateAsync(CancellationToken ct)
    {
        // TODO: Query current state from ATEM
        // var state = await _atemClient.GetStateAsync(ct);
        
        // Stub: Initialize with defaults
        await Task.Delay(50, ct);
        
        var state = new AtemState
        {
            ProgramInputId = 1,
            PreviewInputId = 2,
            Timestamp = DateTimeOffset.UtcNow
        };
        
        UpdateState(state);
    }
    
    private void UpdateConnectionState(AtemConnectionState newState)
    {
        if (_connectionState != newState)
        {
            var oldState = _connectionState;
            _connectionState = newState;
            
            _logger.LogInformation("ATEM connection state changed: {OldState} -> {NewState}", 
                oldState, newState);
            
            ConnectionStateChanged?.Invoke(this, newState);
        }
    }
    
    private void UpdateStateField(Action<AtemState> updateAction)
    {
        var state = _currentState ?? new AtemState();
        updateAction(state);
        state.Timestamp = DateTimeOffset.UtcNow;
        UpdateState(state);
    }
    
    private void UpdateState(AtemState newState)
    {
        _stateLock.Wait();
        try
        {
            // Check if state actually changed
            if (_options.StateEmitOnChangeOnly && _currentState != null)
            {
                if (_currentState.ProgramInputId == newState.ProgramInputId &&
                    _currentState.PreviewInputId == newState.PreviewInputId &&
                    _currentState.InTransition == newState.InTransition)
                {
                    return; // No change
                }
            }
            
            _currentState = newState;
            _pendingState = newState;
            
            // Check if we should emit immediately or coalesce
            var now = DateTimeOffset.UtcNow;
            var elapsed = (now - _lastStateEmit).TotalMilliseconds;
            
            if (elapsed >= _options.StatePublishIntervalMs)
            {
                // Emit immediately
                EmitState(newState);
            }
            else
            {
                // Schedule emission
                var delay = _options.StatePublishIntervalMs - (int)elapsed;
                _statePublishTimer?.Change(delay, Timeout.Infinite);
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }
    
    private void PublishPendingState(object? state)
    {
        _stateLock.Wait();
        try
        {
            if (_pendingState != null)
            {
                EmitState(_pendingState);
                _pendingState = null;
            }
        }
        finally
        {
            _stateLock.Release();
        }
    }
    
    private void EmitState(AtemState state)
    {
        _lastStateEmit = DateTimeOffset.UtcNow;
        _logger.LogDebug("ATEM state: Program={Program}, Preview={Preview}", 
            state.ProgramInputId, state.PreviewInputId);
        StateChanged?.Invoke(this, state);
    }
    
    private void StartReconnectLoop()
    {
        if (_reconnectCts != null)
        {
            return; // Already running
        }
        
        _reconnectCts = new CancellationTokenSource();
        _reconnectTask = ReconnectLoopAsync(_reconnectCts.Token);
    }
    
    private async Task ReconnectLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting ATEM reconnect loop");
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Calculate backoff delay
                var delay = CalculateBackoffDelay(_reconnectAttempts);
                _logger.LogInformation("ATEM reconnect attempt {Attempt} in {Delay}s", 
                    _reconnectAttempts + 1, delay);
                
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
                
                _reconnectAttempts++;
                
                try
                {
                    await ConnectAsync(ct);
                    
                    // Success - stop reconnect loop
                    _logger.LogInformation("ATEM reconnect successful after {Attempts} attempts", 
                        _reconnectAttempts);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ATEM reconnect attempt {Attempt} failed", _reconnectAttempts);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ATEM reconnect loop cancelled");
                break;
            }
        }
    }
    
    private int CalculateBackoffDelay(int attempt)
    {
        // Exponential backoff with jitter
        var baseDelay = _options.ReconnectMinDelaySeconds;
        var maxDelay = _options.ReconnectMaxDelaySeconds;
        
        var exponentialDelay = baseDelay * Math.Pow(2, Math.Min(attempt, 5));
        var delay = Math.Min(exponentialDelay, maxDelay);
        
        // Add jitter (±20%) using Random.Shared for better randomness
        var jitter = delay * 0.2 * (Random.Shared.NextDouble() * 2 - 1);
        
        return (int)Math.Max(baseDelay, delay + jitter);
    }
    
    public void Dispose()
    {
        _reconnectCts?.Cancel();
        _reconnectCts?.Dispose();
        _statePublishTimer?.Dispose();
        _stateLock.Dispose();
        _commandLock.Dispose();
        
        // TODO: Dispose LibAtem client
        // _atemClient?.Dispose();
    }
}
