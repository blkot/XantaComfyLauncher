import ast
import json
import os
import sys


def string_keys(node):
    if not isinstance(node, ast.Dict):
        return set()
    keys = set()
    for key in node.keys:
        if isinstance(key, ast.Constant) and isinstance(key.value, str):
            keys.add(key.value)
    return keys


def scan_file(path):
    try:
        with open(path, "r", encoding="utf-8", errors="ignore") as handle:
            tree = ast.parse(handle.read(), filename=path)
    except Exception:
        return set()

    found = set()
    for node in ast.walk(tree):
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            for target in targets:
                name = ""
                if isinstance(target, ast.Name):
                    name = target.id
                elif isinstance(target, ast.Attribute):
                    name = target.attr
                if "MAPPING" in name.upper():
                    found.update(string_keys(node.value))
        elif isinstance(node, ast.Call):
            func = node.func
            name = ""
            if isinstance(func, ast.Attribute):
                name = func.attr
            elif isinstance(func, ast.Name):
                name = func.id
            if "MAPPING" in name.upper() or (isinstance(func, ast.Attribute) and
                                              isinstance(func.value, ast.Name) and
                                              "MAPPING" in func.value.id.upper()):
                for arg in node.args:
                    found.update(string_keys(arg))
    return found


def scan_package(package_dir):
    result = set()
    for root, dirs, files in os.walk(package_dir):
        dirs[:] = [d for d in dirs if d not in ("__pycache__", ".git", "node_modules")]
        for file_name in files:
            if not file_name.endswith(".py"):
                continue
            path = os.path.join(root, file_name)
            try:
                if os.path.getsize(path) > 3 * 1024 * 1024:
                    continue
            except OSError:
                continue
            result.update(scan_file(path))
    return sorted(result)


def main():
    roots = sys.argv[1:]
    output = {}
    for root in roots:
        root_map = {}
        try:
            names = sorted(os.listdir(root))
        except OSError:
            continue
        for name in names:
            package_dir = os.path.join(root, name)
            if not os.path.isdir(package_dir):
                continue
            if name in ("__pycache__",) or name.endswith(".disabled"):
                continue
            root_map[name] = scan_package(package_dir)
        output[root] = root_map
    print(json.dumps(output, ensure_ascii=False))


if __name__ == "__main__":
    main()
