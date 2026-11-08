#!/usr/bin/env python3
"""Verify captured UI motion and create a visual preview.

Example (run from the repository root):
    python scripts/verify_motion_frames.py --directory artifacts/private/motion-smoke \
        --orbit 650,150,300,280 --circuit 900,790,500,60 --mp4

Required inputs are motion-000.png through motion-029.png and the pairs
paused-0.png / paused-1.png, reduced-0.png / reduced-1.png, and
economy-0.png / economy-1.png. Active frames are captures, not a measurement of
application frame rate. The generated preview plays at the requested rate.
An optional complete circuit-000.png through circuit-029.png sequence verifies
an isolated rendering of the same live circuit visual when cards occlude it.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

import numpy as np
from PIL import Image


REPOSITORY = Path(__file__).resolve().parents[1]
DEFAULT_DIRECTORY = REPOSITORY / "artifacts" / "private" / "motion-smoke"
DEFAULT_GIF = REPOSITORY / "artifacts" / "VL-Optimizer-animacoes.gif"
DEFAULT_MP4 = REPOSITORY / "artifacts" / "VL-Optimizer-animacoes.mp4"
EXPECTED_ACTIVE_FRAMES = 30
CHANGE_THRESHOLD = 2


def rectangle(value: str) -> tuple[int, int, int, int]:
    """Parse x,y,width,height without silently clipping an invalid region."""
    try:
        parts = tuple(int(part.strip()) for part in value.split(","))
    except ValueError as error:
        raise argparse.ArgumentTypeError("Use x,y,width,height with integer values.") from error
    if len(parts) != 4 or min(parts[:2]) < 0 or min(parts[2:]) <= 0:
        raise argparse.ArgumentTypeError("Use nonnegative x,y and positive width,height.")
    return parts  # type: ignore[return-value]


def arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--directory", type=Path, default=DEFAULT_DIRECTORY, help="Directory containing the PNG captures.")
    parser.add_argument("--orbit", type=rectangle, default=(650, 150, 300, 280), metavar="X,Y,W,H")
    parser.add_argument("--circuit", type=rectangle, default=(900, 790, 500, 60), metavar="X,Y,W,H")
    parser.add_argument("--json", type=Path, help="JSON output path; default: DIRECTORY/motion-verification.json.")
    parser.add_argument("--gif", type=Path, default=DEFAULT_GIF, help="Animated preview output path.")
    parser.add_argument("--no-gif", action="store_true", help="Only verify frames; do not create the GIF.")
    parser.add_argument("--mp4", type=Path, nargs="?", const=DEFAULT_MP4, help="Also encode an H.264 preview; an optional path may follow.")
    parser.add_argument("--preview-width", type=int, default=1100, help="Preview width in pixels (default: 1100).")
    parser.add_argument("--preview-fps", type=float, default=10.0, help="Preview playback rate, not measured application FPS (default: 10).")
    args = parser.parse_args()
    if args.preview_width < 2:
        parser.error("--preview-width must be at least 2.")
    if not 0 < args.preview_fps <= 100:
        parser.error("--preview-fps must be greater than 0 and no greater than 100.")
    return args


def load_inputs(directory: Path) -> tuple[list[Path], dict[str, tuple[Path, Path]], list[Path], dict[Path, np.ndarray]]:
    active = [directory / f"motion-{index:03d}.png" for index in range(EXPECTED_ACTIVE_FRAMES)]
    pairs = {mode: (directory / f"{mode}-0.png", directory / f"{mode}-1.png") for mode in ("paused", "reduced", "economy")}
    required = active + [path for pair in pairs.values() for path in pair]
    missing = [path.name for path in required if not path.is_file()]
    if missing:
        raise ValueError("Missing captures: " + ", ".join(missing))
    extra = [path.name for path in directory.glob("motion-*.png") if re.fullmatch(r"motion-\d+\.png", path.name) and path not in active]
    if extra:
        raise ValueError("Unexpected active captures; expected exactly 000 through 029: " + ", ".join(sorted(extra)))

    isolated = [directory / f"circuit-{index:03d}.png" for index in range(EXPECTED_ACTIVE_FRAMES)]
    present = [path.is_file() for path in isolated]
    if any(present) and not all(present):
        raise ValueError("Incomplete isolated circuit sequence: " + ", ".join(path.name for path, exists in zip(isolated, present) if not exists))
    if not any(present):
        isolated = []

    images: dict[Path, np.ndarray] = {}
    # The isolated visual can have its own dimensions, but each series must be
    # internally consistent. Still-state captures always cover the full shell.
    for group in (required, isolated):
        first_shape: tuple[int, ...] | None = None
        for path in group:
            with Image.open(path) as image:
                pixels = np.asarray(image.convert("RGB"), dtype=np.uint8)
            if first_shape is None:
                first_shape = pixels.shape
            elif pixels.shape != first_shape:
                raise ValueError(f"Image size mismatch: {path.name} has {pixels.shape}; expected {first_shape}.")
            images[path] = pixels
    return active, pairs, isolated, images


def validate_regions(regions: dict[str, tuple[int, int, int, int]], shape: tuple[int, ...]) -> None:
    height, width = shape[:2]
    for name, (x, y, region_width, region_height) in regions.items():
        if x + region_width > width or y + region_height > height:
            raise ValueError(f"ROI {name} ({x},{y},{region_width},{region_height}) exceeds image bounds {width}x{height}.")


def difference(first: np.ndarray, second: np.ndarray, region: tuple[int, int, int, int] | None = None) -> dict[str, Any]:
    if region is not None:
        x, y, width, height = region
        first, second = first[y : y + height, x : x + width], second[y : y + height, x : x + width]
    delta = np.abs(first.astype(np.int16) - second.astype(np.int16))
    pixel_delta = delta.max(axis=2)
    changed = int(np.count_nonzero(pixel_delta > CHANGE_THRESHOLD))
    exact_changed = int(np.count_nonzero(pixel_delta))
    count = int(pixel_delta.size)
    return {
        "mean_absolute_difference_rgb": round(float(delta.mean()), 8),
        "maximum_channel_difference": int(delta.max()),
        "changed_pixels_gt_2": changed,
        "changed_fraction_gt_2": round(changed / count, 8),
        "exact_changed_pixels": exact_changed,
        "pixel_count": count,
        "exact_equal": exact_changed == 0,
    }


def active_region_summary(comparisons: list[dict[str, Any]], region: str) -> dict[str, Any]:
    metrics = [comparison["regions"][region] for comparison in comparisons]
    changing_pairs = sum(item["changed_pixels_gt_2"] > 0 for item in metrics)
    return {
        "varies": changing_pairs > 0,
        "adjacent_pairs": len(metrics),
        "pairs_with_changes_gt_2": changing_pairs,
        "mean_absolute_difference_rgb": round(float(np.mean([item["mean_absolute_difference_rgb"] for item in metrics])), 8),
        "maximum_mean_absolute_difference_rgb": max(item["mean_absolute_difference_rgb"] for item in metrics),
        "sum_changed_pixels_gt_2_across_pairs": sum(item["changed_pixels_gt_2"] for item in metrics),
        "maximum_changed_pixels_gt_2": max(item["changed_pixels_gt_2"] for item in metrics),
        "mean_changed_fraction_gt_2": round(float(np.mean([item["changed_fraction_gt_2"] for item in metrics])), 8),
    }


def verify(active: list[Path], pairs: dict[str, tuple[Path, Path]], images: dict[Path, np.ndarray], regions: dict[str, tuple[int, int, int, int]], isolated: list[Path] | None = None) -> dict[str, Any]:
    comparisons = []
    for index, (first, second) in enumerate(zip(active, active[1:])):
        comparison = {
            "from": first.name,
            "to": second.name,
            "regions": {name: difference(images[first], images[second], rectangle) for name, rectangle in regions.items()},
        }
        if isolated:
            comparison["regions"]["circuit_shell"] = comparison["regions"]["circuit"]
            comparison["regions"]["circuit"] = difference(images[isolated[index]], images[isolated[index + 1]])
            comparison["isolated_circuit_from"] = isolated[index].name
            comparison["isolated_circuit_to"] = isolated[index + 1].name
        comparisons.append(comparison)
    summary_names = list(regions) + (["circuit_shell"] if isolated else [])
    active_summary = {name: active_region_summary(comparisons, name) for name in summary_names}
    still_states: dict[str, Any] = {}
    for mode, (first, second) in pairs.items():
        still_states[mode] = {
            "from": first.name,
            "to": second.name,
            "full_frame": difference(images[first], images[second]),
            "regions": {name: difference(images[first], images[second], rectangle) for name, rectangle in regions.items()},
        }
    active_pass = all(active_summary[name]["varies"] for name in ("orbit", "circuit"))
    still_pass = all(state["full_frame"]["exact_equal"] for state in still_states.values())
    height, width = images[active[0]].shape[:2]
    return {
        "passed": active_pass and still_pass,
        "criteria": {
            "active_both_regions_vary_gt_2": active_pass,
            "paused_reduced_economy_exact_full_frame_equality": still_pass,
        },
        "CircuitSource": "isolated-render-of-same-live-visual" if isolated else "shell-circuit-roi",
        "image_size": {"width": width, "height": height},
        "threshold": {"rule": "A pixel changes when its maximum absolute RGB channel difference is greater than 2.", "value": CHANGE_THRESHOLD},
        "regions": {name: {"x": box[0], "y": box[1], "width": box[2], "height": box[3]} for name, box in regions.items()},
        "active": {"frame_count": len(active), "summary": active_summary, "adjacent_frame_comparisons": comparisons},
        "still_states": still_states,
        "limitations": [
            "Captured-frame differences confirm visible change, not measured application FPS, smoothness, or hardware performance gains.",
            "Still-state equality applies to the supplied pair of captures; it does not measure CPU or GPU use.",
            "With isolated circuit captures, the primary circuit metric covers their whole image. The circuit_shell metric still reports visible changes in the selected shell ROI but does not fail solely because cards occlude particles.",
        ],
    }


def preview_frames(active: list[Path], images: dict[Path, np.ndarray], width: int) -> list[Image.Image]:
    source_height, source_width = images[active[0]].shape[:2]
    height = max(2, round(source_height * width / source_width))
    return [Image.fromarray(images[path]).resize((width, height), Image.Resampling.LANCZOS) for path in active]


def save_gif(frames: list[Image.Image], destination: Path, fps: float) -> dict[str, Any]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    duration = max(10, round(1000 / fps / 10) * 10)
    # A shared palette prevents an otherwise static background from shimmering
    # just because each frame selected a slightly different GIF color table.
    shared_palette = frames[0].convert("P", palette=Image.Palette.ADAPTIVE, colors=256)
    palette_frames = [frame.quantize(palette=shared_palette, dither=Image.Dither.NONE) for frame in frames]
    palette_frames[0].save(destination, format="GIF", save_all=True, append_images=palette_frames[1:], duration=duration, loop=0, disposal=2, optimize=False)
    with Image.open(destination) as saved:
        saved_count = saved.n_frames
    return {"path": str(destination.resolve()), "width": frames[0].width, "height": frames[0].height, "encoded_frame_count": saved_count, "playback_frame_duration_ms": duration, "size_bytes": destination.stat().st_size}


def save_mp4(frames: list[Image.Image], destination: Path, fps: float) -> dict[str, Any]:
    import imageio_ffmpeg

    destination.parent.mkdir(parents=True, exist_ok=True)
    width = frames[0].width - frames[0].width % 2
    height = frames[0].height - frames[0].height % 2
    writer = imageio_ffmpeg.write_frames(
        str(destination), (width, height), fps=fps, codec="libx264", pix_fmt_in="rgb24", pix_fmt_out="yuv420p", macro_block_size=1,
        output_params=["-crf", "20", "-movflags", "+faststart"], ffmpeg_log_level="error",
    )
    writer.send(None)
    try:
        for frame in frames:
            if frame.size != (width, height):
                frame = frame.resize((width, height), Image.Resampling.LANCZOS)
            writer.send(np.asarray(frame, dtype=np.uint8))
    finally:
        writer.close()
    return {"path": str(destination.resolve()), "width": width, "height": height, "playback_fps": fps, "encoded_frame_count": len(frames), "size_bytes": destination.stat().st_size}


def main() -> int:
    args = arguments()
    try:
        directory = args.directory.resolve()
        active, pairs, isolated, images = load_inputs(directory)
        regions = {"orbit": args.orbit, "circuit": args.circuit}
        validate_regions(regions, images[active[0]].shape)
        report = verify(active, pairs, images, regions, isolated)
        if isolated:
            circuit_height, circuit_width = images[isolated[0]].shape[:2]
            report["isolated_circuit_image_size"] = {"width": circuit_width, "height": circuit_height}
        report["input_directory"] = str(directory)
        report["previews"] = {}
        preview_errors = []
        if not args.no_gif or args.mp4:
            frames = preview_frames(active, images, args.preview_width)
            if not args.no_gif:
                try:
                    report["previews"]["gif"] = save_gif(frames, args.gif, args.preview_fps)
                except Exception as error:
                    preview_errors.append("GIF: " + str(error))
            if args.mp4:
                try:
                    report["previews"]["mp4"] = save_mp4(frames, args.mp4, args.preview_fps)
                except Exception as error:
                    preview_errors.append("MP4: " + str(error))
        if preview_errors:
            report["preview_errors"] = preview_errors
        output = (args.json or directory / "motion-verification.json").resolve()
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"passed": report["passed"], "json": str(output), "CircuitSource": report["CircuitSource"], "active_summary": report["active"]["summary"], "still_states_exact_equal": {mode: state["full_frame"]["exact_equal"] for mode, state in report["still_states"].items()}, "previews": report["previews"], "preview_errors": preview_errors}, ensure_ascii=False, indent=2))
        return 0 if report["passed"] and not preview_errors else 1
    except (OSError, ValueError) as error:
        print("Motion verification could not run: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
