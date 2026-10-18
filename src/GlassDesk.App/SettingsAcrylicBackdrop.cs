using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDesk.App;

/// <summary>
/// Fixed light desktop acrylic for the settings window. The user-facing
/// "透明度" slider keeps driving the widget glass; it must not change this
/// window's material, otherwise the settings surface turns grey on every drag.
/// </summary>
internal sealed class SettingsAcrylicBackdrop : SystemBackdrop
{
    /// <summary>The single settings material tint; deliberately not user adjustable.</summary>
    public const float TintOpacityValue = .86f;

    readonly Dictionary<ICompositionSupportsSystemBackdrop, BackdropTarget> _controllers = new();

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        if (!IsSupported) return;
        var controller = new DesktopAcrylicController
        {
            TintColor = Color.FromArgb(255, 255, 255, 255), TintOpacity = TintOpacityValue,
            LuminosityOpacity = .22f, FallbackColor = Color.FromArgb(255, 240, 242, 245)
        };
        var entry = new BackdropTarget(controller, target);
        try
        {
            // The HWND region owns the two visible shapes and the desktop gap.
            // Keep the native system backdrop brush on its real XAML target;
            // it must not be replaced by a separately captured visual mask.
            controller.SetSystemBackdropConfiguration(entry.Configuration);
            if (!entry.Connect()) throw new InvalidOperationException("The settings acrylic material target could not be connected.");
            _controllers.Add(target, entry);
        }
        catch { entry.Dispose(); throw; }
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        if (_controllers.TryGetValue(target, out var entry))
        {
            // Phase 1 keeps the settings window light; dark theme is out of scope.
            entry.Configuration.Theme = SystemBackdropTheme.Light;
            entry.Configuration.IsInputActive = true;
        }
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        try { if (_controllers.Remove(target, out var entry)) entry.Dispose(); }
        finally { base.OnTargetDisconnected(target); }
    }

    sealed class BackdropTarget : IDisposable
    {
        readonly ICompositionSupportsSystemBackdrop _target;
        bool _connected, _disposed;
        public DesktopAcrylicController Controller { get; }
        public SystemBackdropConfiguration Configuration { get; } = new()
        {
            IsInputActive = true, Theme = SystemBackdropTheme.Light
        };

        public BackdropTarget(DesktopAcrylicController controller, ICompositionSupportsSystemBackdrop target)
        { Controller = controller; _target = target; }

        public bool Connect() => _connected = Controller.AddSystemBackdropTarget(_target);

        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { if (_connected) Controller.RemoveSystemBackdropTarget(_target); }
            finally { _connected = false; Controller.Dispose(); }
        }
    }
}
