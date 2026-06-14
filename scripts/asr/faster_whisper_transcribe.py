#!/usr/bin/env python
"""Run faster-whisper and emit KoeNote-compatible JSON segments."""

from __future__ import annotations

import argparse
import gc
import importlib.metadata as metadata
import json
import os
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

NVIDIA_SMI_GPU_QUERY = "index,name,driver_version,memory.total,memory.free"
NVIDIA_SMI_COMPUTE_CAP_QUERY = "index,compute_cap"

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except AttributeError:
    pass


def add_windows_dll_directories() -> None:
    if os.name != "nt" or not hasattr(os, "add_dll_directory"):
        return

    candidates = []
    for env_name in ("KOENOTE_CTRANSLATE2_CUDA_DIR", "KOENOTE_ASR_TOOLS_DIR"):
        configured = os.environ.get(env_name)
        if configured:
            candidates.append(configured)

    script_directory = os.path.dirname(os.path.abspath(__file__))
    candidates.append(os.path.abspath(os.path.join(script_directory, "..", "..", "tools", "asr-ctranslate2-cuda")))
    candidates.append(os.path.abspath(os.path.join(script_directory, "..", "..", "tools", "asr")))

    seen = set()
    for candidate in candidates:
        normalized = os.path.normcase(os.path.abspath(candidate))
        if normalized in seen:
            continue
        seen.add(normalized)
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


def collect_dll_candidates() -> list[dict[str, object]]:
    patterns = {
        "cublas64_*.dll",
        "cublasLt64_*.dll",
        "cudart64_*.dll",
        "cudnn*.dll",
        "zlibwapi.dll",
        "ctranslate2*.dll",
        "_ext*.pyd",
    }
    roots: list[str] = []
    for env_name in ("KOENOTE_CTRANSLATE2_CUDA_DIR", "KOENOTE_ASR_TOOLS_DIR"):
        value = os.environ.get(env_name)
        if value:
            roots.append(value)
    roots.extend(os.environ.get("PATH", "").split(os.pathsep)[:20])

    candidates: list[dict[str, object]] = []
    seen: set[str] = set()
    for root_value in roots:
        root = Path(root_value)
        if not root.is_dir():
            continue

        for pattern in patterns:
            for candidate in root.glob(pattern):
                if not candidate.is_file():
                    continue
                key = str(candidate).lower()
                if key in seen:
                    continue

                seen.add(key)
                try:
                    size = candidate.stat().st_size
                except OSError:
                    size = None
                candidates.append({"name": candidate.name, "path": str(candidate), "size": size})
    return candidates


def parse_optional_int(value: str) -> int | None:
    try:
        return int(value.strip())
    except ValueError:
        return None


def parse_nvidia_smi_gpus(stdout: str, compute_caps: dict[int, str] | None = None) -> list[dict[str, object]]:
    gpus: list[dict[str, object]] = []
    for line in stdout.splitlines():
        values = [value.strip() for value in line.split(",")]
        if len(values) < 5:
            continue

        index, name, driver_version, memory_total, memory_free = values[:5]
        parsed_index = parse_optional_int(index)
        gpus.append({
            "index": parsed_index,
            "name": name,
            "driver_version": driver_version,
            "memory_total_mb": parse_optional_int(memory_total),
            "memory_free_mb": parse_optional_int(memory_free),
            "compute_capability": compute_caps.get(parsed_index) if compute_caps is not None and parsed_index is not None else None,
        })
    return gpus


def parse_nvidia_smi_compute_caps(stdout: str) -> dict[int, str]:
    values: dict[int, str] = {}
    for line in stdout.splitlines():
        parts = [value.strip() for value in line.split(",")]
        if len(parts) < 2:
            continue

        index = parse_optional_int(parts[0])
        if index is not None and parts[1]:
            values[index] = parts[1]
    return values


def run_nvidia_smi_query(query: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [
            "nvidia-smi",
            f"--query-gpu={query}",
            "--format=csv,noheader,nounits",
        ],
        capture_output=True,
        text=True,
        timeout=5,
        check=False,
    )


