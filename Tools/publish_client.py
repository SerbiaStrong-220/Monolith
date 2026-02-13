#!/usr/bin/env python3

import argparse
import requests
import os
import subprocess
from typing import Iterable

PUBLISH_TOKEN : str = os.environ["PUBLISH_TOKEN"]
ROBUST_CDN_URL: str = os.environ["CDN_URL"]
FORK_ID       : str = os.environ["FORK_ID"]
VERSION       : str = os.environ["GITHUB_SHA"]


RELEASE_DIR     : str = "release"
CLIENT_FILE_NAME: str = "SS14.Client.zip"

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fork-id", default=FORK_ID)

    args = parser.parse_args()
    fork_id = args.fork_id

    session = requests.Session()
    session.headers = {
        "Authorization": f"Bearer {PUBLISH_TOKEN}",
    }

    print(f"Starting publish on Robust.Cdn for version {VERSION}")

    data = {
        "version": VERSION,
        "engineVersion": get_engine_version(),
    }
    headers = {
        "Content-Type": "application/json"
    }
    resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/start", json=data, headers=headers)
    resp.raise_for_status()
    print("Publish successfully started!...")
    
    file = os.path.join(RELEASE_DIR, CLIENT_FILE_NAME)
    print(f"Publishing {file} ...")
    
    with open(file, "rb") as f:
        headers = {
            "Content-Type": "application/octet-stream",
            "Robust-Cdn-Publish-File": os.path.basename(file),
            "Robust-Cdn-Publish-Version": VERSION
        }
        resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/file", data=f, headers=headers)
    resp.raise_for_status()
    print("Successfully pushed files, finishing publish...")

    data = {
        "version": VERSION
    }
    headers = {
        "Content-Type": "application/json"
    }
    resp = session.post(f"{ROBUST_CDN_URL}fork/{fork_id}/publish/finish", json=data, headers=headers)
    resp.raise_for_status()

    print("SUCCESS!")

def get_engine_version() -> str:
    proc = subprocess.run(["git", "describe","--tags", "--abbrev=0"], stdout=subprocess.PIPE, cwd="RobustToolbox", check=True, encoding="UTF-8")
    tag = proc.stdout.strip()
    assert tag.startswith("v")
    return tag[1:] # Cut off v prefix.

if __name__ == '__main__':
    main()
