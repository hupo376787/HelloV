using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using HelloV.Infrastructure;
using HelloV.Localization;
using HelloV.Models;
using HelloV.Services;

namespace HelloV.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ICameraService _cameraService;
    private readonly Func<IGestureRecognizer> _recognizerFactory;
    private readonly object _recognizerGate = new();
    private readonly GestureEffectMatcher _effectMatcher;
    private readonly GestureStabilizer _stabilizer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _inferenceClock = Stopwatch.StartNew();
    private readonly Stopwatch _previewFpsClock = Stopwatch.StartNew();
    private IGestureRecognizer? _recognizer;
    private VideoFrame? _latestPreviewFrame;
    private CameraDeviceInfo? _selectedCamera;
    private GestureEffectDemoItem? _selectedEffectDemo;
    private string _statusText = string.Empty;
    private string _cameraStateKey = "StatusInitializingCamera";
    private object?[] _cameraStateArguments = [];
    private string _modelStateKey = "StatusLoadingModel";
    private object?[] _modelStateArguments = [];
    private string _resolutionText = string.Empty;
    private string _gestureText = string.Empty;
    private ReactionKind _reaction;
    private double _reactionAnchorX = 0.5;
    private double _reactionAnchorY = 0.5;
    private int _reactionSequence;
    private bool _initialized;
    private bool _flipPreviewHorizontally;
    private bool _isSettingsPanelVisible;
    private int _inferenceBusy;
    private readonly SemaphoreSlim _cameraSwitchGate = new(1, 1);
    private int _previewDispatchPending;
    private long _lastInferenceMs;
    private readonly int _inferenceIntervalMs;
    private int _presentedFrames;
    private int _disposeState;
    private Task? _recognizerLoadTask;

    public MainViewModel(
        ICameraService cameraService,
        Func<IGestureRecognizer> recognizerFactory,
        AppPlatformKind platformKind,
        LocalizationManager localization)
    {
        _cameraService = cameraService;
        _recognizerFactory = recognizerFactory ?? throw new ArgumentNullException(nameof(recognizerFactory));
        Localization = localization ?? throw new ArgumentNullException(nameof(localization));
        _effectMatcher = new GestureEffectMatcher(Localization);
        IsDesktop = platformKind == AppPlatformKind.Desktop;
        IsMobile = !IsDesktop;
        SettingsPanelWidth = IsMobile ? 330d : 360d;

        // On phones the user already sees the recognized gesture label before the effect. Waiting
        // for three or four slow ONNX passes made the animation appear several seconds late. One
        // positive mobile result now triggers immediately; latching still prevents repeated effects
        // until the gesture is released. Desktop retains the stricter multi-frame filter.
        _stabilizer = IsMobile
            ? new GestureStabilizer(
                requiredHits: 1,
                requiredMissesToRelease: 3,
                addExtraHitForCommonGestures: false)
            : new GestureStabilizer(
                requiredHits: 3,
                requiredMissesToRelease: 5);

        // Some DirectShow drivers expose an unmirrored preview while users expect a mirror.
        // This is render-only and can be toggled without touching camera/inference buffers.
        _flipPreviewHorizontally = IsDesktop;
        // The busy gate still guarantees a single ONNX invocation at a time. A shorter interval
        // only lets the next fresh frame enter as soon as the previous inference finishes.
        _inferenceIntervalMs = IsMobile ? 75 : 140;

        SwitchCameraCommand = new AsyncRelayCommand(
            SwitchMobileCameraAsync,
            () => IsMobile && Cameras.Count > 1);
        PreviewEffectCommand = new AsyncRelayCommand(
            PreviewSelectedEffectAsync,
            () => SelectedEffectDemo is not null);
        ToggleSettingsPanelCommand = new AsyncRelayCommand(ToggleSettingsPanelAsync);
        CloseSettingsPanelCommand = new AsyncRelayCommand(CloseSettingsPanelAsync);
        RescanLanguagesCommand = new AsyncRelayCommand(RescanLanguagesAsync);

        Localization.LanguageChanged += OnLanguageChanged;
        RebuildEffectDemos();
        _gestureText = Localization["WaitingGesture"];
        UpdateCombinedStatus();
    }

    public ObservableCollection<CameraDeviceInfo> Cameras { get; } = [];
    public ObservableCollection<GestureEffectDemoItem> EffectDemos { get; } = [];
    public LocalizationManager Localization { get; }
    public bool IsDesktop { get; }
    public bool IsMobile { get; }
    public double SettingsPanelWidth { get; }
    public AsyncRelayCommand SwitchCameraCommand { get; }
    public AsyncRelayCommand PreviewEffectCommand { get; }
    public AsyncRelayCommand ToggleSettingsPanelCommand { get; }
    public AsyncRelayCommand CloseSettingsPanelCommand { get; }
    public AsyncRelayCommand RescanLanguagesCommand { get; }

    public bool IsSettingsPanelVisible
    {
        get => _isSettingsPanelVisible;
        set => SetField(ref _isSettingsPanelVisible, value);
    }

    public bool FlipPreviewHorizontally
    {
        get => _flipPreviewHorizontally;
        set => SetField(ref _flipPreviewHorizontally, value);
    }

    public CameraDeviceInfo? SelectedCamera
    {
        get => _selectedCamera;
        set
        {
            if (!SetField(ref _selectedCamera, value) || !_initialized || value is null)
                return;

            _ = ChangeCameraAsync(value);
        }
    }

    public GestureEffectDemoItem? SelectedEffectDemo
    {
        get => _selectedEffectDemo;
        set
        {
            if (SetField(ref _selectedEffectDemo, value))
                PreviewEffectCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string ResolutionText
    {
        get => _resolutionText;
        private set => SetField(ref _resolutionText, value);
    }

    public string GestureText
    {
        get => _gestureText;
        private set => SetField(ref _gestureText, value);
    }

    public ReactionKind Reaction
    {
        get => _reaction;
        private set => SetField(ref _reaction, value);
    }

    public double ReactionAnchorX
    {
        get => _reactionAnchorX;
        private set => SetField(ref _reactionAnchorX, value);
    }

    public double ReactionAnchorY
    {
        get => _reactionAnchorY;
        private set => SetField(ref _reactionAnchorY, value);
    }

    public int ReactionSequence
    {
        get => _reactionSequence;
        private set => SetField(ref _reactionSequence, value);
    }

    public async Task InitializeAsync()
    {
        if (_initialized || Volatile.Read(ref _disposeState) != 0)
            return;

        _initialized = true;
        SetCameraState("StatusEnumeratingCameras");
        SetModelState("StatusLoadingModel");

        // Start ONNX Runtime initialization on a worker thread before touching the camera.
        // Do not await it here: the window and preview can become usable while the model graph
        // is being read and optimized in the background.
        _recognizerLoadTask = LoadRecognizerAsync();

        try
        {
            var cameras = await _cameraService.GetCamerasAsync(_lifetime.Token);
            foreach (var camera in cameras)
                Cameras.Add(camera);

            if (Cameras.Count == 0)
            {
                SetCameraState("StatusNoCamera");
                return;
            }

            var initial = IsMobile
                ? Cameras.FirstOrDefault(x => x.Facing == CameraFacing.Front) ?? Cameras[0]
                : Cameras[0];

            _selectedCamera = initial;
            OnPropertyChanged(nameof(SelectedCamera));
            SwitchCameraCommand.RaiseCanExecuteChanged();
            await ChangeCameraAsync(initial);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetCameraState("StatusCameraInitializationFailed", ex.Message);
        }
    }

    private async Task LoadRecognizerAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        IGestureRecognizer? loadedRecognizer = null;

        try
        {
            loadedRecognizer = await Task.Run(_recognizerFactory, _lifetime.Token);

            IGestureRecognizer? previous;
            lock (_recognizerGate)
            {
                if (_lifetime.IsCancellationRequested || Volatile.Read(ref _disposeState) != 0)
                {
                    loadedRecognizer.Dispose();
                    return;
                }

                previous = _recognizer;
                Volatile.Write(ref _recognizer, loadedRecognizer);
                // Ownership has moved into _recognizer.
                loadedRecognizer = null;
            }

            previous?.Dispose();
            var recognizer = Volatile.Read(ref _recognizer);
            if (recognizer is null)
            {
                _modelStateKey = "StatusModelNotLoaded";
                _modelStateArguments = [];
            }
            else if (recognizer.IsReady)
            {
                _modelStateKey = "StatusModelLoaded";
                _modelStateArguments = [recognizer.StateText, stopwatch.Elapsed.TotalSeconds];
            }
            else
            {
                _modelStateKey = "StatusModelMissing";
                _modelStateArguments = [GestureRecognizerFactory.SupportedModelNamesText];
            }

            Debug.WriteLine($"手势模型后台加载完成：{stopwatch.ElapsedMilliseconds} ms");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            loadedRecognizer?.Dispose();
            _modelStateKey = "StatusModelLoadFailed";
            _modelStateArguments = [ex.Message];
            Debug.WriteLine($"手势模型加载失败：{ex}");
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested && Volatile.Read(ref _disposeState) == 0)
                Dispatcher.UIThread.Post(UpdateCombinedStatus);
        }
    }

    private async Task ChangeCameraAsync(CameraDeviceInfo camera)
    {
        try
        {
            await _cameraSwitchGate.WaitAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // A newer selection may have been queued while the previous native camera was closing.
            // Skip obsolete requests so Camera2 never receives overlapping open/close operations.
            if (!Equals(camera, _selectedCamera))
                return;

            SetCameraState("StatusOpeningCamera", camera.DisplayName);
            DropPendingPreviewFrame();

            // AndroidCameraService performs stop + close + reopen atomically under its own
            // lifecycle gate. Calling StopAsync separately here used to race with rear-camera open.
            await _cameraService.StartAsync(camera, OnFrameAsync, _lifetime.Token);

            if (Equals(camera, _selectedCamera))
                SetCameraState("StatusCameraReady", camera.DisplayName);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (Equals(camera, _selectedCamera))
                SetCameraState("StatusSwitchCameraFailed", ex.Message);
        }
        finally
        {
            _cameraSwitchGate.Release();
        }
    }

    private async Task SwitchMobileCameraAsync()
    {
        var currentFacing = SelectedCamera?.Facing;
        var targetFacing = currentFacing == CameraFacing.Front
            ? CameraFacing.Back
            : CameraFacing.Front;
        var target = Cameras.FirstOrDefault(x => x.Facing == targetFacing)
                     ?? Cameras.FirstOrDefault(x => x != SelectedCamera);
        if (target is null || Equals(target, _selectedCamera))
            return;

        // Bypass the property setter so the command can await the real switch operation instead
        // of starting a fire-and-forget task that may overlap with another tap.
        _selectedCamera = target;
        OnPropertyChanged(nameof(SelectedCamera));
        await ChangeCameraAsync(target);
    }

    private Task ToggleSettingsPanelAsync()
    {
        IsSettingsPanelVisible = !IsSettingsPanelVisible;
        return Task.CompletedTask;
    }

    private Task CloseSettingsPanelAsync()
    {
        IsSettingsPanelVisible = false;
        return Task.CompletedTask;
    }

    private Task RescanLanguagesAsync()
    {
        Localization.Rescan();
        return Task.CompletedTask;
    }

    private Task PreviewSelectedEffectAsync()
    {
        if (SelectedEffectDemo is null)
            return Task.CompletedTask;

        ReactionAnchorX = 0.5;
        ReactionAnchorY = 0.5;
        Reaction = SelectedEffectDemo.Kind;
        GestureText = Localization.Format("AnimationPreviewFormat", SelectedEffectDemo.DisplayName);
        ReactionSequence++;
        return Task.CompletedTask;
    }

    private ValueTask OnFrameAsync(VideoFrame rawFrame)
    {
        if (_lifetime.IsCancellationRequested)
        {
            rawFrame.Dispose();
            return ValueTask.CompletedTask;
        }

        VideoFrame? frame = rawFrame;
        try
        {
            // Keep the camera frame in its native orientation for preview. CameraPreviewControl
            // applies rotation and mirroring as a render transform, avoiding a full-frame copy on
            // every Android frame. Only frames selected for inference are physically normalized.
            var previewReference = frame.Retain();
            Interlocked.Exchange(ref _latestPreviewFrame, previewReference)?.Dispose();
            QueuePreviewRender();

            var recognizer = Volatile.Read(ref _recognizer);
            if (recognizer?.IsReady == true)
            {
                var nowInference = _inferenceClock.ElapsedMilliseconds;
                if (nowInference - Interlocked.Read(ref _lastInferenceMs) >= _inferenceIntervalMs &&
                    Interlocked.CompareExchange(ref _inferenceBusy, 1, 0) == 0)
                {
                    Interlocked.Exchange(ref _lastInferenceMs, nowInference);
                    var inferenceReference = frame.Retain();
                    _ = Task.Run(() => RunInference(recognizer, inferenceReference));
                }
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
                StatusText = Localization.Format("StatusFrameProcessingFailed", ex.Message));
        }
        finally
        {
            frame?.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void QueuePreviewRender()
    {
        if (_lifetime.IsCancellationRequested ||
            Interlocked.CompareExchange(ref _previewDispatchPending, 1, 0) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(RenderLatestPreview, DispatcherPriority.Render);
    }

    private void RenderLatestPreview()
    {
        try
        {
            var frame = Interlocked.Exchange(ref _latestPreviewFrame, null);
            if (frame is null)
                return;

            using (frame)
            {
                if (!_lifetime.IsCancellationRequested)
                    PresentPreview(frame);
            }
        }
        catch (Exception ex)
        {
            StatusText = Localization.Format("StatusFrameDisplayFailed", ex.Message);
        }
        finally
        {
            Volatile.Write(ref _previewDispatchPending, 0);
            if (!_lifetime.IsCancellationRequested &&
                Volatile.Read(ref _latestPreviewFrame) is not null)
            {
                QueuePreviewRender();
            }
        }
    }

    private void RunInference(IGestureRecognizer recognizer, VideoFrame frame)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
                return;

            // OnnxImagePreprocessor samples rotation/mirroring metadata directly while resizing to
            // the model input. Avoiding a full 1080p rotate-and-copy removes a large part of the
            // mobile recognition latency.
            var frameResult = recognizer.Recognize(frame);
            var currentReaction = _effectMatcher.Match(frameResult);
            var triggeredReaction = _stabilizer.Push(currentReaction);
            var gestureText = currentReaction?.DisplayText ?? DescribeDetections(frameResult);

            Dispatcher.UIThread.Post(() =>
            {
                GestureText = gestureText;
                if (triggeredReaction is null || triggeredReaction.Kind == ReactionKind.None)
                    return;

                // Anchor first, then sequence: ReactionOverlay snapshots the anchor when the
                // sequence changes, so animations originate close to the detected hands.
                ReactionAnchorX = triggeredReaction.Bounds.CenterX;
                ReactionAnchorY = triggeredReaction.Bounds.CenterY;
                Reaction = triggeredReaction.Kind;
                ReactionSequence++;
            });
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
                StatusText = Localization.Format("StatusGestureRecognitionFailed", ex.Message));
        }
        finally
        {
            frame.Dispose();
            Volatile.Write(ref _inferenceBusy, 0);
        }
    }

    private string DescribeDetections(GestureFrameResult result)
    {
        var strongest = result.Detections.OrderByDescending(x => x.Confidence).FirstOrDefault();
        return strongest is null
            ? Localization["WaitingGesture"]
            : Localization.Format(
                "GestureWithConfidence",
                GestureCatalog.DisplayName(strongest.Kind, Localization),
                strongest.Confidence);
    }

    private void PresentPreview(VideoFrame frame)
    {
        // The view synchronously copies frame.Pixels into one persistent WriteableBitmap.
        // Rotation/mirroring metadata is rendered by CameraPreviewControl without changing pixels.
        PreviewFrameReady?.Invoke(frame);

        var normalizedRotation = ((frame.RotationDegrees % 360) + 360) % 360;
        var displayWidth = normalizedRotation is 90 or 270 ? frame.Height : frame.Width;
        var displayHeight = normalizedRotation is 90 or 270 ? frame.Width : frame.Height;

        _presentedFrames++;
        var elapsed = _previewFpsClock.Elapsed.TotalSeconds;
        if (elapsed >= 0.75)
        {
            var fps = _presentedFrames / elapsed;
            ResolutionText = $"{displayWidth} × {displayHeight} · {fps:F0} FPS";
            _presentedFrames = 0;
            _previewFpsClock.Restart();
        }
        else if (string.IsNullOrEmpty(ResolutionText))
        {
            ResolutionText = $"{displayWidth} × {displayHeight}";
        }
    }

    private void SetCameraState(string key, params object?[] arguments)
    {
        _cameraStateKey = key;
        _cameraStateArguments = arguments;
        UpdateCombinedStatus();
    }

    private void SetModelState(string key, params object?[] arguments)
    {
        _modelStateKey = key;
        _modelStateArguments = arguments;
        UpdateCombinedStatus();
    }

    private void UpdateCombinedStatus()
    {
        var cameraText = Localization.Format(_cameraStateKey, _cameraStateArguments);
        var modelText = Localization.Format(_modelStateKey, _modelStateArguments);
        StatusText = string.IsNullOrWhiteSpace(modelText)
            ? cameraText
            : $"{cameraText} · {modelText}";
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnLanguageChanged(sender, e));
            return;
        }

        var selectedKind = SelectedEffectDemo?.Kind;
        RebuildEffectDemos(selectedKind);
        GestureText = Localization["WaitingGesture"];
        UpdateCombinedStatus();
    }

    private void RebuildEffectDemos(ReactionKind? preferredKind = null)
    {
        preferredKind ??= _selectedEffectDemo?.Kind;
        EffectDemos.Clear();
        foreach (var item in GestureCatalog.CreateDemoItems(Localization))
            EffectDemos.Add(item);

        _selectedEffectDemo = preferredKind is null
            ? EffectDemos.FirstOrDefault()
            : EffectDemos.FirstOrDefault(x => x.Kind == preferredKind.Value)
              ?? EffectDemos.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedEffectDemo));
        PreviewEffectCommand.RaiseCanExecuteChanged();
    }

    private void DropPendingPreviewFrame() =>
        Interlocked.Exchange(ref _latestPreviewFrame, null)?.Dispose();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            return;

        Localization.LanguageChanged -= OnLanguageChanged;
        _lifetime.Cancel();
        DropPendingPreviewFrame();
        try
        {
            await _cameraService.StopAsync();
        }
        catch
        {
            // Ignore shutdown races from native camera APIs.
        }

        await _cameraService.DisposeAsync();

        // Give an already-running inference a short chance to leave the session before disposal.
        var waitClock = Stopwatch.StartNew();
        while (Volatile.Read(ref _inferenceBusy) != 0 && waitClock.ElapsedMilliseconds < 1000)
            await Task.Delay(20);

        IGestureRecognizer? recognizerToDispose;
        lock (_recognizerGate)
        {
            recognizerToDispose = _recognizer;
            Volatile.Write(ref _recognizer, null);
        }

        recognizerToDispose?.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>
    /// Raised synchronously on Avalonia's UI thread for the newest available preview frame.
    /// Subscribers must consume/copy pixels before returning and must not retain the frame.
    /// </summary>
    public event Action<VideoFrame>? PreviewFrameReady;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
