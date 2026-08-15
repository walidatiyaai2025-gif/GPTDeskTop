from pathlib import Path

path = Path('src/GPTDeskTop/Services/ChromeDevToolsService.cs')
text = path.read_text(encoding='utf-8')
old = '''      snapshot: null
    };'''
new = '''      snapshot: null,
      markDirty: null
    };'''
if text.count(old) != 1:
    raise SystemExit(f'controller field marker count={text.count(old)}')
text = text.replace(old, new, 1)
old = '''      controller.mode = mode;
      controller.event = event;
      controller.sequence++;
    };'''
new = '''      controller.mode = mode;
      controller.event = event;
      controller.sequence++;
      controller.markDirty?.();
    };'''
if text.count(old) != 1:
    raise SystemExit(f'emit marker count={text.count(old)}')
text = text.replace(old, new, 1)
old = '''  state.autoFollow = createSmartFollowController();
  state.read = () => {'''
new = '''  state.autoFollow = createSmartFollowController();
  state.autoFollow.markDirty = () => { state.dirty = true; };
  state.read = () => {'''
if text.count(old) != 1:
    raise SystemExit(f'state autoFollow marker count={text.count(old)}')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')

test_path = Path('tests/GPTDeskTop.RuntimeTests/SmartChatAutoFollowTests.cs')
test = test_path.read_text(encoding='utf-8')
needle = '''        Assert.Contains("document.addEventListener('keydown'", source, StringComparison.Ordinal);
'''
replacement = needle + '''        Assert.Contains("controller.markDirty?.();", source, StringComparison.Ordinal);
        Assert.Contains("state.autoFollow.markDirty = () => { state.dirty = true; };", source, StringComparison.Ordinal);
'''
if test.count(needle) != 1:
    raise SystemExit(f'test marker count={test.count(needle)}')
test_path.write_text(test.replace(needle, replacement, 1), encoding='utf-8')
print('Auto-follow state propagation fixed.')
