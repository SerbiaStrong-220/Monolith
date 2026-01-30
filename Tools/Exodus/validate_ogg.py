# Generated with DeepSeek, i don't give a fuck
import subprocess
import json
import os
import sys
from pathlib import Path

def check_audio_channels(file_path):
    """Check if an audio file is stereo using ffprobe"""
    try:
        cmd = [
            'ffprobe',
            '-v', 'error',
            '-select_streams', 'a:0',
            '-show_entries', 'stream=channels,duration,sample_rate',
            '-of', 'json',
            str(file_path)
        ]

        result = subprocess.run(cmd, capture_output=True, text=True, check=True)
        data = json.loads(result.stdout)

        if 'streams' in data and len(data['streams']) > 0:
            stream = data['streams'][0]
            return {
                'channels': stream.get('channels', 1),
                'duration': stream.get('duration', 'N/A'),
                'sample_rate': stream.get('sample_rate', 'N/A'),
                'valid': True
            }
    except Exception as e:
        print(f"Error checking {file_path}: {e}")

    return {'valid': False}

def find_ogg_files():
    """Find all OGG files in repository"""
    ogg_files = []
    for root, dirs, files in os.walk('.'):
        # Skip .git directory
        if '.git' in root.split(os.sep):
            continue
        for file in files:
            if file.lower().endswith('.ogg'):
                ogg_files.append(os.path.join(root, file))
    return ogg_files

def main():
    # Get changed files from command line arguments
    if len(sys.argv) > 1:
        changed_files = sys.argv[1:]
    else:
        # If no arguments, check all OGG files
        changed_files = find_ogg_files()

    stereo_files = []
    all_files_info = []

    for file_path in changed_files:
        if not os.path.exists(file_path):
            print(f"File not found: {file_path}")
            continue

        info = check_audio_channels(file_path)
        info['file'] = file_path
        all_files_info.append(info)

        if info['valid'] and info['channels'] > 1:
            stereo_files.append({
                'file': file_path,
                'channels': info['channels'],
                'duration': info['duration'],
                'sample_rate': info['sample_rate']
            })

    # Print summary
    print(f"Checked {len(all_files_info)} OGG file(s)")
    print(f"Found {len(stereo_files)} stereo file(s)")

    # Output in GitHub Actions format
    if stereo_files:
        print("\\n## Stereo OGG Files Found")
        print("The following files are stereo (multiple channels):")
        for sf in stereo_files:
            print(f"- **{sf['file']}**: {sf['channels']} channels, "
                f"{sf['duration']}s, {sf['sample_rate']}Hz")

        # Set output for GitHub Actions
        stereo_list = ' '.join([sf['file'] for sf in stereo_files])
        print(f"::set-output name=stereo_files::{stereo_list}")
        print(f"::set-output name=has_stereo::true")
    else:
        print("\\n✅ All OGG files are mono (single channel)")
        print(f"::set-output name=has_stereo::false")

    # Create JSON report
    report = {
        'total_checked': len(all_files_info),
        'stereo_files': stereo_files,
        'all_files': all_files_info
    }

    with open('ogg_validation_report.json', 'w') as f:
        json.dump(report, f, indent=2)

if __name__ == '__main__':
    main()
