from pathlib import Path

root = Path(__file__).resolve().parents[1]
theme = root / 'src/GPTDeskTop/UI/FluentTheme.cs'
text = theme.read_text(encoding='utf-8')

old_palette = '''    public static readonly Color Background = Color.FromArgb(245, 247, 250);
    public static readonly Color Surface = Color.White;
    public static readonly Color SurfaceAlt = Color.FromArgb(248, 250, 252);
    public static readonly Color SurfaceRaised = Color.FromArgb(252, 253, 255);
    public static readonly Color SurfaceHover = Color.FromArgb(241, 245, 249);
    public static readonly Color SurfacePressed = Color.FromArgb(226, 232, 240);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
    public static readonly Color AccentPressed = Color.FromArgb(30, 64, 175);
    public static readonly Color AccentSubtle = Color.FromArgb(239, 246, 255);
    public static readonly Color AccentBorder = Color.FromArgb(147, 197, 253);
    public static readonly Color Text = Color.FromArgb(15, 23, 42);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);
    public static readonly Color MutedStrong = Color.FromArgb(71, 85, 105);
    public static readonly Color DisabledText = Color.FromArgb(148, 163, 184);
    public static readonly Color DisabledSurface = Color.FromArgb(241, 245, 249);
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color BorderStrong = Color.FromArgb(203, 213, 225);
    public static readonly Color FocusRing = Color.FromArgb(96, 165, 250);
    public static readonly Color Danger = Color.FromArgb(190, 24, 93);
    public static readonly Color DangerSubtle = Color.FromArgb(253, 242, 248);
    public static readonly Color Success = Color.FromArgb(5, 150, 105);
    public static readonly Color SuccessSubtle = Color.FromArgb(236, 253, 245);
    public static readonly Color Warning = Color.FromArgb(180, 83, 9);
    public static readonly Color WarningSubtle = Color.FromArgb(255, 251, 235);
    public static readonly Color Info = Color.FromArgb(2, 132, 199);
    public static readonly Color InfoSubtle = Color.FromArgb(240, 249, 255);'''

new_palette = '''    // Premium dark runtime palette. The colors deliberately preserve the existing semantic
    // roles so every current screen and runtime state keeps the same behavior and meaning.
    public static readonly Color Background = Color.FromArgb(5, 14, 24);
    public static readonly Color Surface = Color.FromArgb(9, 23, 38);
    public static readonly Color SurfaceAlt = Color.FromArgb(12, 29, 47);
    public static readonly Color SurfaceRaised = Color.FromArgb(7, 20, 34);
    public static readonly Color SurfaceHover = Color.FromArgb(16, 40, 65);
    public static readonly Color SurfacePressed = Color.FromArgb(22, 53, 84);
    public static readonly Color Accent = Color.FromArgb(10, 113, 255);
    public static readonly Color AccentHover = Color.FromArgb(39, 130, 255);
    public static readonly Color AccentPressed = Color.FromArgb(0, 91, 214);
    public static readonly Color AccentSubtle = Color.FromArgb(11, 42, 74);
    public static readonly Color AccentBorder = Color.FromArgb(29, 104, 192);
    public static readonly Color Text = Color.FromArgb(235, 243, 255);
    public static readonly Color Muted = Color.FromArgb(135, 153, 179);
    public static readonly Color MutedStrong = Color.FromArgb(177, 194, 215);
    public static readonly Color DisabledText = Color.FromArgb(89, 108, 132);
    public static readonly Color DisabledSurface = Color.FromArgb(17, 31, 47);
    public static readonly Color Border = Color.FromArgb(28, 48, 70);
    public static readonly Color BorderStrong = Color.FromArgb(42, 67, 96);
    public static readonly Color FocusRing = Color.FromArgb(66, 153, 255);
    public static readonly Color Danger = Color.FromArgb(248, 81, 96);
    public static readonly Color DangerSubtle = Color.FromArgb(63, 25, 34);
    public static readonly Color Success = Color.FromArgb(52, 211, 153);
    public static readonly Color SuccessSubtle = Color.FromArgb(12, 52, 43);
    public static readonly Color Warning = Color.FromArgb(245, 158, 11);
    public static readonly Color WarningSubtle = Color.FromArgb(60, 43, 15);
    public static readonly Color Info = Color.FromArgb(56, 189, 248);
    public static readonly Color InfoSubtle = Color.FromArgb(12, 44, 62);'''

if old_palette in text:
    text = text.replace(old_palette, new_palette)
elif new_palette not in text:
    raise SystemExit('FluentTheme palette block no longer matches expected source.')

text = text.replace('grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(251, 252, 254);',
                    'grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(11, 27, 44);')
text = text.replace('danger ? Color.FromArgb(252, 231, 243) : SurfacePressed',
                    'danger ? Color.FromArgb(82, 31, 42) : SurfacePressed')
text = text.replace('button.FlatAppearance.BorderColor = Color.FromArgb(249, 168, 212);',
                    'button.FlatAppearance.BorderColor = Color.FromArgb(121, 50, 62);')
text = text.replace('toolStrip.Padding = new Padding(4, 3, 4, 3);',
                    'toolStrip.Padding = new Padding(8, 5, 8, 5);')

theme.write_text(text, encoding='utf-8', newline='\n')

main = root / 'src/GPTDeskTop/UI/MainForm.cs'
main_text = main.read_text(encoding='utf-8')
# Keep the established DPI/working-area contract. The premium shell must not raise the
# application's minimum viewport above the regression-tested operator baseline.
main_text = main_text.replace('MinimumSize = new Size(1280, 760);', 'MinimumSize = new Size(980, 680);')
main_text = main_text.replace('Padding = new Padding(16),\n            BackColor = FluentTheme.Background',
                              'Padding = new Padding(10),\n            BackColor = FluentTheme.Background', 1)
main_text = main_text.replace('root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));',
                              'root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));', 1)
main.write_text(main_text, encoding='utf-8', newline='\n')

shell = root / 'src/GPTDeskTop/UI/PremiumRuntimeShellExperience.cs'
shell_text = shell.read_text(encoding='utf-8-sig')
shell_text = shell_text.replace('control.Parent?.ScrollControlIntoView(control);',
                                '(control.Parent as ScrollableControl)?.ScrollControlIntoView(control);')
shell_text = shell_text.replace('FocusControl(grid ?? main);',
                                'FocusControl(grid is null ? main : grid);')
shell.write_text(shell_text, encoding='utf-8', newline='\n')

print('Premium UI patch applied.')
