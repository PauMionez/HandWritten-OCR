using System.IO;
using System.Windows;
using System.Windows.Input;

namespace PyOCR.Behaviors;

/// <summary>
/// Attached behavior that wires drag-and-drop image handling to an ICommand.
/// Also exposes an IsDragOver attached property so XAML Style triggers can
/// change the drop zone appearance without any code-behind.
/// </summary>
public static class DropBehavior
{
    // ── DropCommand ───────────────────────────────────────────────────────────

    public static readonly DependencyProperty DropCommandProperty =
        DependencyProperty.RegisterAttached(
            "DropCommand", typeof(ICommand), typeof(DropBehavior),
            new PropertyMetadata(null, OnDropCommandChanged));

    public static ICommand GetDropCommand(DependencyObject d)
        => (ICommand)d.GetValue(DropCommandProperty);

    public static void SetDropCommand(DependencyObject d, ICommand value)
        => d.SetValue(DropCommandProperty, value);

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;

        // Always unsubscribe first to avoid double-subscribing.
        el.Drop      -= OnDrop;
        el.DragOver  -= OnDragOver;
        el.DragEnter -= OnDragEnter;
        el.DragLeave -= OnDragLeave;

        if (e.NewValue is ICommand)
        {
            el.AllowDrop  = true;
            el.Drop      += OnDrop;
            el.DragOver  += OnDragOver;
            el.DragEnter += OnDragEnter;
            el.DragLeave += OnDragLeave;
        }
    }

    // ── IsDragOver (read by XAML Style triggers) ──────────────────────────────

    public static readonly DependencyProperty IsDragOverProperty =
        DependencyProperty.RegisterAttached(
            "IsDragOver", typeof(bool), typeof(DropBehavior),
            new PropertyMetadata(false));

    public static bool GetIsDragOver(DependencyObject d)
        => (bool)d.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject d, bool value)
        => d.SetValue(IsDragOverProperty, value);

    // ── Event handlers ────────────────────────────────────────────────────────

    private static void OnDragEnter(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject d && IsImageDrag(e))
            SetIsDragOver(d, true);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject d)
            SetIsDragOver(d, false);
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsImageDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is DependencyObject d) SetIsDragOver(d, false);
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        var path  = files?.FirstOrDefault(IsImageFile);
        if (path is null) return;

        var cmd = GetDropCommand((DependencyObject)sender);
        if (cmd?.CanExecute(path) == true) cmd.Execute(path);
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static bool IsImageDrag(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        return files?.Any(IsImageFile) == true;
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tiff" or ".tif" or ".webp";
    }
}
