#!/usr/bin/env python3
# Generated with DeepSeek, i don't give a fuck
import subprocess
import os
import json

def convert_to_mono(input_path):
    """Convert stereo audio file to mono using ffmpeg"""
    try:
        # Create backup (we'll restore if PR creator doesn't want changes)
        backup_path = input_path + '.backup'
        os.rename(input_path, backup_path)

        # Convert to mono
        cmd = [
            'ffmpeg',
            '-i', backup_path,
            '-ac', '1',  # Mono
            '-c:a', 'libvorbis',
            '-q:a', '5',  # Good quality
            '-y',  # Overwrite
            input_path
        ]

        print(f"Converting {input_path} to mono...")
        result = subprocess.run(cmd, capture_output=True, text=True, check=True)

        # Verify conversion worked
        verify_cmd = [
            'ffprobe',
            '-v', 'error',
            '-select_streams', 'a:0',
            '-show_entries', 'stream=channels',
            '-of', 'default=noprint_wrappers=1:nokey=1',
            input_path
        ]
        verify_result = subprocess.run(verify_cmd, capture_output=True, text=True, check=True)
        new_channels = int(verify_result.stdout.strip())

        if new_channels == 1:
            print(f"✅ Successfully converted to mono")
            os.remove(backup_path)
            return True
        else:
            print(f"❌ Conversion failed, still {new_channels} channels")
            os.rename(backup_path, input_path)
            return False

    except Exception as e:
        print(f"Error converting {input_path}: {e}")
        # Restore backup if exists
        if os.path.exists(backup_path):
            os.rename(backup_path, input_path)
        return False

def main():
    import sys

    if len(sys.argv) < 2:
        print("Usage: python convert_to_mono.py <file1> <file2> ...")
        sys.exit(1)

    files_to_convert = sys.argv[1:]
    converted_files = []

    for file_path in files_to_convert:
        if os.path.exists(file_path):
            if convert_to_mono(file_path):
                converted_files.append(file_path)
        else:
            print(f"File not found: {file_path}")

    # Output for GitHub Actions
    if converted_files:
        print(f"::set-output name=converted_files::{json.dumps(converted_files)}")
        print(f"::set-output name=converted_count::{len(converted_files)}")

if __name__ == '__main__':
    main()
