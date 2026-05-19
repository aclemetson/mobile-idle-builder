import sys
import json
import os

try:
    data = json.load(sys.stdin)
except Exception:
    sys.exit(0)

file_path = data.get('tool_input', {}).get('file_path', '')
if not file_path:
    sys.exit(0)

PROJECT_ROOT = os.path.normcase(r'E:\Programming\unity\mobile-idle-builder')
abs_path = os.path.normcase(os.path.abspath(file_path))

if not abs_path.startswith(PROJECT_ROOT):
    print(
        '[worktree-guard] WARNING: editing file outside the main Unity project root.\n'
        '  File : ' + abs_path + '\n'
        '  Root : ' + PROJECT_ROOT + '\n'
        '  Unity reads from the project root — changes here will NOT be picked up by the Editor.\n'
        '  Confirm this is intentional before proceeding.'
    )
