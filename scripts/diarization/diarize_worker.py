from __future__ import annotations

import argparse
import importlib
import json
from pathlib import Path
from typing import Any, Iterable


def main() -> int:
    parser = argparse.ArgumentParser(description="Run lightweight CPU speaker diarization with diarize.")
    parser.add_argument("--audio", required=True)
    parser.add_argument("--output-json", required=True)
    args = parser.parse_args()

    output_path = Path(args.output_json)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    try:
        module = importlib.import_module("diarize")
    except Exception as exc:
        write_output(output_path, f"skipped: diarize package not available ({exc})", [])
        return 0

    try:
        raw_turns = run_diarize(module, args.audio)
        turns = normalize_turns(raw_turns)
        if not turns:
            write_output(output_path, "skipped: diarize produced no speaker turns", [])
            return 0

        write_output(output_path, "succeeded", remap_speakers(turns))
        return 0
    except Exception as exc:
        write_output(output_path, f"failed: {type(exc).__name__}: {exc}", [])
        return 0


def run_diarize(module: Any, audio_path: str) -> Any:
    if hasattr(module, "Diarizer"):
        diarizer = module.Diarizer()
        for method_name in ("diarize", "run", "predict"):
            method = getattr(diarizer, method_name, None)
            if callable(method):
                return method(audio_path)

    for function_name in ("diarize", "run", "predict"):
        function = getattr(module, function_name, None)
        if callable(function):
            return function(audio_path)

    raise RuntimeError("unsupported diarize API")


def normalize_turns(raw: Any) -> list[dict[str, Any]]:
    if raw is None:
        return []

    if isinstance(raw, dict):
        for key in ("turns", "segments", "diarization"):
            if key in raw:
                return normalize_turns(raw[key])
        if has_turn_fields(raw):
            return [normalize_turn(raw)]

    if hasattr(raw, "to_list") and callable(raw.to_list):
        return normalize_turns(raw.to_list())

    if hasattr(raw, "segments"):
        return normalize_turns(raw.segments)

    if isinstance(raw, Iterable) and not isinstance(raw, (str, bytes)):
        turns: list[dict[str, Any]] = []
        for item in raw:
            turn = normalize_turn(item)
            if turn is not None:
                turns.append(turn)
        return turns

    return []


def has_turn_fields(value: dict[str, Any]) -> bool:
    return any(key in value for key in ("start", "start_seconds", "start_time")) and any(
        key in value for key in ("end", "end_seconds", "end_time")
    )


def normalize_turn(value: Any) -> dict[str, Any] | None:
    start = read_value(value, "start", "start_seconds", "start_time")
    end = read_value(value, "end", "end_seconds", "end_time")
    speaker = read_value(value, "speaker", "speaker_id", "label")

    try:
        start_seconds = float(start)
        end_seconds = float(end)
    except (TypeError, ValueError):
        return None

    if end_seconds <= start_seconds:
        return None

    return {
        "start": start_seconds,
        "end": end_seconds,
        "speaker": str(speaker or "Speaker_0"),
    }


def read_value(value: Any, *names: str) -> Any:
    if isinstance(value, dict):
        for name in names:
            if name in value:
                return value[name]

    for name in names:
        if hasattr(value, name):
            return getattr(value, name)

    return None


def remap_speakers(turns: list[dict[str, Any]]) -> list[dict[str, Any]]:
    speaker_map: dict[str, str] = {}
    normalized: list[dict[str, Any]] = []
    for turn in turns:
        raw_speaker = str(turn["speaker"])
        if raw_speaker not in speaker_map:
            speaker_map[raw_speaker] = f"Speaker_{len(speaker_map)}"
        normalized.append(
            {
                "start": turn["start"],
                "end": turn["end"],
                "speaker": speaker_map[raw_speaker],
            }
        )
    return normalized


def write_output(path: Path, status: str, turns: list[dict[str, Any]]) -> None:
    path.write_text(
        json.dumps(
            {
                "engine": "diarize",
                "status": status,
                "turns": turns,
            },
            ensure_ascii=False,
            indent=2,
        ),
        encoding="utf-8",
    )


if __name__ == "__main__":
    raise SystemExit(main())
