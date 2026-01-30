#!/usr/bin/env python3
# Generated with DeepSeek, i don't give a fuck
import json
import os
from pathlib import Path

def env_bool(name, default="false"):
    return os.getenv(name, default).lower() == "true"

def env_int(name, default="0"):
    try:
        return int(os.getenv(name, default))
    except ValueError:
        return 0

has_stereo = env_bool("HAS_STEREO")
converted_count = env_int("CONVERTED_COUNT")
stereo_files = os.getenv("STEREO_FILES", "").split()

report_path = Path("ogg_validation_report.json")
report = {}

if report_path.exists():
    report = json.loads(report_path.read_text())

lines = []
lines.append("## 🎵 OGG Audio Validation Results\n")

if has_stereo:
    if converted_count > 0:
        lines.append("### ⚠️ Stereo Files Automatically Converted\n")
        lines.append(
            f"I found **{converted_count}** stereo OGG file(s) and automatically converted them to mono.\n"
        )
        lines.append("**Converted files:**")
        for f in stereo_files:
            lines.append(f"- \\`{f}\\`")

        lines.append(
            "### 📝 Next Steps\n\n"
            "1. **Review the changes**: The converted mono files have been automatically committed to your branch\n"
            "2. **Test the audio**: Ensure the mono conversion sounds good\n"
            "3. **If you need stereo**: Contact the maintainers to discuss if stereo is actually needed\n\n"
            "### 🔧 How to Convert OGG to Mono Locally\n\n"
            "**Using FFmpeg:**\n"
            "\\`\\`\\`bash\n"
            "ffmpeg -i input.ogg -ac 1 -c:a libvorbis -q:a 5 output.ogg\n"
            "\\`\\`\\`\n\n"
            "**Using Audacity:**\n"
            "1. Open the OGG file in Audacity\n"
            "2. Go to Tracks → Mix → Mix Stereo Down to Mono\n"
            "3. Export as OGG"
        )
    else:
        lines.append("### ❌ Stereo Files Found\n")
        lines.append(
            "I found stereo OGG file(s) in your PR. Only mono OGG files are allowed in this repository.\n"
        )
        lines.append("**Stereo files detected:**")

        for sf in report.get("stereo_files", []):
            lines.append(
                f"- \\`{sf['file']}\\`: {sf['channels']} channels, {sf['duration']}s, {sf['sample_rate']}Hz"
            )

        lines.append(
            "### 🔧 How to Fix\n\n"
            "1. **Using FFmpeg** (recommended):\n"
            "\\`\\`\\`bash\n"
            "ffmpeg -i stereo.ogg -ac 1 -c:a libvorbis -q:a 5 mono.ogg\n"
            "\\`\\`\\`\n\n"
            "2. Replace the file with the mono version\n"
            "3. Push the changes to your branch"
        )
else:
    total_checked = report.get("total_checked", 0)
    lines.append("### ✅ All Good!\n")
    lines.append(
        f"All OGG files in this PR are mono (single channel). No action needed.\n\n"
        f"**Checked files:** {total_checked}"
    )

lines.append(
    "---\n\n"
    "*This check ensures all OGG files are mono to maintain consistency and reduce file size.*  \n"
    "*Need stereo audio? Please discuss with maintainers first.*"
)

print("\n".join(lines))
