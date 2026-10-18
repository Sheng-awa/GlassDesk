using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace GlassDesk.App;

/// <summary>System desktop acrylic connected directly to the XAML window target.</summary>
internal sealed class SettingsAcrylicBackdrop : SystemBackdrop
{
    readonly Dictionary<ICompositionSupportsSystemBackdrop, BackdropTarget> _controllers = new();
    float _tintOpacity;

    public SettingsAcrylicBackdrop(float tintOpacity)
        => _tintOpacity = Math.Clamp(tintOpacity, .1f, .9f);

    public static bool IsSupported => DesktopAcrylicController.IsSupported();

    public void SetTintOpacity(float opacity)
    {
        opacity = Math.Clamp(opacity, .1f, .9f);
        if (Math.Abs(_tintOpacity - opacity) < .001f) return;
        _tintOpacity = opacity;
        foreach (var entry in _controllers.Values) entry.Controller.TintOpacity = opacity;
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        if (!IsSupported) return;
        var controller = new DesktopAcrylicController
        {
            TintColor = Color.FromArgb(255, 255, 255, 255), TintOpacity = _tintOpacity,
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
