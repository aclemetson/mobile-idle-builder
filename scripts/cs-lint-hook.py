import sys
import json
import re

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

file_path = data.get('tool_input', {}).get('file_path', '')
if not file_path.endswith('.cs'):
    sys.exit(0)

try:
    with open(file_path, encoding='utf-8') as f:
        content = f.read()
except Exception:
    sys.exit(0)

issues = []

# Missing base.Awake() in an Awake override
if re.search(r'override\s+void\s+Awake\s*\(', content) and 'base.Awake()' not in content:
    issues.append('Awake() override is missing base.Awake() — add it or this class will break singleton/base init chains')

# Debug.Log on a non-comment line
for line in content.splitlines():
    stripped = line.strip()
    if 'Debug.Log' in stripped and not stripped.startswith('//'):
        issues.append('Debug.Log found — use GameLogger instead (configurable log levels, pre-commit audit)')
        break

# MenuItem with named parameter (Unity does not support named params)
if re.search(r'\[MenuItem\([^)]*validate\s*:', content):
    issues.append('MenuItem uses named parameter (validate:) — Unity does not support named params; use positional args')

if issues:
    print('[cs-lint] Issues in ' + file_path)
    for issue in issues:
        print('  ! ' + issue)
