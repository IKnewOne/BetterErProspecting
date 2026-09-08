#!/usr/bin/env python3
"""
Convert a configlib settings schema (configlib-patches.json) into an IMM config schema (imm.json).

Expected layout:
    <mod root>/
        modinfo.json <- must contain a "modid" field
        assets/<modid>/config/configlib-patches.json

Output is written next to the source file as:

    <mod root>/assets/<modid>/config/imm.json

Usage:
    python configlib_to_imm.py [search_root]

If search_root is omitted, the current working directory is used.
"""

import json
import sys
from pathlib import Path
from typing import Optional


CONFIGLIB_FILENAME = "configlib-patches.json"
IMM_FILENAME = "imm.json"
MODINFO_FILENAME = "modinfo.json"


class ConversionError(Exception):
    """Raised for any unrecoverable problem while locating or converting files."""


# Path resolution.

def find_configlib_file(search_root: Path) -> Path:
    """
    Recursively search search_root for a single
    assets/<modid>/config/configlib-patches.json and return its path.
    """
    assets_dir = search_root / "assets"

    matches = [
        p
        for p in assets_dir.rglob(CONFIGLIB_FILENAME)
        if p.parent.name == "config"
    ]

    if not matches:
        raise ConversionError(
            f"Could not find any 'assets/<modid>/config/{CONFIGLIB_FILENAME}' "
            f"under '{search_root}'."
        )
    if len(matches) > 1:
        raise ConversionError(
            "Found more than one configlib-patches.json under "
            f"'{search_root}':\n  " + "\n  ".join(str(m) for m in matches)
        )

    return matches[0]


def find_modinfo_file(configlib_path: Path) -> Path:
    """
    Given .../assets/<modid>/config/configlib-patches.json, locate the mod
    project's modinfo.json, which is expected to sit directly next to (one
    level above) the 'assets' folder.
    """
    assets_dir = configlib_path.parent.parent.parent  # config -> modid -> assets
    project_root = assets_dir.parent
    modinfo_path = project_root / MODINFO_FILENAME

    if not modinfo_path.is_file():
        raise ConversionError(
            f"Could not find '{MODINFO_FILENAME}' at the expected location: "
            f"'{modinfo_path}'."
        )

    return modinfo_path


def get_modid(modinfo_path: Path) -> str:
    with modinfo_path.open("r", encoding="utf-8") as f:
        data = json.load(f)

    modid = data.get("modid")
    if not modid:
        raise ConversionError(f"'{modinfo_path}' does not contain a 'modid' field.")
    return modid


# ---------------------------------------------------------------------------
# Per-type conversion.
#
# To support a new configlib setting type: write a convert_* function with
# the signature (setting: dict, modid: str) -> dict, and register it in
# TYPE_CONVERTERS below. Anything not registered raises ConversionError.
# ---------------------------------------------------------------------------

def _localize(modid: str, key: Optional[str]) -> Optional[str]:
    """Turn a bare configlib localization key into a 'modid:key' entry."""
    if not key:
        return None
    return f"{modid}:{key}"


def _build(setting: dict, modid: str, imm_type: str) -> dict:
    result = {
        "Type": imm_type,
        "Label": _localize(modid, setting.get("ingui")) or setting["code"],
        "DefaultValueJson": setting.get("default")
    }
    description = _localize(modid, setting.get("comment"))
    if description:
        result["Description"] = description
    result["Map"] = setting["code"]
    return result


def _build_slider(setting: dict, modid: str, rng: dict) -> dict:
    result = _build(setting, modid, "Slider")
    result["Min"] = rng["min"]
    result["Max"] = rng["max"]
    result["Step"] = rng.get("step", 1)
    return result


def convert_boolean(setting: dict, modid: str) -> dict:
    return _build(setting, modid, "Boolean")


def convert_string(setting: dict, modid: str) -> dict:
    return _build(setting, modid, "String")


def convert_integer(setting: dict, modid: str) -> dict:
    rng = setting.get("range")
    return _build_slider(setting, modid, rng) if rng else _build(setting, modid, "Integer")


def convert_float(setting: dict, modid: str) -> dict:
    rng = setting.get("range")
    return _build_slider(setting, modid, rng) if rng else _build(setting, modid, "Decimal")


TYPE_CONVERTERS = {
    "boolean": convert_boolean,
    "string": convert_string,
    "integer": convert_integer,
    "float": convert_float,
}


def convert_setting(setting: dict, modid: str) -> Optional[dict]:
    setting_type = setting.get("type")

    if setting_type == "separator":
        return None

    converter = TYPE_CONVERTERS.get(setting_type)
    if converter is None:
        raise ConversionError(
            f"Unsupported configlib setting type '{setting_type}' "
            f"(code='{setting.get('code')}'). "
            f"Supported types: {', '.join(sorted(TYPE_CONVERTERS))}."
        )

    return converter(setting, modid)


# ---------------------------------------------------------------------------
# Top-level conversion
# ---------------------------------------------------------------------------
def convert_configlib_to_imm(configlib_data: dict, modid: str, config_file_value: str) -> dict:
    settings = [
        imm_setting
        for raw_setting in configlib_data.get("settings", [])
        if (imm_setting := convert_setting(raw_setting, modid)) is not None
    ]

    configuration_block = {
        "ConfigFile": config_file_value,
        # No equivalent in configlib
        "ConfigLabel": modid,
        "ConfigSource": "ModConfig",
        "ConfigSide": "Server",
        "Settings": settings,
    }

    return {"Configuration": [configuration_block]}


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------

def main() -> int:
    search_root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path.cwd()

    try:
        configlib_path = find_configlib_file(search_root)
        modinfo_path = find_modinfo_file(configlib_path)
        modid = get_modid(modinfo_path)

        with configlib_path.open("r", encoding="utf-8") as f:
            configlib_data = json.load(f)

        configlib_file_value = configlib_data.get("file")
        if not configlib_file_value:
            raise ConversionError(f"'{configlib_path}' is missing a top-level 'file' field.")

        config_file_value = configlib_data.get("file")
        imm_data = convert_configlib_to_imm(configlib_data, modid, config_file_value)

        output_path = configlib_path.parent / IMM_FILENAME
        with output_path.open("w", encoding="utf-8") as f:
            json.dump(imm_data, f, indent=4)
            f.write("\n")

        print(f"Wrote '{output_path}' (modid='{modid}').")
        return 0

    except ConversionError as e:
        print(f"Error: {e}", file=sys.stderr)
        return 1
    except json.JSONDecodeError as e:
        print(f"Error: failed to parse JSON — {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
