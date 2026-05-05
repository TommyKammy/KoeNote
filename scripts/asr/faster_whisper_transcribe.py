#!/usr/bin/env python
"""Run faster-whisper and emit KoeNote-compatible JSON segments."""

from __future__ import annotations

import argparse
import gc
import json
import os
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except AttributeError:
    pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--language", default="ja")
    parser.add_argument("--context", default=None)
    parser.add_argument("--hotword", action="append", default=[])
    parser.add_argument("--chunk-length", type=int, default=None)
    parser.add_argument("--diarization", choices=["auto", "off", "pyannote"], default="auto")
    parser.add_argument("--diarization-model", default="pyannote/speaker-diarization-3.1")
    parser.add_argument("--hf-token", default=None)
    return parser.parse_args()


def release_gpu_memory() -> None:
    gc.collect()
    try:
        import torch

        if torch.cuda.is_available():
            torch.cuda.empty_cache()
    except Exception:
        pass


def run_pyannote_diarization(audio_path: str, model_name: str, token: str) -> list[dict[str, object]]:
    from pyannote.audio import Pipeline

    pipeline = Pipeline.from_pretrained(model_name, use_auth_token=token)
    try:
        import torch

        if torch.cuda.is_available():
            pipeline.to(torch.device("cuda"))
    except Exception:
        pass

    diarization = pipeline(audio_path)
    turns: list[dict[str, object]] = []
    speaker_map: dict[str, str] = {}
    for turn, _, raw_speaker in diarization.itertracks(yield_label=True):
        speaker = speaker_map.setdefault(str(raw_speaker), f"Speaker_{len(speaker_map)}")
        turns.append({
            "start": float(turn.start),
            "end": float(turn.end),
            "speaker": speaker,
        })

    return sorted(turns, key=lambda item: (float(item["start"]), float(item["end"])))


def overlap_seconds(start_a: float, end_a: float, start_b: float, end_b: float) -> float:
    return max(0.0, min(end_a, end_b) - max(start_a, start_b))


def assign_speaker(start: float, end: float, diarization_turns: list[dict[str, object]]) -> str | None:
    if not diarization_turns:
        return None

    scores: dict[str, float] = {}
    for turn in diarization_turns:
        speaker = str(turn["speaker"])
        overlap = overlap_seconds(start, end, float(turn["start"]), float(turn["end"]))
        if overlap > 0:
            scores[speaker] = scores.get(speaker, 0.0) + overlap

    if scores:
        return max(scores.items(), key=lambda item: item[1])[0]

    midpoint = start + ((end - start) / 2)
    for turn in diarization_turns:
        if float(turn["start"]) <= midpoint <= float(turn["end"]):
            return str(turn["speaker"])

    return None


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
    transcribe_options = {
        "language": args.language,
        "initial_prompt": initial_prompt,
        "vad_filter": True,
    }
    if args.chunk_length is not None and args.chunk_length > 0:
        transcribe_options["chunk_length"] = args.chunk_length

    segments, info = model.transcribe(args.audio, **transcribe_options)

    segment_items = [
        {
            "id": index,
            "start": float(segment.start),
            "end": float(segment.end),
            "text": segment.text.strip(),
            "speaker": None,
        }
        for index, segment in enumerate(segments)
    ]
    detected_language = getattr(info, "language", args.language)

    del segments
    del info
    del model
    release_gpu_memory()

    diarization_turns: list[dict[str, object]] = []
    diarization_status = "off" if args.diarization == "off" else "skipped"
    if args.diarization != "off":
        token = args.hf_token or os.environ.get("HF_TOKEN") or os.environ.get("HUGGINGFACE_TOKEN")
        if not token:
            diarization_status = "skipped: missing_hf_token"
            if args.diarization == "pyannote":
                print("Diarization skipped: missing HF_TOKEN/HUGGINGFACE_TOKEN.", file=sys.stderr)
        else:
            try:
                diarization_turns = run_pyannote_diarization(args.audio, args.diarization_model, token)
            except ImportError as exc:
                diarization_status = "skipped: missing_pyannote"
                print(f"Diarization skipped: missing pyannote.audio package: {exc}", file=sys.stderr)
            except Exception as exc:
                diarization_status = f"failed: {exc.__class__.__name__}"
                print(f"Diarization skipped: {exc}", file=sys.stderr)
            else:
                diarization_status = "pyannote" if diarization_turns else "skipped: no_speech_turns"

    if diarization_turns:
        for item in segment_items:
            item["speaker"] = assign_speaker(float(item["start"]), float(item["end"]), diarization_turns)

    payload = {
        "engine": "faster-whisper",
        "language": detected_language,
        "diarization": {
            "engine": "pyannote.audio" if diarization_turns else None,
            "status": diarization_status,
            "turns": diarization_turns,
        },
        "segments": segment_items,
    }

    with open(args.output_json, "w", encoding="utf-8") as output:
        json.dump(payload, output, ensure_ascii=False, indent=2)

    print(json.dumps({
        "output_json": args.output_json,
        "segments": len(payload["segments"]),
        "diarization": diarization_status,
    }, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