def resolve_selected_cuda_device_index(device: str) -> str | None:
    if device.lower() == "cpu":
        return None

    visible_devices = os.environ.get("CUDA_VISIBLE_DEVICES")
    if visible_devices:
        first_visible = next((item.strip() for item in visible_devices.split(",") if item.strip()), None)
        if first_visible:
            return first_visible

    return "0" if device.lower() == "cuda" else "auto"


def run_nvidia_smi_probe() -> dict[str, object]:
    try:
        result = run_nvidia_smi_query(NVIDIA_SMI_GPU_QUERY)
    except Exception as exc:
        return {"available": False, "error": f"{type(exc).__name__}: {exc}"}

    stdout = result.stdout.strip()
    compute_caps: dict[int, str] = {}
    compute_cap_error = None
    if result.returncode == 0:
        try:
            compute_cap_result = run_nvidia_smi_query(NVIDIA_SMI_COMPUTE_CAP_QUERY)
            if compute_cap_result.returncode == 0:
                compute_caps = parse_nvidia_smi_compute_caps(compute_cap_result.stdout.strip())
            else:
                compute_cap_error = compute_cap_result.stderr.strip()[:1000]
        except Exception as exc:
            compute_cap_error = f"{type(exc).__name__}: {exc}"

    return {
        "available": result.returncode == 0,
        "exit_code": result.returncode,
        "query": NVIDIA_SMI_GPU_QUERY,
        "compute_cap_query": NVIDIA_SMI_COMPUTE_CAP_QUERY,
        "compute_cap_error": compute_cap_error,
        "gpus": parse_nvidia_smi_gpus(stdout, compute_caps) if result.returncode == 0 else [],
        "stdout": stdout[:1000],
        "stderr": result.stderr.strip()[:1000],
    }


def should_probe_gpu_memory(args: argparse.Namespace) -> bool:
    device = args.device.lower()
    execution_profile = args.execution_profile.lower()
    return device == "cuda" or "cuda" in execution_profile


def write_gpu_memory_snapshot(stage: str, args: argparse.Namespace) -> None:
    if should_probe_gpu_memory(args):
        write_diagnostic("gpu_memory_snapshot", stage=stage, nvidia_smi=run_nvidia_smi_probe())


def write_phase_failure(event: str, exc: BaseException) -> None:
    write_diagnostic(event, error_type=type(exc).__name__, error=str(exc)[:1000])


