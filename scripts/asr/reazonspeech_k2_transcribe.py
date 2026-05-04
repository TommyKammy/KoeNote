#!/usr/bin/env python
"""Run ReazonSpeech k2 and emit KoeNote-compatible JSON segments."""

from __future__ import annotations

import argparse
import json
import os
import sys


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--language", default="ja")
    parser.add_argument("--context", default=None)
    parser.add_argument("--hotword", action="append", default=[])
    return parser.parse_args()


def to_float(value: object, default: float) -> float:
    try:
        return float(value)
    except (TypeError, ValueError):
        return default


def read_attr_or_key(value: object, name: str, default: object = None) -> object:
    if isinstance(value, dict):
        return value.get(name, default)
    return getattr(value, name, default)


def main() -> int:
    args = parse_args()

    try:
        from reazonspeech.k2.asr import audio_from_path, load_model, transcribe
    except ImportError as exc:
        print(f"Missing ReazonSpeech k2 package: {exc}", file=sys.stderr)
        return 2

    device = os.environ.get("REAZONSPEECH_DEVICE", "cpu")
    model = load_model(device=device, language=args.language)
    audio = audio_from_path(args.audio)
    result = transcribe(model, audio)
    raw_segments = read_attr_or_key(result, "segments", []) or []

    segments = []
    cursor = 0.0
    for index, segment in enumerate(raw_segments):
        start = to_float(read_attr_or_key(segment, "start", None), cursor)
        end = to_float(read_attr_or_key(segment, "end", None), start)
        text = str(read_attr_or_key(segment, "text", "")).strip()
        cursor = max(cursor, end)
        segments.append(
            {
                "id": index,
                "start": start,
                "end": end,
                "text": text,
                "speaker": None,
            }
        )

    if not segments:
        text = str(read_attr_or_key(result, "text", "")).strip()
        segments.append({"id": 0, "start": 0.0, "end": 0.0, "text": text, "speaker": None})

    payload = {
        "engine": "reazonspeech-k2",
        "language": args.language,
        "segments": segments,
    }

    with open(args.output_json, "w", encoding="utf-8") as output:
        json.dump(payload, output, ensure_ascii=False, indent=2)

    print(json.dumps(payload, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
