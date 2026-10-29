"""Apply narrowly-scoped compatibility fixes inside the volume-v1 See-through image.

* LayerDiff & Marigold UNets: request published safetensors explicitly.
* The official Marigold model moved to layerdifforg; fix its CLI default only
  if it still points at the old 24yearsold path.

Fails safely when upstream structure differs. Doesn't change the RunPod handler,
network-volume cache settings, app, backend or existing volume-v1 image.
"""
from __future__ import annotations

import ast
from pathlib import Path

UTILS = Path('/opt/see-through/common/utils/inference_utils.py')
CLI = Path('/opt/see-through/inference/scripts/inference_psd.py')
OLD_DEPTH_REPO = '24yearsold/seethroughv0.0.1_marigold'
NEW_DEPTH_REPO = 'layerdifforg/seethroughv0.0.1_marigold'


def offset_for(source: str, node: ast.AST, end: bool = False) -> int:
    lines = source.splitlines(keepends=True)
    line = node.end_lineno if end else node.lineno
    column = node.end_col_offset if end else node.col_offset
    if line is None or column is None:
        raise RuntimeError('AST location missing')
    return sum(len(s.encode('utf-8')) for s in lines[:line - 1]) + column


def apply_replacements(source: str, edits: list[tuple[int, int, str]]) -> str:
    encoded = source.encode('utf-8')
    positions = sorted(edits, reverse=True)
    for (start, stop, replacement) in positions:
        encoded = encoded[:start] + replacement.encode('utf-8') + encoded[stop:]
    modified = encoded.decode('utf-8')
    ast.parse(modified)
    return modified


def patch_utils(path: Path) -> str:
    original = path.read_text(encoding='utf-8')
    tree = ast.parse(original)
    funcs = {node.name: node for node in tree.body if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))}
    required = ('apply_layerdiff', 'apply_marigold')
    edits: list[tuple[int, int, str]] = []

    for name in required:
        func = funcs.get(name)
        if func is None:
            raise RuntimeError(f'Required function {name} not found; not patching')
        matches = []
        for node in ast.walk(func):
            if not isinstance(node, ast.Call):
                continue
            if not isinstance(node.func, ast.Attribute) or node.func.attr != 'from_pretrained':
                continue
            if not isinstance(node.func.value, ast.Name) or node.func.value.id != 'UNetFrameConditionModel':
                continue
            if len(node.args) != 1 or not isinstance(node.args[0], ast.Name) or node.args[0].id != 'pretrained':
                continue
            if not any(kw.arg == 'subfolder' and isinstance(kw.value, ast.Constant) and kw.value.value == 'unet' for kw in node.keywords):
                continue
            matches.append(node)
        if len(matches) != 1:
            raise RuntimeError(f'Expected exactly one {name} pretrained UNet call; found {len(matches)}. Not patching.')
        call = matches[0]
        safetensor_kws = [kw for kw in call.keywords if kw.arg == 'use_safetensors']
        if safetensor_kws:
            if len(safetensor_kws) == 1 and isinstance(safetensor_kws[0].value, ast.Constant) and safetensor_kws[0].value.value is True:
                print(f'Already patched {name}')
                continue
            raise RuntimeError(f'{name} already has a non-True use_safetensors argument; not overriding')
        call.keywords.append(ast.keyword(arg='use_safetensors', value=ast.Constant(value=True)))
        replacement = ast.unparse(call)
        edits.append((offset_for(original, call), offset_for(original, call, end=True), replacement))
        print(f'Will patch {name} model loader')

    return apply_replacements(original, edits)


def patch_cli(path: Path) -> str:
    original = path.read_text(encoding='utf-8')
    tree = ast.parse(original)
    matches = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.Call) or not isinstance(node.func, ast.Attribute) or node.func.attr != 'add_argument':
            continue
        if not node.args or not isinstance(node.args[0], ast.Constant) or node.args[0].value != '--repo_id_depth':
            continue
        matches.append(node)
    if len(matches) != 1:
        raise RuntimeError(f'Expected one --repo_id_depth argument, found {len(matches)}. Not patching.')
    defaults = [kw for kw in matches[0].keywords if kw.arg == 'default']
    if len(defaults) != 1 or not isinstance(defaults[0].value, ast.Constant) or not isinstance(defaults[0].value.value, str):
        raise RuntimeError('Depth repository default is not a string; not patching.')
    default = defaults[0].value
    if default.value == NEW_DEPTH_REPO:
        print('Marigold repo already current')
        return original
    if default.value != OLD_DEPTH_REPO:
        raise RuntimeError(f'Unexpected depth model repository: {default.value!r}. Not patching.')
    print('Will update Marigold model repository default')
    return apply_replacements(original, [(offset_for(original, default), offset_for(original, default, end=True), repr(NEW_DEPTH_REPO))])


def main() -> None:
    for path in (UTILS, CLI):
        if not path.is_file():
            raise RuntimeError(f'Missing official See-through file: {path}')
    # Validate both changes BEFORE writing either file.
    modified_utils = patch_utils(UTILS)
    modified_cli = patch_cli(CLI)
    original_utils = UTILS.read_text(encoding='utf-8')
    original_cli = CLI.read_text(encoding='utf-8')
    if modified_utils != original_utils:
        UTILS.write_text(modified_utils, encoding='utf-8')
    if modified_cli != original_cli:
        CLI.write_text(modified_cli, encoding='utf-8')
    print('SUCCESS: See-through LayerDiff/Marigold compatibility patch verified and applied')


if __name__ == '__main__':
    main()
