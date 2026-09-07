from pathlib import Path

path = Path('tools/v2035_single_chrome_patch.py')
text = path.read_text(encoding='utf-8')

old_writer = 'using (var writer = new StreamWriter(stream, leaveOpen: true))'
new_writer = 'using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8, 128, leaveOpen: true))'
if text.count(old_writer) != 1:
    raise SystemExit(f'StreamWriter correction expected 1 match, found {text.count(old_writer)}')
text = text.replace(old_writer, new_writer, 1)

old_lookup = '''        Registration registration;\n        lock (RegistrationSync)\n        {\n            if (!Registrations.TryGetValue(selectedChrome, out registration!))\n                throw new InvalidOperationException("Monitor Only Chrome ownership is not registered. Physical send is blocked.");\n        }'''
new_lookup = '''        Registration registration;\n        lock (RegistrationSync)\n        {\n            if (!Registrations.TryGetValue(selectedChrome, out var found))\n                throw new InvalidOperationException("Monitor Only Chrome ownership is not registered. Physical send is blocked.");\n            registration = found;\n        }'''
if text.count(old_lookup) != 1:
    raise SystemExit(f'Registration correction expected 1 match, found {text.count(old_lookup)}')
text = text.replace(old_lookup, new_lookup, 1)

path.write_text(text, encoding='utf-8')
print('single-Chrome generator corrections applied')
