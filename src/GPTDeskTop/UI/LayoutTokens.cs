namespace GPTDeskTop.UI;

/// <summary>
/// Central layout tokens for GPTDeskTop. New UI should consume these values instead of
/// inventing per-screen spacing, sizing or breakpoint magic numbers.
/// </summary>
public static class LayoutTokens
{
    public const int Space4 = 4;
    public const int Space8 = 8;
    public const int Space12 = 12;
    public const int Space16 = 16;
    public const int Space24 = 24;
    public const int Space32 = 32;

    public const int ControlHeight = 36;
    public const int CompactControlHeight = 34;
    public const int IconButtonSize = 36;
    public const int CardRadius = 10;
    public const int ControlRadius = 8;

    public const int CompactBreakpoint = 860;
    public const int NarrowBreakpoint = 720;
    public const int ComfortableContentWidth = 1180;

    public const int MinimumUsableWidth = 640;
    public const int MinimumUsableHeight = 480;
    public const int SplitPaneMinimum = 180;

    public static readonly Padding ControlMargin = new(Space4);
    public static readonly Padding FieldPadding = new(Space8, Space4, Space8, Space4);
    public static readonly Padding CardPadding = new(Space16);
    public static readonly Padding PagePadding = new(Space16);
}
