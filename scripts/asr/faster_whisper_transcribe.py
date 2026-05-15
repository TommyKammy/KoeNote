#!/usr/bin/env python
"""Run faster-whisper and emit KoeNote-compatible JSON segments."""

from __future__ import annotations

import argparse
import gc
import importlib.metadata as metadata
import json
import os
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except AttributeError:
    pass


def add_windows_dll_directories() -> None:
    if os.name != "nt" or not hasattr(os, "add_dll_directory"):
        return

    candidates = []
    configured = os.environ.get("KOENOTE_ASR_TOOLS_DIR")
    if configured:
        candidates.append(configured)

    script_directory = os.path.dirname(os.path.abspath(__file__))
    candidates.append(os.path.abspath(os.path.join(script_directory, "..", "..", "tools", "asr")))

    for candidate in candidates:
        if not os.path.isdir(candidate):
            continue

        try:
            os.add_dll_directory(candidate)
        except OSError:
            continue


def write_diagnostic(event: str, **values: object) -> None:
    payload = {"event": event, **values}
    print("koenote_asr_diagnostic " + json.dumps(payload, ensure_ascii=True, sort_keys=True), file=sys.stderr, flush=True)


def package_version(package_name: str) -> str | None:
    try:
        return metadata.version(package_name)
    except metadata.PackageNotFoundError:
        return None


def collect_asr_tools_files() -> list[str]:
    tools_dir = os.environ.get("KOENOTE_ASR_TOOLS_DIR")
    if not tools_dir:
        return []

    root = Path(tools_dir)
    if not root.is_dir():
        return []

    names = [
        "crispasr.exe",
        "crispasr.dll",
        "whisper.dll",
        "ggml-cuda.dll",
        "cublas64_12.dll",
        "cublasLt64_12.dll",
        "cudart64_12.dll",
        "cudnn64_9.dll",
        "zlibwapi.dll",
    ]
    present = []
    for name in names:
        candidate = root / name
        if candidate.exists():
            present.append(name)
    return present


def write_startup_diagnostics(args: argparse.Namespace) -> None:
    write_diagnostic(
        "startup",
        requested_device=args.device,
        requested_compute_type=args.compute_type,
        model_path=args.model,
        audio_path=args.audio,
        output_json_path=args.output_json,
        koenote_asr_tools_dir=os.environ.get("KOENOTE_ASR_TOOLS_DIR"),
        path_head=os.environ.get("PATH", "").split(os.pathsep)[:5],
        asr_tools_files=collect_asr_tools_files(),
    )

    try:
        import ctranslate2

        cuda_device_count = None
        cuda_available = None
        try:
            cuda_device_count = ctranslate2.get_cuda_device_count()
            cuda_available = cuda_device_count > 0
        except Exception as exc:
            cuda_available = False
            cuda_device_count = f"error: {type(exc).__name__}: {exc}"

        write_diagnostic(
            "runtime",
            faster_whisper_version=package_version("faster-whisper"),
            ctranslate2_version=package_version("ctranslate2"),
            ctranslate2_cuda_available=cuda_available,
            ctranslate2_cuda_device_count=cuda_device_count,
            supported_compute_types_auto=safe_supported_compute_types("auto"),
            supported_compute_types_cpu=safe_supported_compute_types("cpu"),
            supported_compute_types_cuda=safe_supported_compute_types("cuda"),
        )
    except Exception as exc:
        write_diagnostic("runtime_probe_failed", error=f"{type(exc).__name__}: {exc}")


def safe_supported_compute_types(device: str) -> object:
    try:
        import ctranslate2

        supported = ctranslate2.get_supported_compute_types(device)
        if isinstance(supported, set):
            return sorted(str(item) for item in supported)
        if isinstance(supported, tuple):
            return [str(item) for item in supported]
        if isinstance(supported, list):
            return [str(item) for item in supported]
        return str(supported)
    except Exception as exc:
        return f"error: {type(exc).__name__}: {exc}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--audio", required=True)
    parser.add_argument("--model", required=True)
    parser.add_argument("--output-json", required=True)
    parser.add_argument("--language", default="ja")
    parser.add_argument("--device", default="auto")
    parser.add_argument("--compute-type", default="auto")
    parser.add_argument("--local-files-only", action="store_true")
    parser.add_argument("--context", default=None)
    parser.add_argument("--hotword", action="append", default=[])
    parser.add_argument("--chunk-length", type=int, default=None)
    parser.add_argument("--condition-on-previous-text", choices=["true", "false"], default="true")
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

    add_windows_dll_directories()
    write_startup_diagnostics(args)

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

    transcribe_options = {
        "language": args.language,
        "initial_prompt": initial_prompt,
        "vad_filter": True,
        "condition_on_previous_text": args.condition_on_previous_text == "true",
    }
    if args.chunk_length is not None and args.chunk_length > 0:
        transcribe_options["chunk_length"] = args.chunk_length

    write_diagnostic(
        "model_load_start",
        requested_device=args.device,
        requested_compute_type=args.compute_type,
        local_files_only=args.local_files_only,
    )

    model = WhisperModel(
        args.model,
        device=args.device,
        compute_type=args.compute_type,
        local_files_only=args.local_files_only)
    write_diagnostic(
        "model_loaded",
        model_device=str(getattr(model, "device", "unknown")),
        model_compute_type=str(getattr(model, "compute_type", "unknown")),
    )
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
