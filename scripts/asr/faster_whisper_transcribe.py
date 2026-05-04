#!/usr/bin/env python
"""Run faster-whisper and emit KoeNote-compatible JSON segments."""

from __future__ import annotations

import argparse
import json
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


def main() -> int:
    args = parse_args()

    try:
        from faster_whisper import WhisperModel
    except ImportError as exc:
        print(f"Missing faster-whisper package: {exc}", file=sys.stderr)
        return 2

    prompt_parts = []
    if args.context:
        prompt_parts.append(args.context)
    if args.hotword:
        prompt_parts.append(" ".join(args.hotword))
    initial_prompt = "\n".join(prompt_parts) if prompt_parts else None

    model = WhisperModel(args.model, device="auto", compute_type="auto")
    segments, info = model.transcribe(
        args.audio,
        language=args.language,
        initial_prompt=initial_prompt,
        vad_filter=True,
    )

    payload = {
        "engine": "faster-whisper",
        "language": getattr(info, "language", args.language),
        "segments": [
            {
                "id": index,
                "start": float(segment.start),
                "end": float(segment.end),
                "text": segment.text.strip(),
                "speaker": None,
            }
            for index, segment in enumerate(segments)
        ],
    }

    with open(args.output_json, "w", encoding="utf-8") as output:
        json.dump(payload, output, ensure_ascii=False, indent=2)

    print(json.dumps(payload, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
