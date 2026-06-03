import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "install_aibridge_to_unity_project.py"
PACKAGE_NAME = "cn.lys.aibridge"


def load_installer():
    if not SCRIPT_PATH.exists():
        return None

    spec = importlib.util.spec_from_file_location("install_aibridge_to_unity_project", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class AIBridgeUnityProjectInstallerTests(unittest.TestCase):
    def setUp(self):
        self.temp_root = Path(tempfile.mkdtemp(prefix="aibridge_linker_test_"))
        self.package_root = self.temp_root / "AIBridge"
        self.package_root.mkdir()
        (self.package_root / "package.json").write_text(
            json.dumps({"name": PACKAGE_NAME}, indent=2),
            encoding="utf-8",
        )

    def tearDown(self):
        shutil.rmtree(self.temp_root, ignore_errors=True)

    def create_unity_project(self, manifest=None, name="UnityProject"):
        project_root = self.temp_root / name
        (project_root / "Assets").mkdir(parents=True)
        (project_root / "ProjectSettings").mkdir()
        (project_root / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 2022.3.52f1\n",
            encoding="utf-8",
        )

        packages_root = project_root / "Packages"
        packages_root.mkdir()
        if manifest is not None:
            (packages_root / "manifest.json").write_text(
                json.dumps(manifest, indent=2),
                encoding="utf-8",
            )
        return project_root

    def test_rejects_non_unity_project(self):
        installer = load_installer()
        self.assertIsNotNone(installer, "installer script should exist")

        non_project = self.temp_root / "NotUnity"
        non_project.mkdir()

        with self.assertRaises(installer.InstallError):
            installer.install(non_project, self.package_root, link_creator=lambda _link, _target: None)

    def test_creates_package_link_and_registers_manifest_dependency(self):
        installer = load_installer()
        self.assertIsNotNone(installer, "installer script should exist")

        project_root = self.create_unity_project(
            {"dependencies": {"com.unity.ugui": "1.0.0"}}
        )
        created_links = []

        def fake_link_creator(link_path, target_path):
            created_links.append((link_path, target_path))
            link_path.mkdir()

        result = installer.install(project_root, self.package_root, link_creator=fake_link_creator)

        expected_link = project_root / "Packages" / PACKAGE_NAME
        self.assertEqual(1, len(created_links))
        self.assertEqual(expected_link.resolve(), created_links[0][0].resolve())
        self.assertEqual(self.package_root.resolve(), created_links[0][1].resolve())
        self.assertTrue(result.link_created)
        self.assertTrue(result.manifest_changed)

        manifest = json.loads((project_root / "Packages" / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual("1.0.0", manifest["dependencies"]["com.unity.ugui"])
        self.assertEqual(f"file:{PACKAGE_NAME}", manifest["dependencies"][PACKAGE_NAME])

    def test_updates_existing_dependency_value_without_losing_other_manifest_fields(self):
        installer = load_installer()
        self.assertIsNotNone(installer, "installer script should exist")

        project_root = self.create_unity_project(
            {
                "dependencies": {PACKAGE_NAME: "https://example.invalid/aibridge.git"},
                "scopedRegistries": [{"name": "Example", "url": "https://example.invalid", "scopes": ["com.example"]}],
            }
        )

        installer.install(
            project_root,
            self.package_root,
            link_creator=lambda link_path, _target_path: link_path.mkdir(),
        )

        manifest = json.loads((project_root / "Packages" / "manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(f"file:{PACKAGE_NAME}", manifest["dependencies"][PACKAGE_NAME])
        self.assertEqual("Example", manifest["scopedRegistries"][0]["name"])

    def test_install_many_registers_every_dragged_unity_project(self):
        installer = load_installer()
        self.assertIsNotNone(installer, "installer script should exist")

        first_project = self.create_unity_project({"dependencies": {}}, name="FirstUnityProject")
        second_project = self.create_unity_project({"dependencies": {}}, name="SecondUnityProject")
        created_links = []

        def fake_link_creator(link_path, target_path):
            created_links.append((link_path, target_path))
            link_path.mkdir()

        summary = installer.install_many(
            [first_project, second_project],
            self.package_root,
            link_creator=fake_link_creator,
        )

        self.assertEqual(2, len(summary.results))
        self.assertEqual([], summary.failures)
        self.assertEqual(2, len(created_links))
        for project_root in [first_project, second_project]:
            manifest = json.loads((project_root / "Packages" / "manifest.json").read_text(encoding="utf-8"))
            self.assertEqual(f"file:{PACKAGE_NAME}", manifest["dependencies"][PACKAGE_NAME])


if __name__ == "__main__":
    unittest.main()
