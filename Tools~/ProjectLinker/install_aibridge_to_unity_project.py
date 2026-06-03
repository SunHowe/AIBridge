# -*- coding: utf-8 -*-
import argparse
import json
import os
import sys
from dataclasses import dataclass
from pathlib import Path


PACKAGE_NAME = "cn.lys.aibridge"
DEPENDENCY_VALUE = "file:" + PACKAGE_NAME


class InstallError(Exception):
    pass


@dataclass(frozen=True)
class InstallResult:
    project_root: Path
    package_root: Path
    link_path: Path
    manifest_path: Path
    link_created: bool
    manifest_changed: bool


@dataclass(frozen=True)
class InstallFailure:
    project_root: Path
    error: str


@dataclass(frozen=True)
class InstallSummary:
    results: list
    failures: list


def install(project_root, package_root, link_creator=None):
    project_root = Path(project_root).expanduser().resolve()
    package_root = Path(package_root).expanduser().resolve()
    link_creator = link_creator or create_directory_link

    validate_unity_project(project_root)
    validate_package_root(package_root)

    packages_root = project_root / "Packages"
    packages_root.mkdir(exist_ok=True)
    manifest_path = packages_root / "manifest.json"
    manifest = load_manifest(manifest_path)

    link_path = packages_root / PACKAGE_NAME
    link_created = ensure_package_link(link_path, package_root, link_creator)
    manifest_changed = ensure_manifest_dependency(manifest_path, manifest)

    return InstallResult(
        project_root=project_root,
        package_root=package_root,
        link_path=link_path,
        manifest_path=manifest_path,
        link_created=link_created,
        manifest_changed=manifest_changed,
    )


def install_many(project_roots, package_root, link_creator=None):
    results = []
    failures = []
    for project_root in project_roots:
        try:
            results.append(install(project_root, package_root, link_creator=link_creator))
        except InstallError as exc:
            failures.append(InstallFailure(Path(project_root), str(exc)))

    return InstallSummary(results=results, failures=failures)


def validate_unity_project(project_root):
    if not project_root.is_dir():
        raise InstallError("Target is not a directory: " + str(project_root))

    # Unity 项目至少应包含 Assets、ProjectSettings 和 ProjectVersion.txt。
    required_paths = [
        project_root / "Assets",
        project_root / "ProjectSettings",
        project_root / "ProjectSettings" / "ProjectVersion.txt",
    ]
    missing = [str(path.relative_to(project_root)) for path in required_paths if not path.exists()]
    if missing:
        raise InstallError(
            "Target does not look like a Unity project. Missing: " + ", ".join(missing)
        )


def validate_package_root(package_root):
    package_json_path = package_root / "package.json"
    if not package_json_path.is_file():
        raise InstallError("AIBridge package.json not found: " + str(package_json_path))

    try:
        package_json = json.loads(package_json_path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as exc:
        raise InstallError("Invalid AIBridge package.json: " + str(exc)) from exc

    actual_name = package_json.get("name")
    if actual_name != PACKAGE_NAME:
        raise InstallError(
            "Unexpected package name in package.json. Expected "
            + PACKAGE_NAME
            + ", got "
            + repr(actual_name)
        )


def load_manifest(manifest_path):
    if not manifest_path.exists():
        return {"dependencies": {}}

    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
    except json.JSONDecodeError as exc:
        raise InstallError("Invalid Unity manifest.json: " + str(exc)) from exc

    if not isinstance(manifest, dict):
        raise InstallError("Unity manifest.json root must be a JSON object.")

    dependencies = manifest.get("dependencies")
    if dependencies is None:
        manifest["dependencies"] = {}
    elif not isinstance(dependencies, dict):
        raise InstallError("Unity manifest.json dependencies must be a JSON object.")

    return manifest


def ensure_package_link(link_path, package_root, link_creator):
    if link_path.exists() or link_path.is_symlink():
        if points_to(link_path, package_root):
            return False

        raise InstallError(
            "Package path already exists and does not point to AIBridge: " + str(link_path)
        )

    link_creator(link_path, package_root)
    return True


def points_to(path, target):
    try:
        return path.resolve(strict=True) == target.resolve(strict=True)
    except OSError:
        return False


def create_directory_link(link_path, target_path):
    try:
        os.symlink(str(target_path), str(link_path), target_is_directory=True)
    except OSError as exc:
        message = (
            "Failed to create directory symlink: "
            + str(link_path)
            + " -> "
            + str(target_path)
            + "\nOn Windows, run this batch file as Administrator or enable Developer Mode."
        )
        raise InstallError(message) from exc


def ensure_manifest_dependency(manifest_path, manifest):
    dependencies = manifest["dependencies"]
    if dependencies.get(PACKAGE_NAME) == DEPENDENCY_VALUE:
        return False

    dependencies[PACKAGE_NAME] = DEPENDENCY_VALUE
    manifest_path.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    return True


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Link AIBridge into a Unity project and register it in Packages/manifest.json."
    )
    parser.add_argument(
        "project_roots",
        nargs="+",
        help="Unity project folder or folders dragged onto the batch file.",
    )
    parser.add_argument(
        "--package-root",
        default=str(Path(__file__).resolve().parents[2]),
        help="AIBridge package root. Defaults to the repository containing this script.",
    )
    return parser.parse_args(argv)


def main(argv=None):
    args = parse_args(argv if argv is not None else sys.argv[1:])
    summary = install_many(args.project_roots, args.package_root)

    for result in summary.results:
        print("[AIBridge] Unity project : " + str(result.project_root))
        print("[AIBridge] Package root  : " + str(result.package_root))
        print("[AIBridge] Link path     : " + str(result.link_path))
        print("[AIBridge] Manifest      : " + str(result.manifest_path))
        print("[AIBridge] Link          : " + ("created" if result.link_created else "already correct"))
        print("[AIBridge] Manifest      : " + ("updated" if result.manifest_changed else "already registered"))

    for failure in summary.failures:
        print(
            "[AIBridge] Install failed for "
            + str(failure.project_root)
            + ": "
            + failure.error,
            file=sys.stderr,
        )

    print(
        "[AIBridge] Summary       : "
        + str(len(summary.results))
        + " succeeded, "
        + str(len(summary.failures))
        + " failed"
    )
    return 1 if summary.failures else 0


if __name__ == "__main__":
    sys.exit(main())
