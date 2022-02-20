using Microsoft.AspNetCore.Components.Web;
namespace OxyPlot.SkiaSharp.Blazor;
public static class EventArgsConversionUtil
{
    internal static OxyMouseDownEventArgs OxyMouseEventArgs(this MouseEventArgs args) => new()
    {
        Position = new ScreenPoint(args.OffsetX, args.OffsetY),
        ClickCount = (int)args.Detail,
        ChangedButton = args.OxyMouseButton(),
        ModifierKeys = args.OxyModifierKeys()
    };
    internal static OxyMouseButton OxyMouseButton(this MouseEventArgs args) => args.Button switch
    {
        0 => OxyPlot.OxyMouseButton.Left,
        1 => OxyPlot.OxyMouseButton.Middle,
        2 => OxyPlot.OxyMouseButton.Right,
        _ => OxyPlot.OxyMouseButton.None
    };

    internal static string TranslateCursorType(CursorType cursorType) => cursorType switch
    {
        CursorType.Pan => "grabbing",
        CursorType.ZoomRectangle => "zoom-in",
        CursorType.ZoomHorizontal => "col-resize",
        CursorType.ZoomVertical => "row-resize",
        CursorType.Default => "default",
        _ => "default",
    };

    internal static OxyModifierKeys OxyModifierKeys(this MouseEventArgs args)
    {
        var result = OxyPlot.OxyModifierKeys.None;
        if (args.ShiftKey) result |= OxyPlot.OxyModifierKeys.Shift;
        if (args.AltKey) result |= OxyPlot.OxyModifierKeys.Alt;
        if (args.CtrlKey) result |= OxyPlot.OxyModifierKeys.Control;
        if (args.MetaKey) result |= OxyPlot.OxyModifierKeys.Windows;
        return result;
    }
    internal static OxyModifierKeys OxyModifierKeys(this WheelEventArgs args) => ((MouseEventArgs)args).OxyModifierKeys();

    internal static OxyMouseWheelEventArgs OxyMouseWheelEventArgs(this WheelEventArgs args) => new()
    {
        Position = new ScreenPoint(args.OffsetX, args.OffsetY),
        Delta = (int)(args.DeltaY != 0 ? -args.DeltaY : args.DeltaX),
        ModifierKeys = OxyModifierKeys(args)
    };
}