def write_startup_diagnostics(args: argparse.Namespace) -> None:
    write_diagnostic(
        "startup",
        requested_device=args.device,
        requested_compute_type=args.compute_type,
        selected_cuda_device_index=resolve_selected_cuda_device_index(args.device),
        cuda_visible_devices=os.environ.get("CUDA_VISIBLE_DEVICES"),
        execution_profile=args.execution_profile,
        attempt_number=args.attempt_number,
        chunk_seconds=args.chunk_seconds,
        model_path=args.model,
        audio_path=args.audio,
        output_json_path=args.output_json,
        koenote_asr_tools_dir=os.environ.get("KOENOTE_ASR_TOOLS_DIR"),
        koenote_ctranslate2_cuda_dir=os.environ.get("KOENOTE_CTRANSLATE2_CUDA_DIR"),
        path_head=os.environ.get("PATH", "").split(os.pathsep)[:5],
        asr_tools_files=collect_asr_tools_files(),
    )

    write_diagnostic(
        "gpu_probe",
        nvidia_smi=run_nvidia_smi_probe(),
        dll_candidates=collect_dll_candidates(),
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
    parser.add_argument("--chunk-seconds", type=int, default=None)
    parser.add_argument("--condition-on-previous-text", choices=["true", "false"], default="true")
    parser.add_argument("--execution-profile", default="auto")
    parser.add_argument("--attempt-number", type=int, default=1)
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


def should_use_explicit_chunks(audio_path: str, chunk_seconds: int | None) -> bool:
    if chunk_seconds is None or chunk_seconds <= 0:
        return False

    return Path(audio_path).suffix.lower() == ".wav"


def transcribe_audio(
    model: object,
    audio_path: str,
    transcribe_options: dict[str, object],
    chunk_seconds: int | None,
) -> tuple[list[dict[str, object]], str | None]:
    if not should_use_explicit_chunks(audio_path, chunk_seconds):
        write_diagnostic("transcribe_start", mode="single", audio_path=audio_path)
        segments, info = model.transcribe(audio_path, **transcribe_options)
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
        detected_language = getattr(info, "language", None)
        del segments
        del info
        return segment_items, detected_language

    return transcribe_wav_chunks(model, audio_path, transcribe_options, int(chunk_seconds or 0))


def transcribe_wav_chunks(
    model: object,
    audio_path: str,
    transcribe_options: dict[str, object],
    chunk_seconds: int,
) -> tuple[list[dict[str, object]], str | None]:
    segment_items: list[dict[str, object]] = []
    detected_language: str | None = None

    with wave.open(audio_path, "rb") as source:
        frame_rate = source.getframerate()
        frame_count = source.getnframes()
        duration_seconds = frame_count / float(frame_rate or 1)
        if duration_seconds <= chunk_seconds:
            write_diagnostic(
                "transcribe_start",
                mode="single",
                audio_path=audio_path,
                duration_seconds=duration_seconds,
                chunk_seconds=chunk_seconds,
            )
            segments, info = model.transcribe(audio_path, **transcribe_options)
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
            detected_language = getattr(info, "language", None)
            del segments
            del info
            return segment_items, detected_language

        params = source.getparams()
        frames_per_chunk = max(1, int(frame_rate * chunk_seconds))
        write_diagnostic(
            "transcribe_start",
            mode="chunked",
            audio_path=audio_path,
            duration_seconds=duration_seconds,
            chunk_seconds=chunk_seconds,
            frame_rate=frame_rate,
        )

        with tempfile.TemporaryDirectory(prefix="koenote-asr-chunks-") as temp_dir:
            chunk_index = 0
            next_segment_id = 0
            while source.tell() < frame_count:
                start_frame = source.tell()
                frames = source.readframes(frames_per_chunk)
                if not frames:
                    break

                chunk_path = os.path.join(temp_dir, f"chunk-{chunk_index:04d}.wav")
                with wave.open(chunk_path, "wb") as chunk:
                    chunk.setparams(params)
                    chunk.writeframes(frames)

                offset_seconds = start_frame / float(frame_rate or 1)
                write_diagnostic(
                    "chunk_transcribe_start",
                    chunk_index=chunk_index,
                    offset_seconds=offset_seconds,
                    chunk_path=chunk_path,
                )
                segments, info = model.transcribe(chunk_path, **transcribe_options)
                chunk_count = 0
                for segment in segments:
                    segment_items.append({
                        "id": next_segment_id,
                        "start": offset_seconds + float(segment.start),
                        "end": offset_seconds + float(segment.end),
                        "text": segment.text.strip(),
                        "speaker": None,
                    })
                    next_segment_id += 1
                    chunk_count += 1

                if detected_language is None:
                    detected_language = getattr(info, "language", None)
                del segments
                del info
                release_gpu_memory()
                write_diagnostic(
                    "chunk_transcribe_done",
                    chunk_index=chunk_index,
                    segment_count=chunk_count,
                )
                chunk_index += 1

    return segment_items, detected_language


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
        selected_cuda_device_index=resolve_selected_cuda_device_index(args.device),
        local_files_only=args.local_files_only,
    )
    write_gpu_memory_snapshot("before_model_load", args)

    try:
        model = WhisperModel(
            args.model,
            device=args.device,
            compute_type=args.compute_type,
            local_files_only=args.local_files_only)
    except BaseException as exc:
        write_phase_failure("model_load_failed", exc)
        raise

    write_diagnostic(
        "model_loaded",
        model_device=str(getattr(model, "device", "unknown")),
        model_compute_type=str(getattr(model, "compute_type", "unknown")),
    )
    write_gpu_memory_snapshot("after_model_load", args)
    write_gpu_memory_snapshot("before_transcribe", args)

    try:
        segment_items, detected_language = transcribe_audio(
            model,
            args.audio,
            transcribe_options,
            args.chunk_seconds,
        )
    except BaseException as exc:
        write_phase_failure("transcribe_failed", exc)
        raise

    detected_language = detected_language or args.language
    write_diagnostic("transcribe_done", segment_count=len(segment_items), detected_language=detected_language)

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
