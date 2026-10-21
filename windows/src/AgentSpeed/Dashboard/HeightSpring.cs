using System.Windows;
using System.Windows.Media;
using AgentSpeed.Presentation.Motion;

namespace AgentSpeed.Dashboard;

/// <summary>
/// Drives an element's <see cref="FrameworkElement.Height"/> with the layout spring (DESIGN section 6). Frames run only
/// while it moves; a retarget mid-flight keeps the current velocity.
/// </summary>
internal sealed class HeightSpring(FrameworkElement element)
{
    private const double MaxStep = 1 / 30.0;

    private DampedSpring _spring = DampedSpring.Layout();
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private double _target;

    /// <summary>After each animated frame, with the new height.</summary>
    public event EventHandler<double>? Stepped;

    /// <summary>The height reached its target, by settling or by <see cref="Finish"/>.</summary>
    public event EventHandler? Settled;

    public bool IsRunning { get; private set; }

    public double Target => _target;

    /// <summary>Starts from <paramref name="from"/>, or continues the running animation toward the new target.</summary>
    public void AnimateTo(double from, double target)
    {
        _target = Math.Max(0, target);
        if (IsRunning)
        {
            return;
        }

        _spring = DampedSpring.Layout(Math.Max(0, from));
        _lastFrame = TimeSpan.MinValue;
        IsRunning = true;
        CompositionTarget.Rendering += OnRendering;
    }

    public void Retarget(double target) => _target = Math.Max(0, target);

    /// <summary>Stops any animation and jumps to <paramref name="height"/> without raising <see cref="Settled"/>.</summary>
    public void Snap(double height)
    {
        Stop();
        _target = Math.Max(0, height);
        _spring.Snap(_target);
        element.Height = _target;
    }

    /// <summary>Completes a running animation at once.</summary>
    public void Finish()
    {
        if (!IsRunning)
        {
            return;
        }

        Snap(_target);
        Settled?.Invoke(this, EventArgs.Empty);
    }

    private void Stop()
    {
        if (IsRunning)
        {
            CompositionTarget.Rendering -= OnRendering;
            IsRunning = false;
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        // Rendering can fire more than once per composed frame; step only when the frame time advances.
        var now = e is RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        if (now == _lastFrame)
        {
            return;
        }

        var dt = _lastFrame == TimeSpan.MinValue ? 1 / 60.0 : Math.Clamp((now - _lastFrame).TotalSeconds, 0, MaxStep);
        _lastFrame = now;
        _spring.Step(_target, dt);
        if (_spring.IsSettled(_target, 0.25))
        {
            Finish();
            return;
        }

        var height = Math.Max(0, _spring.Value);
        element.Height = height;
        Stepped?.Invoke(this, height);
    }
}
